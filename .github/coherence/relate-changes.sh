#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/fathomise/MailFathom

# Pair every path that landed under a pull request with every path the pull request changes that
# it is related to.
#
# Two changes git merges without a conflict can still disagree: a page one of them adds may describe
# a file the other one changes, or a rule one of them writes into an `AGENTS.md` may govern a file
# the other one changes without following it. No test sees either, so `Coherence` asks this instead,
# and a pair it returns is what somebody has to read before the pull request merges.
#
# A landed path and a changed path are related when
#
#   - they are the same prose file — a `*.md` anywhere, an `AGENTS.md` or an ADR among them, or
#     anything under `design/`. The same code file on both sides is not a pair: the checks a merge
#     queue runs on the combined tree are what judge it;
#   - either one is a page whose `describes:` marker covers the other;
#   - either one is an `AGENTS.md` whose directory contains the other;
#   - the lines one side added to a code file name the other side's code file, by its path or by a
#     file name no other file in the tree carries. That is a dependency the code states itself — a
#     script calling another, a comment saying which file holds the rule it follows — and the one a
#     parallel change can break with no test noticing. A test is never the file that names: it runs
#     on the combined tree, so what it depends on is already checked. Prose, generated files, and
#     lock files are on neither end of it, and both sides changing the same code file is still no
#     pair.
#
# The markers are read from the repository root given, which is the combined tree — the base with
# the pull request applied. That is what makes both orders a pair: a page that landed and describes
# a path this pull request changes, and a page this pull request adds that describes a path that
# landed. They are resolved by `.github/fathom-review/index-obligations.sh`, the one implementation
# of git's `:(glob)` semantics `Fathom review` already reads markers through, so a page the review
# says describes a path is a page this check pairs with it.
#
# It reads files and calls no API, which is what lets the contract suite run it against a fixture
# tree. The root has to be a git work tree, whose tracked and unignored files are what decides
# whether a file name is unique. Each side's added lines come as `git diff --unified=0` output; an
# empty file is a side whose lines are unknown, which relates nothing by naming. The pairs come out
# as a JSON array of `{landed, changed, reason}`, empty when nothing is related. The reason carries
# the direction, because the message a reader acts on has to say which side holds the page, the
# `AGENTS.md`, or the file that names the other: `same prose file`, `landed describes changed`,
# `changed describes landed`, `landed governs changed`, `changed governs landed`,
# `landed names changed`, or `changed names landed`.
#
# Usage: relate-changes.sh <repository-root> <landed-paths> <changed-paths> <landed-diff> <changed-diff> <output.json>

set -euo pipefail

repository_root="${1:?the repository root is required}"
landed_paths="${2:?the file listing the paths that landed is required}"
changed_paths="${3:?the file listing the paths the pull request changes is required}"
landed_diff="${4:?the diff of what landed is required}"
changed_diff="${5:?the diff of what the pull request changes is required}"
output_file="${6:?the output path is required}"

index_script="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/fathom-review/index-obligations.sh"

work_directory="$(mktemp -d)"
trap 'rm -rf "$work_directory"' EXIT

grep -v '^$' "$landed_paths" | sort -u > "$work_directory/landed" || true
grep -v '^$' "$changed_paths" | sort -u > "$work_directory/changed" || true

# Every path on either side goes to the index as one change, so its documentation section names the
# pages describing each of them. A page describing a landed path and a page describing a changed path
# are both in that answer, which is the whole of the relation in both directions.
sort -u "$work_directory/landed" "$work_directory/changed" \
  | jq -R . \
  | jq -s 'map({filename: ., status: "modified", patch: null})' \
  > "$work_directory/files.json"

bash "$index_script" "$repository_root" "$work_directory/files.json" "$work_directory/obligations.json" > /dev/null

jq -r '.documentation[] | .path as $described | .describing_documents[] | "\(.path)\t\($described)"' \
  "$work_directory/obligations.json" > "$work_directory/describes"

git -C "$repository_root" ls-files --cached --others --exclude-standard > "$work_directory/tree"

