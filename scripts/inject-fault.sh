#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/fathomise/MailFathom

set -euo pipefail

### Breaks one of a Compose deployment's connections on purpose, through the proxy deploy/compose/compose.faults.yaml
### starts, and mends it again.
#
# Usage:
#   scripts/inject-fault.sh list
#   scripts/inject-fault.sh start <preset> [--for <duration>]
#   scripts/inject-fault.sh stop [preset]
#
# `start` holds the fault for the duration — seconds, or a number followed by s, m, or h, 60s when none is given — and
# removes it when the duration ends or when the script is interrupted, so a fault never outlives the run that started
# it unless that run is killed outright. `stop` is for that case: it removes one preset's fault, or with no preset every
# fault the proxy holds. `list` names the presets and what is active now.
#
# The proxy's control API is reached on 127.0.0.1 at MAILFATHOM_FAULTS_CONTROL_PORT, 8474 unless the overlay was
# started with another. docs/operations/fault-injection.md is the page.

readonly default_duration='60s'
readonly control_address="http://127.0.0.1:${MAILFATHOM_FAULTS_CONTROL_PORT:-8474}"

# One line per preset: its name, the proxy it acts on, the toxic it adds or `disable` for a refused connection, the
# toxic's attributes, and what a reader sees. The toxic is named after the preset, which is what lets `stop` remove
# exactly the fault it started and nothing another run added beside it.
readonly presets='imap-down|imap|disable||the IMAP connection is refused, and every open one is closed
imap-stalled|imap|timeout|{"timeout": 0}|the IMAP server stops answering and the connection stays open
imap-slow|imap|latency|{"latency": 3000, "jitter": 1000}|every answer from the IMAP server arrives two to four seconds late
imap-cut-midway|imap|limit_data|{"bytes": 65536}|an IMAP connection is closed once the server has sent 64 KiB on it
smtp-down|smtp|disable||the SMTP connection is refused, and every open one is closed
server-down|client|disable||the client cannot reach the service, and every open connection is closed
server-slow|client|latency|{"latency": 3000, "jitter": 1000}|every answer from the service reaches the client two to four seconds late'

usage() {
  sed -n '/^# Usage:/,/^# started with another/s/^# \{0,1\}//p' "$0"
}

fail() {
  printf '%s\n' "$1" >&2
  exit 1
}

control() {
  local method="$1" path="$2" body="${3-}"
  local arguments=(--silent --show-error --fail-with-body --max-time 10 --request "$method")

  if [[ -n "$body" ]]; then
    arguments+=(--header 'Content-Type: application/json' --data "$body")
  fi

  curl "${arguments[@]}" "$control_address$path" \
    || fail "The proxy's control API at $control_address refused $method $path. Is the overlay up?"
}

preset_line() {
  local line
  line="$(printf '%s\n' "$presets" | awk -F'|' -v name="$1" '$1 == name')"
  [[ -n "$line" ]] || fail "No preset is called '$1'. scripts/inject-fault.sh list names them."
  printf '%s' "$line"
}

list_presets() {
  local state

  printf 'Presets:\n'
  printf '%s\n' "$presets" | awk -F'|' '{ printf "  %-16s %s\n", $1, $5 }'

  state="$(control GET /proxies)"
  printf '\nActive now:\n'
  jq -r '
    [ .[] | (select(.enabled | not) | "  \(.name): connection refused"),
            (.name as $proxy | .toxics[] | "  \($proxy): \(.name) (\(.type))") ]
    | if length == 0 then "  nothing" else .[] end
  ' <<< "$state"
}

start_fault() {
  local line name proxy toxic attributes description

  line="$(preset_line "$1")"
  IFS='|' read -r name proxy toxic attributes description <<< "$line"

  if [[ "$toxic" == 'disable' ]]; then
    [[ "$(control GET "/proxies/$proxy" | jq -r .enabled)" == 'true' ]] \
      || fail "The $proxy connection is already refused; another run holds that fault."
    control POST "/proxies/$proxy" '{"enabled": false}' > /dev/null
  else
    control POST "/proxies/$proxy/toxics" \
      "{\"name\": \"$name\", \"type\": \"$toxic\", \"stream\": \"downstream\", \"attributes\": $attributes}" > /dev/null
  fi

  printf 'Started %s: %s.\n' "$name" "$description"
}

stop_fault() {
  local line name proxy toxic

  if [[ -z "${1-}" ]]; then
    control POST /reset > /dev/null
    printf 'Removed every fault; all three connections pass through again.\n'
    return
  fi

  line="$(preset_line "$1")"
  IFS='|' read -r name proxy toxic _ <<< "$line"

  if [[ "$toxic" == 'disable' ]]; then
    control POST "/proxies/$proxy" '{"enabled": true}' > /dev/null
  else
    control DELETE "/proxies/$proxy/toxics/$name" > /dev/null
  fi

  printf 'Removed %s.\n' "$name"
}

# The preset and the sleep are globals rather than locals, because the trap that ends them runs after this function
# has returned.
hold_fault() {
  held_preset="$1"
  local duration="$2"

  [[ "$duration" =~ ^[1-9][0-9]*[smh]?$ ]] \
    || fail "A duration is a number of seconds, or a number followed by s, m, or h; '$duration' is neither."

  start_fault "$held_preset"

  # Armed only once the fault exists, so a start that failed removes nothing another run started.
  trap 'kill "${sleeper-}" 2> /dev/null || true; stop_fault "$held_preset"' EXIT
  trap 'exit 130' INT TERM

  printf 'Holding it for %s; interrupt to end it sooner.\n' "$duration"
  sleep "$duration" &
  sleeper=$!
  wait "$sleeper"
}

case "${1-}" in
  list)
    (($# == 1)) || fail "list takes no argument."
    list_presets
    ;;
  start)
    [[ -n "${2-}" ]] || fail "start needs a preset. scripts/inject-fault.sh list names them."
    if (($# == 2)); then
      hold_fault "$2" "$default_duration"
    elif (($# == 4)) && [[ "$3" == '--for' ]]; then
      hold_fault "$2" "$4"
    else
      fail "start takes a preset and, optionally, --for <duration>."
    fi
    ;;
  stop)
    (($# <= 2)) || fail "stop takes at most one preset."
    stop_fault "${2-}"
    ;;
  -h | --help)
    usage
    ;;
  *)
    usage >&2
    exit 1
    ;;
esac
