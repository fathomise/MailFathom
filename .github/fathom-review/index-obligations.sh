#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/fathomise/MailFathom

# Index the obligations a pull request triggers in the rest of the repository.
#
# `Fathom review` reads a change as a diff, and a whole class of defect here is invisible in one: a
# source file that changed while no test followed it, a page under `docs/` that still describes the
# behavior the change replaced, a moved pin with no row in `THIRD_PARTY_LICENSES.md`. The defect is
# the *absence* of a second file from the diff, so a reviewer reading only the diff cannot see it.
#
# This produces the list of second files. It reads the base checkout and the collected `files.json`
# and calls no API, which is what lets the contract suite run it against a fixture tree. Nothing it
# emits is a finding: it is where to look, and the reviewer confirms or drops each row against the
# code. A row that becomes a finding without that confirmation is a defect in the review.
#
# The two kinds of edge it follows are recorded differently on purpose.
#
# A production file to its test is *derived*, never recorded, and each stack states the rule the
# derivation reads. In the service, `AGENTS.md` requires one primary type per file and a file name
# that matches it, and `backend/tests/<Boundary>.UnitTests/` mirrors `backend/src/<Boundary>/`, so
# the edge is a type name searched for across the test tree. In the client,
# `frontend/tests/AGENTS.md` puts the test beside the source and names it after it, so the edge is
# one path rather than a search: `session.ts` is covered by `session.test.ts` in the same directory
# and `App.tsx` by `App.test.tsx`. Both mappings already exist as rules; a written-down copy could
# drift from one, and a derived one cannot.
#
# The two derivations are exact in different ways, and the section below says so per entry rather
# than leaving the reviewer to assume. A type name is a *search*, so it over-reports — a test naming
# `Result` in passing is listed as covering it — and `referencing_test_count` is the warning. A
# sibling path is a *lookup*, so it is either there or it is not, and an empty list under a client
# entry means exactly that no file with that name exists.
#
# A source path to the page that documents it is *declared*, because nothing derives it:
# documentation is written about configuration keys and behavior rather than about type names, so no
# name match finds the edge. Each page states its own subject in a `describes:` marker, which lives
# in the file somebody edits rather than in a central index that would go stale silently and
# conflict on every merge. `scripts/test-agent-workflow.sh` fails when a page carries no marker and
# when a marker names a pattern matching nothing, so both ways the declaration can rot are loud.
#
# Usage: index-obligations.sh <repository-root> <files.json> <output.json>

set -euo pipefail

repository_root="${1:?the repository root is required}"
files_json="${2:?the collected files.json is required}"
output_file="${3:?the output path is required}"

# Ceilings, for the reason every other ceiling in this workflow exists: the reviewer's context is
# finite, and an index long enough to exhaust it produces a silently partial review. What is left out
# is recorded in `notes` and reaches the review body through the reviewer's summary.
#
# The documentation section needs none of its own. It holds at most one entry per changed path, and
# the collection step already caps `files.json`, so a second limit here would be a branch nothing can
# reach — which is worse than no branch, because it reads as a bound somebody verified.
max_test_entries=80

# A per-entry ceiling as well as a per-section one, because the two bound different things. A type
# whose name is common — `Program`, `Result` — is matched as a whole word in every test that names
# it, and the count is a property of the name rather than of the change, so eighty entries of twenty
# references each is a different document from eighty entries of six hundred.
max_references_per_type=20

# How far into a page a `describes:` marker is looked for. `scripts/test-agent-workflow.sh` holds
# the same number and fails a page whose marker sits below it, so the bound is a rule rather than a
# scan that happens to stop early.
marker_preamble_lines=15

work_directory="$(mktemp -d)"
trap 'rm -rf "$work_directory"' EXIT

notes=()

# Every path the change touches, and the paths it touches with a status that leaves a file behind.
# `removed` is kept out of the second list because a deleted production file owes no test.
jq -r '.[].filename' "$files_json" | sort -u > "$work_directory/changed-paths"
jq -r '.[] | select(.status != "removed") | .filename' "$files_json" | sort -u > "$work_directory/present-paths"

path_is_changed() {
  grep -qxF "$1" "$work_directory/changed-paths"
}

# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------

