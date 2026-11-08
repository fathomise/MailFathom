#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/fathomise/MailFathom

set -euo pipefail

### Folds a release cycle's migrations into one migration for that release.
#
# Usage:
#   scripts/squash-migrations.sh <version> <previous-release-tag>             every migration added since that tag
#   scripts/squash-migrations.sh <version> <previous-release-tag> <through>   only those up to the newest one at <through>
#   scripts/squash-migrations.sh <version> none <through>                     every migration up to the newest one at <through>
#
# `$prepare-release` runs the first form: `scripts/squash-migrations.sh 0.9.0 v0.8.0` folds every migration added after
# the newest one `v0.8.0` carries. The last form folded the baseline: `scripts/squash-migrations.sh 0.8.0 none v0.8.0`.
#
# The fold is a concatenation, never a regeneration. Many migrations carry hand-written SQL — role seeding, data
# rewrites, backfills — that `dotnet ef migrations add` would not reproduce, and a release migration applied over the
# previous release's data has to run the same steps in the same order. So each folded migration keeps its own class,
# nested in the release migration with its comments and its helpers, and the release migration's Up runs their Up in the
# order they were added while its Down runs their Down in reverse.
#
# The release migration takes the identifier — the string in [Migration("…")] — of the last migration it folds, and its
# Designer file is that migration's, which already carries the model at the end of the range. A database that recorded
# that identifier skips the release migration; one that recorded none of the range applies it whole. ADR 0050 records
# why, and which upgrades that leaves unsupported.
#
# The result is not proven equivalent here. EF Core generates every folded operation against the model at the end of
# the range rather than against its own, which can change the SQL; scripts/compare-migration-squash.sh is what shows
# whether it did, and what it reports is fixed in the generated file before anything is committed.

readonly migrations_directory='backend/src/Infrastructure/Persistence/Migrations'

if ! repository_root="$(git rev-parse --show-toplevel 2>/dev/null)"; then
  printf 'squash-migrations.sh must run inside a Git worktree.\n' >&2
  exit 1
fi

cd "$repository_root"

fail() {
  printf '%s\n' "$1" >&2
  exit 1
}

version="${1:-}"
previous_release="${2:-}"
through="${3:-}"

if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ || -z "$previous_release" ]]; then
  fail 'Usage: scripts/squash-migrations.sh <version> <previous-release-tag|none> [<through>]'
fi

release_class="Release_${version//./_}"

# The identifier EF Core records is the attribute on the Designer file, never the file name: a release migration is named
# after its release and carries the identifier of the last migration it folds.
migration_identifier_of() {
  sed -n 's/^ *\[Migration("\([^"]*\)")\].*/\1/p' | head -n 1
}

newest_identifier_at() {
  local revision="$1" newest_designer

  newest_designer="$(
    git ls-tree --name-only "$revision" "$migrations_directory/" | grep '_.*\.Designer\.cs$' | sort | tail -n 1
  )" || fail "No migration exists at $revision."

  git show "$revision:$newest_designer" | migration_identifier_of
}

lower_bound=''
if [[ "$previous_release" != 'none' ]]; then
  lower_bound="$(newest_identifier_at "$previous_release")"
fi

upper_bound=''
if [[ -n "$through" ]]; then
  upper_bound="$(newest_identifier_at "$through")"
fi

mapfile -t migration_files < <(
  find "$migrations_directory" -maxdepth 1 -name '*_*.cs' ! -name '*.Designer.cs' -printf '%f\n' | sort
)

folded_stems=()
folded_classes=()
for file in "${migration_files[@]}"; do
  stem="${file%.cs}"
  designer="$migrations_directory/$stem.Designer.cs"
  [[ -f "$designer" ]] || fail "$file has no Designer file, so its identifier cannot be read."

  identifier="$(migration_identifier_of < "$designer")"
  [[ -n "$identifier" ]] || fail "$stem.Designer.cs carries no [Migration] attribute."

  if [[ -n "$lower_bound" && ! "$identifier" > "$lower_bound" ]]; then
    continue
  fi

  if [[ -n "$upper_bound" && "$identifier" > "$upper_bound" ]]; then
    continue
  fi

  class_name="$(sed -n 's/^    public partial class \([A-Za-z0-9_]*\) : Migration$/\1/p' "$migrations_directory/$file")"
  [[ -n "$class_name" ]] || fail "$file declares no 'public partial class <Name> : Migration' at the expected indentation."

  folded_stems+=("$stem")
  folded_classes+=("$class_name")
  last_identifier="$identifier"
done

if [[ "${#folded_stems[@]}" -eq 0 ]]; then
  fail 'No migration falls in the range, so there is nothing to fold.'
fi

if [[ -n "$upper_bound" && "$last_identifier" != "$upper_bound" ]]; then
  fail "The range ends at $last_identifier, but $through ends at $upper_bound, which this tree no longer carries."
fi

timestamp="${last_identifier%%_*}"
release_stem="${timestamp}_${release_class}"
release_file="$migrations_directory/$release_stem.cs"
release_designer="$migrations_directory/$release_stem.Designer.cs"
last_stem="${folded_stems[-1]}"

if [[ -e "$release_file" ]]; then
  fail "$release_file already exists."
fi

