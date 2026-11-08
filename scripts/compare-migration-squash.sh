#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/fathomise/MailFathom

set -euo pipefail

### Proves a migration fold produces a database nothing can tell from the one the individual migrations produce.
#
# Usage:
#   scripts/compare-migration-squash.sh <before> [<release>] [<output-directory>]
#
# <before> is the revision the fold was made on, usually origin/main, or a schema artifact already built from it. The
# tree checked out here is the fold. <release> is the release an existing installation upgrades from — a tag, or an
# artifact already downloaded — and defaults to the newest published release. <output-directory> keeps every artifact,
# dump, and difference for the pull request to report, and defaults to a fresh temporary directory.
#
# Five comparisons, each printed as one line, and the script fails when any of them does:
#
#   fresh-install   both artifacts applied to an empty database dump identically, schema and data, setting aside the
#                   migration history, which differs by design
#   idempotent      the fold's artifact applied a second time changes nothing
#   release-upgrade the release's artifact and then the fold's dumps identically to the same upgrade done with the
#                   artifact from before the fold, and to a fresh install of the fold
#   nightly-upgrade a database that recorded every individual migration finds nothing to apply in the fold's artifact
#   generated-sql   both artifacts carry the same statements in the same order once the history guards and the history
#                   rows are set aside
#
# What it needs is what building the artifact needs — the SDK and the pinned Aspire CLI — plus podman or docker to run
# the PostgreSQL image the app model declares, and gh to download the release. The container is this run's own, is
# reached through exec rather than a published port, and is removed when the script exits. The artifacts carry schema
# and seeded rows only, so nothing here holds mail or personal data.

readonly schema_builder='scripts/build-schema-artifact.sh'

if ! repository_root="$(git rev-parse --show-toplevel 2>/dev/null)"; then
  printf 'compare-migration-squash.sh must run inside a Git worktree.\n' >&2
  exit 1
fi

cd "$repository_root"

fail() {
  printf '%s\n' "$1" >&2
  exit 1
}

before="${1:-}"
release="${2:-}"
output_directory="${3:-$(mktemp --directory "${TMPDIR:-/tmp}/compare-migration-squash-XXXXXX")}"

[[ -n "$before" ]] || fail 'Usage: scripts/compare-migration-squash.sh <before> [<release>] [<output-directory>]'

if command -v podman > /dev/null 2>&1; then
  container_engine=podman
elif command -v docker > /dev/null 2>&1; then
  container_engine=docker
else
  fail 'Neither podman nor docker is on PATH, and the comparison applies the artifacts to a real PostgreSQL.'
fi

mkdir --parents "$output_directory"
output_directory="$(cd "$output_directory" && pwd)"

before_worktree=''
container=''
cleanup() {
  if [[ -n "$container" ]]; then
    "$container_engine" rm --force "$container" > /dev/null 2>&1 || true
  fi

  if [[ -n "$before_worktree" ]]; then
    git worktree remove --force "$before_worktree" > /dev/null 2>&1 || true
  fi
}
trap cleanup EXIT

printf 'Writing everything this run compares to %s.\n' "$output_directory" >&2

# Built with this tree's builder in both cases, so the two artifacts differ by the migrations and nothing else.
build_artifact() {
  local tree="$1" name="$2"

  (cd "$tree" && bash "$repository_root/$schema_builder" "$output_directory/$name" "$name" > /dev/null)
  find "$output_directory/$name" -maxdepth 1 -name '*.sql' -type f
}

if [[ -f "$before" ]]; then
  before_artifact="$before"
else
  before_revision="$(git rev-parse --verify "$before^{commit}")" || fail "$before is neither a file nor a revision."
  before_worktree="$(mktemp --directory "${TMPDIR:-/tmp}/compare-migration-squash-before-XXXXXX")"
  git worktree add --detach "$before_worktree" "$before_revision" > /dev/null 2>&1
  printf 'Building the artifact from before the fold, at %s.\n' "$before_revision" >&2
  before_artifact="$(build_artifact "$before_worktree" before)"
