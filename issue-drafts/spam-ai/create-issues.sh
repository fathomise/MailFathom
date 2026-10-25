#!/usr/bin/env bash
# Opens the AI spam-classification parent and its seven children on fathomise/MailFathom, plus one
# parked issue outside the parent, links the hierarchy and the dependency relations, and places every
# issue on project 1.
# Needs an authenticated `gh` with write access to the repository and to the board.
# DRY_RUN=1 prints what would be sent, with invented numbers, and calls nothing.
set -euo pipefail

repo=fathomise/MailFathom
dir=$(cd "$(dirname "$0")" && pwd)
dry=${DRY_RUN:-0}

# key | body file | title | labels | Area | Queue | Size | Status (- leaves it to the board automation)
specs=(
  "PARENT|00-parent.md|[P] Replace the SpamAssassin scanner with a model stage that names what kind of spam a message is|type:feature,parent,backend,frontend|Automation|Later|XL|-"
  "REMOVE|01-remove-spamassassin.md|Remove the SpamAssassin scanner stage and every asset that deploys it|type:feature,backend|Automation|Later|L|-"
  "DECISION|02-decision.md|Decide how the model stage reaches a spam verdict: the Decisions API, the provider, and what leaves the deployment|type:decision,backend|Agents & answering|Later|S|-"
  "RECORD|03-spam-subcategory-record.md|Record what kind of spam a message is, and the model stage that named it|type:feature,backend|Automation|Later|L|-"
  "ABSTRACTION|04-decision-client.md|Add a decision-client abstraction shaped for Microsoft.Extensions.AI, with an OpenRouter adapter|type:feature,backend|Agents & answering|Needs decision|L|-"
  "MODEL|05-model-stage.md|Ask a model whether a message is spam and what kind, after the deterministic stage|type:feature,backend,security|Automation|Needs decision|L|-"
  "SURFACES|06-read-surfaces.md|Publish a message's spam verdict and subcategory in the client API and the MCP read results|type:feature,backend|Automation|Later|S|-"
  "CLIENT|07-client-display.md|Show what kind of spam a message is on the opened message|type:feature,frontend|Client|Later|L|-"
  "STANDARD|08-standard-decisions-rewrite.md|Move the decision client onto the standard OpenAI Decisions contract once OpenAI publishes it and OpenRouter implements it|type:feature,backend,blocked|Agents & answering|Parked|S|Blocked"
)

# blocked | blocker
dependencies=(
  "RECORD|REMOVE"
  "ABSTRACTION|DECISION"
  "MODEL|RECORD"
  "MODEL|ABSTRACTION"
  "MODEL|DECISION"
  "SURFACES|RECORD"
  "CLIENT|SURFACES"
  "STANDARD|ABSTRACTION"
)

declare -A number

render() {
  local body
  body=$(cat "$dir/$1")
  for key in "${!number[@]}"; do
    body=${body//"{{$key}}"/${number[$key]}}
  done
  printf '%s\n' "$body"
}

run() {
  if [[ $dry == 1 ]]; then
    { printf 'would run:'; printf ' %q' "$@"; printf '\n'; } >&2
  else
    "$@"
  fi
}

# 1. Open every issue. A body can only name siblings opened before it, so step 2 rewrites them all.
fake=9000
for spec in "${specs[@]}"; do
  IFS='|' read -r key file title labels _ _ _ _ <<<"$spec"
  if [[ $dry == 1 ]]; then
    number[$key]=$((fake++))
  else
    url=$(gh issue create --repo "$repo" --title "$title" --label "$labels" --body "$(render "$file")")
    number[$key]=${url##*/}
  fi
  echo "opened $key as #${number[$key]}"
done

# 2. Rewrite each body now that every number is known.
for spec in "${specs[@]}"; do
  IFS='|' read -r key file _ _ _ _ _ _ <<<"$spec"
  rendered=$(render "$file")
  if grep -q '{{' <<<"$rendered"; then
    echo "unresolved placeholder in $file" >&2; exit 1
  fi
  if [[ $dry == 1 ]]; then
    printf '\n===== #%s %s =====\n%s\n' "${number[$key]}" "$key" "$rendered"
  else
    gh api "repos/$repo/issues/${number[$key]}" -X PATCH -f body="$rendered" >/dev/null
  fi
done

id_of() { [[ $dry == 1 ]] && echo "id-of-$1" || gh api "repos/$repo/issues/$1" --jq .id; }

# 3. Link the seven children under the parent, in delivery order. STANDARD stays outside it.
for key in REMOVE DECISION RECORD ABSTRACTION MODEL SURFACES CLIENT; do
  run gh api "repos/$repo/issues/${number[PARENT]}/sub_issues" -F sub_issue_id="$(id_of "${number[$key]}")" >/dev/null
done

# 4. Record the order between the issues.
for pair in "${dependencies[@]}"; do
  IFS='|' read -r blocked blocker <<<"$pair"
  run gh api --method POST "repos/$repo/issues/${number[$blocked]}/dependencies/blocked_by" \
    -F issue_id="$(id_of "${number[$blocker]}")" >/dev/null
done

# 5. Place each issue on the board: Area, Queue, Size, and Status where one is stated.
if [[ $dry == 1 ]]; then
  echo "would set Area, Queue, and Size on project 1 for every issue above, and Status: Blocked on STANDARD"
  exit 0
fi

project_id=$(gh project view 1 --owner fathomise --format json --jq .id)
fields=$(gh project field-list 1 --owner fathomise --format json)
field_id() { jq -r --arg f "$1" '.fields[] | select(.name == $f) | .id' <<<"$fields"; }
option_id() { jq -r --arg f "$1" --arg o "$2" '.fields[] | select(.name == $f) | .options[] | select(.name == $o) | .id' <<<"$fields"; }

for spec in "${specs[@]}"; do
  IFS='|' read -r key _ _ _ area queue size status <<<"$spec"
  item=$(gh project item-add 1 --owner fathomise --url "https://github.com/$repo/issues/${number[$key]}" --format json --jq .id)
  pairs=("Area|$area" "Queue|$queue" "Size|$size")
  [[ $status == - ]] || pairs+=("Status|$status")
  for pair in "${pairs[@]}"; do
    IFS='|' read -r field value <<<"$pair"
    option=$(option_id "$field" "$value")
    [[ -n $option ]] || { echo "no option '$value' in field '$field'" >&2; exit 1; }
    gh project item-edit --project-id "$project_id" --id "$item" \
      --field-id "$(field_id "$field")" --single-select-option-id "$option" >/dev/null
  done
  echo "placed #${number[$key]}: Area=$area Queue=$queue Size=$size Status=$status"
done