# Migrations are excluded: `AGENTS.md` makes them append-only and generated, so a migration owes no
# unit test and a reviewer asked to check for one would raise the same wrong finding on every schema
# change.
grep -E '^backend/src/.+\.cs$' "$work_directory/present-paths" \
  | grep -vE '/Migrations/' \
  > "$work_directory/changed-sources" || true

# The type each changed file declares, which `AGENTS.md` guarantees is the file name. `sort -u`
# because two boundaries may both declare a type of the same name, and one pattern is enough to find
# either.
sed -E 's|.*/||; s|\.cs$||' "$work_directory/changed-sources" | sort -u > "$work_directory/type-names" || true

: > "$work_directory/type-references"

if [[ -s "$work_directory/type-names" ]]; then
  # One pass over the test tree rather than one grep per changed type. `-w` gives the whole-word
  # match that keeps `EmailSummary` from matching `StoredEmailSummary`, and `-F` keeps a type name
  # that happens to contain a regular-expression character from being read as a pattern.
  if [[ -d "$repository_root/backend/tests" ]]; then
    (
      cd "$repository_root"
      grep -roFw --include='*.cs' -f "$work_directory/type-names" backend/tests 2>/dev/null || true
    ) > "$work_directory/base-references"

    # `path:match` becomes `match<TAB>path`, which is the direction the lookup below reads.
    sed -E 's|^([^:]*):(.*)$|\2\t\1|' "$work_directory/base-references" >> "$work_directory/type-references" || true
  fi

  # A test the pull request adds is not in the base tree, so the base pass above cannot see it — and
  # a change that adds a class together with its test is the case where reporting a missing test
  # would be most obviously wrong. The added lines of every changed test carry the same reference,
  # so they are searched as one file of `path<TAB>line` records.
  jq -r '
    .[]
    | select(.filename | startswith("backend/tests/"))
    | select(.patch != null)
    | .filename as $path
    | .patch
    | split("\n")[]
    | select(startswith("+"))
    | "\($path)\t\(.)"
  ' "$files_json" > "$work_directory/added-test-lines" || true

  # The line alone is searched, never its path, so a type named by the directory a test sits in is
  # not read as a reference. `-n` numbers each match by the line it came from, which is how it finds
  # its way back to that line's path.
  if [[ -s "$work_directory/added-test-lines" ]]; then
    cut -f 2- "$work_directory/added-test-lines" > "$work_directory/added-test-line-texts"
    grep -noFw -f "$work_directory/type-names" "$work_directory/added-test-line-texts" 2>/dev/null \
      | awk -F'\t' '
          FILENAME == ARGV[1] { path[FNR] = $1; next }
          {
            separator = index($0, ":")
            print substr($0, separator + 1) "\t" path[substr($0, 1, separator - 1)]
          }
        ' "$work_directory/added-test-lines" - \
      >> "$work_directory/type-references" || true
  fi
fi

sort -u "$work_directory/type-references" -o "$work_directory/type-references"

# Every path is read into a set once, so the loops below decide membership without starting a
# process per path. Nothing from here on starts a process per item: on a busy machine starting one
# costs far more than the work it does, and a count that grows with the change turns seconds into
# minutes.
declare -A changed_path_set=() present_path_set=()

while IFS= read -r path; do
  [[ -n "$path" ]] || continue
  changed_path_set["$path"]=1
done < "$work_directory/changed-paths"

while IFS= read -r path; do
  [[ -n "$path" ]] || continue
  present_path_set["$path"]=1
done < "$work_directory/present-paths"

# The tests naming each type, in the order `sort -u` gives a type's own list: the sort keys on the
# path within a type, so the order is the one the list would have sorted alone.
declare -A reference_counts=()

sort -t $'\t' -k 1,1 -k 2,2 -u "$work_directory/type-references" > "$work_directory/references-by-type"

# The count is copied out of the array before any arithmetic, because a subscript inside `(( ))` is
# evaluated again, and a type name is a file name the pull request chose.
while IFS=$'\t' read -r type_name test_path; do
  reference_count="${reference_counts["$type_name"]:-0}"
  reference_count=$(( reference_count + 1 ))
  reference_counts["$type_name"]="$reference_count"
  if (( reference_count <= max_references_per_type )); then
    printf '%s\t%s\n' "$type_name" "$test_path"
  fi
