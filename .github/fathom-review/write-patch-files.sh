#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/fathomise/MailFathom

# Write each changed file's patch to a file of its own, one diff line per line.
#
# `files.json` carries every patch as one JSON string, so however long a diff is it sits on one
# line, and the session's `Read` tool cuts any line past 2,000 characters. A reader handed the patch
# that way saw the first hunks of a large file and judged the change from them without saying so —
# measured on #2334, whose longest patch was 6,345 characters. Written out here, the same diff is
# as many lines as it has, and `Read` pages through it like any other file.
#
# The path under the output directory is the changed file's own path with `.diff` appended, so the
# patch of `a/b.cs` is `<output>/a/b.cs.diff` and no index is needed to find it. That makes the path
# a value from the API deciding where this script writes, so an absolute one or one with a `..`
# segment is refused before anything is written: GitHub never reports either for a file in a
# repository, and one that did would be writing outside the directory it was given. A file with no
# patch — binary, or a diff too large for GitHub to return — gets no file, which the prompts say.
#
# Usage: write-patch-files.sh <files.json> <output-directory>

set -euo pipefail

files_json="${1:?the collected files.json is required}"
output_directory="${2:?the output directory is required}"

mapfile -d '' paths < <(jq -j '.[] | select(.patch != null) | .filename + "\u0000"' "$files_json")

for path in "${paths[@]}"; do
  if [[ -z "$path" || "$path" == /* || "/$path/" == */../* ]]; then
    printf 'Refusing to write the patch of %q: the path is empty, absolute, or has a .. segment.\n' "$path" >&2
    exit 1
  fi
done

for path in "${paths[@]}"; do
  destination="$output_directory/$path.diff"
  mkdir -p "$(dirname "$destination")"
  jq -r --arg path "$path" '.[] | select(.filename == $path) | .patch' "$files_json" > "$destination"
done