# One folded migration, nested: its class becomes a private static one, Up and Down become static methods the release
# migration calls, and everything else — its comments, constants, and helpers — is kept as written. Each rewrite is
# asserted, so a migration whose shape this does not recognize stops the fold rather than being half carried.
nest_migration() {
  local file="$1" class_name="$2" body

  body="$(
    awk '
      /^namespace / { in_namespace = 1; next }
      in_namespace && !opened && /^\{$/ { opened = 1; next }
      opened { lines[++count] = $0 }
      END { for (index_ = 1; index_ < count; index_++) print lines[index_] }
    ' "$file"
  )"

  [[ "$(tail -n 1 <<< "$body")" == '    }' ]] || fail "$file does not end its class where expected."

  # A nested class inherits nothing to document, so every <inheritdoc /> becomes a summary of its own: a remarks block
  # written beside one would otherwise be left with no summary, which the analyzers refuse.
  body="$(
    perl -0pe 's{^        /// <inheritdoc />\n((?:        ///.*\n)*)(        protected override void (Up|Down)\()}{"        /// <summary>" . ($3 eq "Up" ? "Applies" : "Reverts") . " the migration.</summary>\n$1$2"}gme' <<< "$body" |
      sed \
        -e "s|^    /// <inheritdoc />\$|    /// <summary>The <c>$class_name</c> migration, as it was added.</summary>|" \
        -e "s/^    public partial class $class_name : Migration\$/    private static class $class_name/" \
        -e 's/^        protected override void Up(MigrationBuilder migrationBuilder)$/        internal static void Up(MigrationBuilder migrationBuilder)/' \
        -e 's/^        protected override void Down(MigrationBuilder migrationBuilder)$/        internal static void Down(MigrationBuilder migrationBuilder)/'
  )"

  if grep --quiet --fixed-strings '<inheritdoc' <<< "$body"; then
    fail "$file: an <inheritdoc /> is left where a nested class has nothing to inherit."
  fi

  for rewritten in "    private static class $class_name" \
    '        internal static void Up(MigrationBuilder migrationBuilder)' \
    '        internal static void Down(MigrationBuilder migrationBuilder)'; do
    [[ "$(grep --count --fixed-strings --line-regexp -- "$rewritten" <<< "$body")" -eq 1 ]] ||
      fail "$file: expected exactly one '$rewritten' after nesting it."
  done

  sed 's/^\(.\)/    \1/' <<< "$body"
}

using_directives="$(
  for stem in "${folded_stems[@]}"; do
    grep '^using ' "$migrations_directory/$stem.cs"
  done | sort --unique
)"

{
  # The licensing header, from a migration rather than its Designer file, which is marked as generated and carries none.
  head -n 3 "$migrations_directory/$last_stem.cs"
  printf '\n'
  grep '^using System' <<< "$using_directives" || true
  grep -v '^using System' <<< "$using_directives" || true
  printf '\n#nullable disable\n\nnamespace MailFathom.Infrastructure.Persistence.Migrations\n{\n'
  printf '    /// <summary>The schema release %s carries: %d migrations, folded into one.</summary>\n' \
    "$version" "${#folded_stems[@]}"
  printf '    /// <remarks>\n'
  printf '    /// Written by <c>scripts/squash-migrations.sh</c>. It carries the identifier of the last migration it folds, so a\n'
  printf '    /// database that recorded that one skips it, and Up runs each folded migration'"'"'s Up in the order they were added\n'
  printf '    /// while Down runs their Down in reverse. Each folded migration is kept below as it was written.\n'
  printf '    /// </remarks>\n'
  printf '    public partial class %s : Migration\n    {\n' "$release_class"
  printf '        /// <inheritdoc />\n        protected override void Up(MigrationBuilder migrationBuilder)\n        {\n'
  for class_name in "${folded_classes[@]}"; do
    printf '            %s.Up(migrationBuilder);\n' "$class_name"
  done
  printf '        }\n\n'
  printf '        /// <inheritdoc />\n        protected override void Down(MigrationBuilder migrationBuilder)\n        {\n'
  for ((position = ${#folded_classes[@]} - 1; position >= 0; position--)); do
    printf '            %s.Down(migrationBuilder);\n' "${folded_classes[position]}"
  done
  printf '        }\n'
  for position in "${!folded_stems[@]}"; do
    printf '\n'
    nest_migration "$migrations_directory/${folded_stems[position]}.cs" "${folded_classes[position]}"
  done
  printf '    }\n}\n'
} > "$release_file"

last_class="${folded_classes[-1]}"
sed "s/^    partial class $last_class\$/    partial class $release_class/" \
  "$migrations_directory/$last_stem.Designer.cs" > "$release_designer"

grep --quiet --line-regexp --fixed-strings "    partial class $release_class" "$release_designer" ||
  fail "$last_stem.Designer.cs does not declare 'partial class $last_class' where expected."

for stem in "${folded_stems[@]}"; do
  rm -- "$migrations_directory/$stem.cs" "$migrations_directory/$stem.Designer.cs"
done

printf 'Folded %d migrations, %s through %s, into %s under the identifier %s.\n' \
  "${#folded_stems[@]}" "${folded_stems[0]}" "$last_stem" "$release_stem" "$last_identifier" >&2
printf 'Prove it with scripts/compare-migration-squash.sh before committing.\n' >&2