done < "$work_directory/references-by-type" > "$work_directory/listed-references"

# One row per entry, `path, type, project, count`, and one row per covering test,
# `entry, test`, which the JSON is assembled from once at the end. `count` is empty for a client
# entry, whose count is the length of its list.
: > "$work_directory/test-entries"
: > "$work_directory/test-references"

test_entry_count=0

while IFS= read -r source_path; do
  [[ -n "$source_path" ]] || continue

  if (( test_entry_count >= max_test_entries )); then
    notes+=("The change touches more source files than the ${max_test_entries} this index covers, so the tests section is partial.")
    break
  fi

  type_name="${source_path##*/}"
  type_name="${type_name%.cs}"
  boundary="${source_path#backend/src/}"
  boundary="${boundary%%/*}"
  reference_count="${reference_counts["$type_name"]:-0}"

  if (( reference_count > max_references_per_type )); then
    notes+=("${type_name} is named by ${reference_count} tests and the first ${max_references_per_type} are listed; a name that common says little about what covers the behavior.")
  fi

  printf '%s\t%s\t%s\t%s\n' "$source_path" "$type_name" "backend/tests/${boundary}.UnitTests" "$reference_count" \
    >> "$work_directory/test-entries"

  test_entry_count=$(( test_entry_count + 1 ))
done < "$work_directory/changed-sources"

# Each backend entry's tests, joined on its type. The entries were written in order, so the row
# number is the entry's index in the final list.
awk -F'\t' '
  FILENAME == ARGV[1] { tests[$1] = tests[$1] $2 "\n"; next }
  {
    count = split(tests[$2], listed, "\n")
    for (position = 1; position < count; position++) print FNR "\t" listed[position]
  }
' "$work_directory/listed-references" "$work_directory/test-entries" >> "$work_directory/test-references"

# The client's half of the same section, into the same list and under the same ceiling, because what
# bounds it is the reviewer's context rather than which stack an entry came from.
#
# Only `frontend/src/<package>/src/` is production source. A package's `vite.config.ts`,
# `vitest.setup.ts`, and `tsconfig.json` sit above that directory, so scoping to it excludes the
# build's own configuration without naming any of it. Three kinds inside it are excluded for the
# reason migrations are excluded above — a test for one is not a thing that can exist, so an index
# that asked for one would produce the same wrong finding on every change of that kind: a test file
# itself, a `.d.ts` declaration, which carries no behavior to reach, and an `index.ts` or
# `index.tsx`, which re-exports and is proven by whether its consumers still compile.
grep -E '^frontend/src/[^/]+/src/.+\.(ts|tsx)$' "$work_directory/present-paths" \
  | grep -vE '\.test\.(ts|tsx)$' \
  | grep -vE '\.d\.ts$' \
  | grep -vE '(^|/)index\.(ts|tsx)$' \
  > "$work_directory/changed-client-sources" || true

# The test beside a client source, as a lookup rather than a search. It is either in the base tree or
# added by this change, and the second case is the one a base-tree pass alone would get wrong — a
# change adding a module together with its test is exactly where reporting a missing test is most
# obviously wrong. A candidate this change *removed* is neither: it is in the base tree and gone from
# the branch, so it is skipped rather than listed as covering anything.
while IFS= read -r source_path; do
  [[ -n "$source_path" ]] || continue

  if (( test_entry_count >= max_test_entries )); then
    notes+=("The change touches more source files than the ${max_test_entries} this index covers, so the tests section is partial.")
    break
  fi

  # The module rather than a type: a `.ts` file declares no single one, and what the file is named
  # after is what `frontend/tests/AGENTS.md` names the test after and what `describe` states.
  module_name="${source_path##*/}"
  module_name="${module_name%.tsx}"
  module_name="${module_name%.ts}"

  # The package the test belongs to, which is what `backend/tests/<Boundary>.UnitTests` is for the
  # service. A client test lives inside the package it covers rather than in one of its own, for the
  # resolver reason `frontend/tests/AGENTS.md` gives.
  client_package="${source_path#frontend/src/}"
  client_package="frontend/src/${client_package%%/*}"

  printf '%s\t%s\t%s\t\n' "$source_path" "$module_name" "$client_package" >> "$work_directory/test-entries"
  test_entry_count=$(( test_entry_count + 1 ))

  for extension in ts tsx; do
    candidate="${source_path%/*}/${module_name}.test.${extension}"

    if [[ -n "${changed_path_set["$candidate"]:-}" && -z "${present_path_set["$candidate"]:-}" ]]; then
      continue
    fi

    if [[ -f "$repository_root/$candidate" || -n "${present_path_set["$candidate"]:-}" ]]; then
      printf '%s\t%s\n' "$test_entry_count" "$candidate" >> "$work_directory/test-references"
    fi
  done
