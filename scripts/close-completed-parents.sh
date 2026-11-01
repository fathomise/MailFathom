#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/fathomise/MailFathom

set -euo pipefail

### Closes every open issue whose sub-issues are all closed, as completed, with one comment saying why.
#
# Usage:
#   scripts/close-completed-parents.sh <owner/repository>
#   scripts/close-completed-parents.sh <owner/repository> --dry-run    names what is complete now and closes nothing
#
# GitHub publishes sub-issue progress on the issue itself, so one listing answers it for every parent at once. The
# `parent` label is deliberately not what is filtered on: the label goes stale while the sub-issue links stay right.
# An issue with no sub-issues at all is not a completed parent, which is why the count has to be above zero.
#
# The board is not written here: closing the issue is what moves its item to `Done`. Closing comes first and the
# comment second, so a comment that fails leaves a closed parent rather than a line repeated on every run.
#
# A sub-parent closed in one pass makes the parent above it complete only in the next listing, so the listing is
# repeated until a pass closes nothing. The hierarchy is two levels deep, so three passes always suffice.

readonly settle_seconds=900
readonly max_passes=3

if [[ $# -lt 1 || $# -gt 2 || ( $# -eq 2 && "$2" != '--dry-run' ) ]]; then
  printf 'Usage: %s <owner/repository> [--dry-run]\n' "$0" >&2
  exit 2
fi

repository="$1"
dry_run=0
[[ "${2:-}" == '--dry-run' ]] && dry_run=1

# A parent counts as complete from the instant its first child is closed, so one being assembled — a closed child
# linked a moment ago, the rest still to come — reads exactly like a finished one. `updated_at` cannot tell the two
# apart, because linking a child does not touch the parent's; the timeline can. An unreadable timeline holds the parent
# open: closing it a run later costs nothing, and closing one that should have stayed open costs a person their time.
links_settled() {
  local number="$1" newest

  # `--paginate` runs the filter once per page, so it emits one line per event and the newest is chosen after. The
  # timestamps are ISO 8601 in UTC, so they sort as text.
  newest="$(
    gh api "repos/${repository}/issues/${number}/timeline?per_page=100" --paginate \
      --jq '.[] | select((.event? // "") | startswith("sub_issue_")) | .created_at' |
      sort | tail -n 1
  )" || return 1

  [[ -n "$newest" ]] || return 0
  (($(date +%s) - $(date --date "$newest" +%s) >= settle_seconds))
}

list_completed_parents() {
  gh api "repos/${repository}/issues?state=open&per_page=100" --paginate --jq '
    .[]
    | select(.pull_request == null)
    | .sub_issues_summary as $children
    | select(($children.total // 0) > 0 and $children.completed == $children.total)
    | [.number, $children.total, .title] | @tsv'
}

for ((pass = 1; pass <= max_passes; pass++)); do
  closed=0

  # Read whole before the loop, so a listing that fails ends the run as a failure rather than as a quiet nothing to close.
  parents="$(list_completed_parents)"

  while IFS=$'\t' read -r number total title; do
    [[ -n "$number" ]] || continue

    if ! links_settled "$number"; then
      printf 'Held #%s: its sub-issue links moved in the last %d minutes, or its timeline could not be read.\n' \
        "$number" "$((settle_seconds / 60))"
      continue
    fi

    if ((dry_run)); then
      printf 'Would close #%s: all %s sub-issues are closed: %s\n' "$number" "$total" "$title"
      continue
    fi

    if ! gh api --method PATCH "repos/${repository}/issues/${number}" \
      -f state=closed -f state_reason=completed > /dev/null; then
      printf 'Held #%s: GitHub would not close it; the next run tries again.\n' "$number" >&2
      continue
    fi

    gh api --method POST "repos/${repository}/issues/${number}/comments" \
      -f body='All sub-issues are closed, so this parent is closed with them.' > /dev/null ||
      printf 'Note: #%s was closed but the comment saying why did not post.\n' "$number" >&2

    printf 'Closed #%s: all %s sub-issues are closed: %s\n' "$number" "$total" "$title"
    closed=$((closed + 1))
  done <<< "$parents"

  ((closed > 0)) || break
done