awk -F'\t' '
  function is_prose(path) {
    return path ~ /\.md$/ || path ~ /^design\//
  }

  function is_code(path) {
    return !is_prose(path) \
      && path !~ /(^|\/)Migrations\/|\.Designer\.cs$|\.g\.cs$|\/ci\/golden\/|\.d\.ts$/ \
      && path !~ /(^|\/)(pnpm-lock\.yaml|Cargo\.lock|packages\.lock\.json)$/
  }

  function is_test(path) {
    return path ~ /(^|\/)tests?\/|\.(test|spec)\.[a-z]+$|(^|\/)test-[^\/]*\.sh$|Tests?\.cs$/
  }

  function governs(instructions, path,    directory) {
    if (instructions !~ /(^|\/)AGENTS\.md$/) return 0
    directory = instructions
    sub(/\/?AGENTS\.md$/, "", directory)
    return directory == "" || index(path, directory "/") == 1
  }

  # A mention counts only as a whole name, so `run.sh` is not found inside `prerun.sh` or
  # `run.sh-old`. A slash may precede it, because a path is often written under a variable root.
  function mentions(text, name,    start, at, before, after) {
    start = 1
    while ((at = index(substr(text, start), name)) > 0) {
      at += start - 1
      before = at > 1 ? substr(text, at - 1, 1) : ""
      after = substr(text, at + length(name), 1)
      if (before !~ /[[:alnum:]_.-]/ && after !~ /[[:alnum:]_-]/) return 1
      start = at + 1
    }
    return 0
  }

  function file_name(path) {
    sub(/.*\//, "", path)
    return path
  }

  function names(added, referrer, target,    text, name) {
    if (!(referrer in added) || !is_code(referrer) || is_test(referrer) || !is_code(target)) return 0
    text = added[referrer]
    name = file_name(target)
    return mentions(text, target) || (name != target && file_name_count[name] == 1 && mentions(text, name))
  }

  # Added lines only, from the hunks: a header line is never content, whatever it begins with.
  function read_added(line, side) {
    if (line ~ /^diff --git /) { in_header[side] = 1; current[side] = ""; return }
    if (in_header[side]) {
      if (line ~ /^\+\+\+ b\//) current[side] = substr(line, 7)
      else if (line ~ /^@@/) in_header[side] = 0
      return
    }
    if (current[side] != "" && line ~ /^\+/) {
      if (side == "landed") landed_added[current[side]] = landed_added[current[side]] substr(line, 2) "\n"
      else changed_added[current[side]] = changed_added[current[side]] substr(line, 2) "\n"
    }
  }

  FILENAME == ARGV[1] { describes[$1 SUBSEP $2] = 1; next }
  FILENAME == ARGV[2] { landed[$0] = 1; next }
  FILENAME == ARGV[3] { changed[$0] = 1; next }
  FILENAME == ARGV[4] { file_name_count[file_name($0)]++; next }
  FILENAME == ARGV[5] { read_added($0, "landed"); next }
  FILENAME == ARGV[6] { read_added($0, "changed"); next }

  END {
    for (l in landed) {
      for (c in changed) {
        reason = ""
        if (l == c) {
          if (is_prose(l)) reason = "same prose file"
        } else if ((l SUBSEP c) in describes) {
          reason = "landed describes changed"
        } else if ((c SUBSEP l) in describes) {
          reason = "changed describes landed"
        } else if (governs(l, c)) {
          reason = "landed governs changed"
        } else if (governs(c, l)) {
          reason = "changed governs landed"
        } else if (names(landed_added, l, c)) {
          reason = "landed names changed"
        } else if (names(changed_added, c, l)) {
          reason = "changed names landed"
        }
        if (reason != "") print l "\t" c "\t" reason
      }
    }
  }
' "$work_directory/describes" "$work_directory/landed" "$work_directory/changed" "$work_directory/tree" \
  "$landed_diff" "$changed_diff" \
  | LC_ALL=C sort \
  | jq -R 'split("\t") | {landed: .[0], changed: .[1], reason: .[2]}' \
  | jq -s . \
  > "$output_file"