done < "$work_directory/changed-client-sources"

# ---------------------------------------------------------------------------
# Documentation
# ---------------------------------------------------------------------------

# A `describes:` pattern is matched as a regular expression over the path list, because no shell
# construct gives the two meanings this needs at once: `**` crosses directory separators and `*`
# does not, which is what makes `backend/src/*/*.csproj` mean the project files and `backend/src/**` mean everything
# under the boundary.
#
# What this has to agree with is git's own `:(glob)` pathspec, because that is what
# `scripts/test-agent-workflow.sh` resolves every marker against — so a pattern this converter reads
# more narrowly than git does is one the contract suite calls valid while the index silently skips
# the paths it covers.
#
# Agreeing means `**` bounded by slashes matches *zero* directories as well as many: git documents
# `a/**/b` as matching `a/b`, so `backend/src/**/*Options.cs` has to credit `backend/src/FooOptions.cs` and not only
# `backend/src/Host/Configuration/McpOptions.cs`. Turning the `**` alone into `.*` and leaving both slashes
# where they were would require a directory between them, so the slash is taken into the rewrite:
# `/**/` becomes `/(.*/)?` as one unit, and a leading `**/` becomes `(.*/)?` for the same reason at
# the front of a pattern.
#
# Each rewrite is parked as a byte no path contains and restored at the end, so the narrower pattern
# can never consume the wider one first.
#
# Only the preamble is read for a marker. A page that documents the convention writes the syntax out
# as an example — `docs/AGENTS.md` does — and a scan of the whole file would read that example as a
# declaration, so every path the example names would acquire a document that says nothing about it.
# The preamble is where a real marker goes and where an example does not: the longest one here is an
# ADR, whose front matter puts the heading on line 10. The first line in it carrying a marker is the
# one read, and every marker on that line counts.
#
# All of it is one `awk` over every page, because the edges are read on every run before a single
# changed path is, and a process per page and per pattern made that fixed cost most of the run.
: > "$work_directory/documentation-edges"

