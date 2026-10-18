#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/fathomise/MailFathom

# The paths only a maintainer of the repository may change. `Fathom review` reads this file from the
# base commit to withhold the approval that would otherwise merge a contributor's change to one, so a
# pull request cannot shorten the list it is judged against. `Protected paths`, which refuses such a
# change outright, carries the same set inline until its own run can read this file from a base
# commit that has it, so until then an entry changes here and in that workflow's arrays together.
#
# Reads one changed path per line on standard input and prints each protected one, sorted and
# deduplicated. `--describe` prints the whole set on one line instead, for a message naming it.
# The header of `.github/workflows/protected-paths.yml` records why each entry is here.

set -euo pipefail

protected_prefixes=('.github/' '.agents/' '.claude/' '.config/' 'docs/decisions/')
protected_names=(
  '.editorconfig'
  '.gitattributes'
  '.worktreeinclude'
  'AGENTS.md'
  'CLAUDE.md'
  'Directory.Build.props'
)
protected_files=(
  'CHANGELOG.md'
  'CLA.md'
  'LICENSE'
  'NOTICE'
  'NuGet.config'
  'Version.props'
  'global.json'
)

if [[ "${1:-}" == '--describe' ]]; then
  echo "${protected_prefixes[*]} ${protected_names[*]} ${protected_files[*]}"
  exit 0
fi

# Literal matching rather than a regular expression, so the leading dot of each entry is a dot and a
# sibling whose name merely ends that way is not protected by accident. A prefix ends in `/` and
# covers that directory and everything beneath it, so a sibling whose name merely begins the same
# way — `docs/decisions-notes.md` — is not caught. A protected name matches at the repository root or
# after a `/`, so `docs/.editorconfig` is caught and `docs/my.editorconfig` is not. A protected file
# is matched whole, so a differently placed file of the same name is not caught and a longer name
# beginning the same way is not either.
while IFS= read -r changed_path; do
  [[ -n "$changed_path" ]] || continue
  for protected_prefix in "${protected_prefixes[@]}"; do
    if [[ "$changed_path" == "${protected_prefix}"* ]]; then
      printf '%s\n' "$changed_path"
      continue 2
    fi
  done
  for protected_name in "${protected_names[@]}"; do
    if [[ "$changed_path" == "$protected_name" || "$changed_path" == *"/${protected_name}" ]]; then
      printf '%s\n' "$changed_path"
      continue 2
    fi
  done
  for protected_file in "${protected_files[@]}"; do
    if [[ "$changed_path" == "$protected_file" ]]; then
      printf '%s\n' "$changed_path"
      continue 2
    fi
  done
done | LC_ALL=C sort --unique