fi

printf 'Building the artifact of the fold, from this tree.\n' >&2
after_artifact="$(build_artifact "$repository_root" after)"

if [[ -f "$release" ]]; then
  release_artifact="$release"
else
  mkdir --parents "$output_directory/release"
  gh release download ${release:+"$release"} --repo fathomise/MailFathom \
    --pattern 'mailfathom-schema-*.sql*' --dir "$output_directory/release" --clobber
  (cd "$output_directory/release" && sha256sum --check --quiet ./*.sha256)
  release_artifact="$(find "$output_directory/release" -maxdepth 1 -name 'mailfathom-schema-*.sql' -type f)"
fi

image="$(sed -n 's:.*\.WithImage("\([^"]*\)").*:\1:p' backend/src/AppHost/Program.cs | head -n 1)"
tag="$(sed -n 's:.*\.WithImageTag("\([^"]*\)").*:\1:p' backend/src/AppHost/Program.cs | head -n 1)"
[[ -n "$image" && -n "$tag" ]] || fail 'No PostgreSQL image found in the app model.'

# The app model names the image the way Docker resolves it, and podman resolves no short name, so the registry Docker
# assumes is written out. Docker accepts the qualified name too.
if [[ "${image%%/*}" != *.* && "${image%%/*}" != *:* && "${image%%/*}" != 'localhost' ]]; then
  image="docker.io/$image"
fi

container="mailfathom-squash-comparison-$(od --read-bytes=6 --address-radix=n --format=x1 /dev/urandom | tr --delete ' ')"
"$container_engine" run --detach --name "$container" --env POSTGRES_PASSWORD=postgres "$image:$tag" > /dev/null

for _ in $(seq 60); do
  "$container_engine" exec "$container" pg_isready --host 127.0.0.1 --username postgres > /dev/null 2>&1 && break
  sleep 2
done
"$container_engine" exec "$container" pg_isready --host 127.0.0.1 --username postgres > /dev/null ||
  fail 'PostgreSQL did not become ready.'

psql_in() {
  "$container_engine" exec --interactive "$container" \
    psql --username postgres --dbname "$1" --set ON_ERROR_STOP=on --quiet > /dev/null
}

apply() {
  local database="$1" artifact="$2"

  psql_in "$database" < "$artifact" || fail "Applying $(basename "$artifact") to $database failed."
}

# Schema and data, without the migration history, which is the one table a fold changes by design. `\restrict` carries
# a random key on every dump, so it is the one line two dumps of one database differ in. A row a migration seeds with
# now() carries the moment it was applied, and a fold moves that moment — the folded migrations share one transaction
# where they had one each — so a timestamp in a data row is masked. A timestamp a migration writes literally is still
# compared, as part of the generated SQL below.
dump() {
  "$container_engine" exec "$container" \
    pg_dump --username postgres --no-owner --no-privileges --exclude-table='"__EFMigrationsHistory"' "$1" |
    grep --invert-match --extended-regexp '^\\(un)?restrict ' |
    awk '
      /^COPY / { in_data = 1; print; next }
      in_data && /^\\\.$/ { in_data = 0 }
      in_data { gsub(/[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9] [0-9][0-9]:[0-9][0-9]:[0-9][0-9](\.[0-9]+)?\+00/, "<applied at>") }
      { print }
    '
}

history_of() {
  "$container_engine" exec "$container" psql --username postgres --dbname "$1" --tuples-only --no-align \
    --command 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId"'
}

for database in fresh_before fresh_after release_before release_after nightly; do
  psql_in postgres <<< "CREATE DATABASE $database"
done

apply fresh_before "$before_artifact"
apply fresh_after "$after_artifact"
apply release_before "$release_artifact"
apply release_before "$before_artifact"
apply release_after "$release_artifact"
apply release_after "$after_artifact"
apply nightly "$before_artifact"

mkdir --parents "$output_directory/dumps"
for database in fresh_before fresh_after release_before release_after nightly; do
  dump "$database" > "$output_directory/dumps/$database.sql"
done
history_of nightly > "$output_directory/dumps/nightly-history-before.txt"

apply fresh_after "$after_artifact"
dump fresh_after > "$output_directory/dumps/fresh_after-reapplied.sql"
apply nightly "$after_artifact"
dump nightly > "$output_directory/dumps/nightly-after.sql"
history_of nightly > "$output_directory/dumps/nightly-history-after.txt"

failures=0
report() {
  local name="$1" difference="$2" detail="$3"

  if [[ -s "$difference" ]]; then
    printf '%-16s differs — %s, see %s\n' "$name" "$detail" "$difference"
    failures=$((failures + 1))
  else
    printf '%-16s identical — %s\n' "$name" "$detail"
    rm -- "$difference"
  fi
}

compare() {
  diff --unified "$1" "$2" > "$3" || true
}

mkdir --parents "$output_directory/differences"
differences="$output_directory/differences"
dumps="$output_directory/dumps"

compare "$dumps/fresh_before.sql" "$dumps/fresh_after.sql" "$differences/fresh-install.diff"
report fresh-install "$differences/fresh-install.diff" 'an empty database, before against after'

compare "$dumps/fresh_after.sql" "$dumps/fresh_after-reapplied.sql" "$differences/idempotent.diff"
report idempotent "$differences/idempotent.diff" 'the fold applied a second time'

{
  diff --unified "$dumps/release_before.sql" "$dumps/release_after.sql" || true
  diff --unified "$dumps/fresh_after.sql" "$dumps/release_after.sql" || true
} > "$differences/release-upgrade.diff"
report release-upgrade "$differences/release-upgrade.diff" \
  "$(basename "$release_artifact") then the fold, against the same upgrade before it and a fresh install"

{
  diff --unified "$dumps/nightly-history-before.txt" "$dumps/nightly-history-after.txt" || true
  diff --unified "$dumps/nightly.sql" "$dumps/nightly-after.sql" || true
} > "$differences/nightly-upgrade.diff"
report nightly-upgrade "$differences/nightly-upgrade.diff" \
  "every individual migration recorded, then the fold: $(wc -l < "$dumps/nightly-history-before.txt") history rows"

# The statements each artifact runs, with what is a property of the history rather than of the schema set aside: the
# guard each statement is wrapped in, the history table's own creation, the rows recording each migration, and the
# transaction around the script.
statements_of() {
  sed -e '1s/^\xef\xbb\xbf//' "$1" | awk '
    /^DO \$EF\$$/ { in_guard = 1; next }
    in_guard && /^BEGIN$/ { next }
    in_guard && /^    IF NOT EXISTS\(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = / { next }
    in_guard && /^    END IF;$/ { next }
    in_guard && /^END \$EF\$;$/ { in_guard = 0; next }
    /^ *INSERT INTO "__EFMigrationsHistory" / { skip_history = 1 }
    skip_history { if ($0 ~ /;$/) skip_history = 0; next }
    /^CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" / { skip_history_table = 1 }
    skip_history_table { if ($0 ~ /;$/) skip_history_table = 0; next }
    /^(START TRANSACTION|COMMIT);$/ { next }
    /^[[:space:]]*$/ { next }
    { print }
  '
}

statements_of "$before_artifact" > "$output_directory/before-statements.sql"
statements_of "$after_artifact" > "$output_directory/after-statements.sql"
compare "$output_directory/before-statements.sql" "$output_directory/after-statements.sql" \
  "$differences/generated-sql.diff"
report generated-sql "$differences/generated-sql.diff" \
  "$(wc -l < "$output_directory/after-statements.sql") lines of statements, guards and history rows set aside"

if [[ "$failures" -gt 0 ]]; then
  printf '%d of five comparisons differ, so the fold is not equivalent. Fix it in the release migration and run this again.\n' \
    "$failures" >&2
  exit 1
fi

printf 'All five comparisons are identical.\n' >&2