if [[ -d "$repository_root/docs" ]]; then
  (
    cd "$repository_root"
    find docs -type f -name '*.md' -print0 | xargs -0 -r awk -v preamble="$marker_preamble_lines" '
      function glob_to_regex(pattern) {
        gsub(/[.^$+(){}|\[\]\\]/, "\\\\&", pattern)
        sub(/^\*\*\//, "\003", pattern)
        gsub(/\/\*\*\//, "\002", pattern)
        gsub(/\*\*/, "\001", pattern)
        gsub(/\*/, "[^/]*", pattern)
        gsub(/\?/, "[^/]", pattern)
        gsub(/\001/, ".*", pattern)
        gsub(/\002/, "/(.*/)?", pattern)
        gsub(/\003/, "(.*/)?", pattern)
        return "^" pattern "$"
      }

      FNR == 1 { marker_found = 0 }
      marker_found || FNR > preamble { nextfile }

      {
        line = $0
        markers = 0
        patterns = ""
        while (match(line, /<!--[[:space:]]*describes:[^>]*-->/)) {
          marker = substr(line, RSTART, RLENGTH)
          line = substr(line, RSTART + RLENGTH)
          sub(/^<!--[[:space:]]*describes:[[:space:]]*/, "", marker)
          sub(/[[:space:]]*-->$/, "", marker)
          patterns = (markers++ ? patterns "\n" : "") marker
        }
        if (!markers) next
        marker_found = 1

        sub(/\n+$/, "", patterns)
        if (patterns == "none") next

        count = split(patterns, pieces, /[,\n]/)
        for (piece = 1; piece <= count; piece++) {
          pattern = pieces[piece]
          sub(/^[[:space:]]+/, "", pattern)
          sub(/[[:space:]]+$/, "", pattern)
          if (pattern != "") print FILENAME "\t" glob_to_regex(pattern)
        }
      }
    '
  ) > "$work_directory/documentation-edges"
fi

# Every changed path against every edge in one pass. The edges are the outer loop so each pattern is
# compiled once rather than once per path. A change to the documentation itself owes no
# documentation, and a marker that covered `docs/` would otherwise make every page describe every
# other one. The sort orders the pages under each path the way `sort -u` orders a list of them.
awk -F'\t' '
  FILENAME == ARGV[1] { if ($0 != "" && $0 !~ /^docs\//) path[++paths] = $0; next }
  $2 != "" {
    for (position = 1; position <= paths; position++)
      if (path[position] ~ $2) print position "\t" path[position] "\t" $1
  }
' "$work_directory/changed-paths" "$work_directory/documentation-edges" \
  | sort -t $'\t' -k 1,1n -k 3,3 -u \
  > "$work_directory/documentation-matches"

# Whether the change touched a file is recorded against that file rather than against the entry it
# belongs to. An entry-level flag would have to mean "any of these", and any-of-these answers the
# wrong question twice over: a change that updates one of two pages would read as having discharged
# its obligation to both, and a change that happens to touch an unrelated test naming the same type
# would read as having tested the behavior that moved.
read -r -d '' assemble_definitions <<'JQ' || true
def rows: split("\n") | map(select(. != "") | split("\t"));
($changed | split("\n") | map(select(. != "") | {key: ., value: true}) | from_entries) as $changed_set
| def flagged: {path: ., changed_by_this_pull_request: ($changed_set[.] // false)};
JQ

jq -nc \
  --rawfile changed "$work_directory/changed-paths" \
  --rawfile entries "$work_directory/test-entries" \
  --rawfile references "$work_directory/test-references" \
  --slurpfile files "$files_json" \
  "$assemble_definitions"'
  ($files[0] | reduce .[] as $file ({};
      if has($file.filename) then . else .[$file.filename] = $file.status end)) as $status
  | ($references | rows | reduce .[] as [$entry, $test] ({}; .[$entry] += [$test])) as $tests_by_entry
  | $entries | rows | to_entries[]
  | .value as [$path, $type, $project, $count]
  | ($tests_by_entry[.key + 1 | tostring] // []) as $tests
  | {path: $path, type: $type, status: ($status[$path] // "" | if type == "string" then . else tojson end),
     expected_test_project: $project,
     referencing_test_count: (if $count == "" then ($tests | length) else ($count | tonumber) end),
     referencing_tests: ($tests | map(flagged))}
' > "$work_directory/tests.ndjson"

jq -nc \
  --rawfile changed "$work_directory/changed-paths" \
  --rawfile matches "$work_directory/documentation-matches" \
  "$assemble_definitions"'
  $matches | rows
  | reduce .[] as [$position, $path, $document] ([];
      if length > 0 and .[length - 1].position == $position
      then .[length - 1].documents += [$document]
      else . + [{position: $position, path: $path, documents: [$document]}] end)
  | .[]
  | {path, describing_documents: (.documents | map(flagged))}
' > "$work_directory/documentation.ndjson"

# ---------------------------------------------------------------------------
# Registers
# ---------------------------------------------------------------------------

# A register is a file that has to gain a row when something else changes, and the pair is written
# out rather than derived because there is nothing to derive it from. Only a pair whose trigger
# actually moved is emitted, so an unrelated change produces an empty section rather than a list of
# obligations it never incurred.
: > "$work_directory/registers.ndjson"

emit_register() {
  local trigger_description="$1" register="$2" trigger_matched="$3"

  [[ "$trigger_matched" == 'true' ]] || return 0

  local register_changed='false'
  if path_is_changed "$register"; then
    register_changed='true'
  fi

  jq -nc \
    --arg trigger "$trigger_description" \
    --arg register "$register" \
    --argjson register_changed "$register_changed" \
    '{trigger: $trigger, register: $register, trigger_changed: true,
      register_changed: $register_changed}' \
    >> "$work_directory/registers.ndjson"
}

dependency_pins_changed='false'
if grep -qxF 'backend/Directory.Packages.props' "$work_directory/changed-paths" \
  || grep -qE '(^|/)packages\.lock\.json$' "$work_directory/changed-paths"; then
  dependency_pins_changed='true'
fi

emit_register 'a dependency pin moved in backend/Directory.Packages.props or a packages.lock.json' \
  'THIRD_PARTY_LICENSES.md' "$dependency_pins_changed"

# The client's pin families, as a pair of their own rather than folded into the one above, so the
# trigger names what actually moved. `frontend/AGENTS.md` puts the npm pin in each package's own
# `package.json` and the resolution in `frontend/pnpm-lock.yaml`; the desktop shell's crate pins
# sit in `frontend/src-tauri/Cargo.toml` with the resolution in `Cargo.lock` beside it. Both require
# the same register row a service pin does under ADR 0016, and both oblige one thing a service pin
# does not: the register records each of those graphs as a census, and `scripts/update-dependencies.sh`
# rewrites pins without recomputing one. So the survey catches a pin that is behind and the review
# catches a census that no longer describes what the lock file resolves.
client_npm_pins_changed='false'
if grep -qE '^frontend/(src/[^/]+/)?package\.json$' "$work_directory/changed-paths" \
  || grep -qxF 'frontend/pnpm-lock.yaml' "$work_directory/changed-paths"; then
  client_npm_pins_changed='true'
fi

client_pins_changed="$client_npm_pins_changed"
if grep -qE '^frontend/src-tauri/Cargo\.(toml|lock)$' "$work_directory/changed-paths"; then
  client_pins_changed='true'
fi

emit_register 'a client dependency pin moved in a frontend package.json, in frontend/pnpm-lock.yaml, or in the desktop shell'"'"'s Cargo.toml or Cargo.lock' \
  'THIRD_PARTY_LICENSES.md' "$client_pins_changed"

# The npm family alone obliges a second register, which is the only one in this repository that is
# not a file a reader consults: the bundle carries it. `pnpm build` copies
# `frontend/src/Client.App/public/` verbatim into the output, the image and every desktop package
# redistribute that output, and the notice reproduces the licence text of the packages inside it
# under names and versions of their own. So a moved npm pin can leave a published artifact naming a
# version it no longer carries, which no register row and no census would show.
#
# The crate family is excluded rather than forgotten. The desktop shell's crates are a separate
# component under separate terms, they reach no bundle, and `THIRD_PARTY_LICENSES.md` carries them as
# a closure row of their own — so a `Cargo.toml` bump owes the register above and nothing here.
emit_register 'an npm pin that can reach the client bundle moved in a frontend package.json or in frontend/pnpm-lock.yaml' \
  'frontend/src/Client.App/public/THIRD-PARTY-NOTICES.txt' "$client_npm_pins_changed"

exception_added='false'
if jq -e '[.[] | select(.status == "added") | select(.filename | test("^backend/src/.*Exception\\.cs$"))] | length > 0' \
     "$files_json" > /dev/null; then
  exception_added='true'
fi

emit_register 'a new exception type was added under backend/src/' \
  'backend/src/Domain/Failures/MailFathomErrorCode.cs' "$exception_added"

# ---------------------------------------------------------------------------

: > "$work_directory/notes"

if (( ${#notes[@]} > 0 )); then
  printf '%s\n' "${notes[@]}" > "$work_directory/notes"
fi

notes_json="$(jq -R . < "$work_directory/notes" | jq -sc .)"

jq -n \
  --slurpfile tests "$work_directory/tests.ndjson" \
  --slurpfile documentation "$work_directory/documentation.ndjson" \
  --slurpfile registers "$work_directory/registers.ndjson" \
  --argjson notes "$notes_json" \
  '{tests: $tests, documentation: $documentation, registers: $registers, notes: $notes}' \
  > "$output_file"

printf 'Indexed %s source files, %s described paths, and %s register obligations.\n' \
  "$(jq '.tests | length' "$output_file")" \
  "$(jq '.documentation | length' "$output_file")" \
  "$(jq '.registers | length' "$output_file")"
