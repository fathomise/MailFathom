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
# `start` adds the fault and returns at once. The fault lasts for the duration — seconds, or a number followed by s, m,
# or h, 60s when none is given — and a timer left behind removes it then, whether or not the terminal it was started
# from is still open. Several faults can be active at once, each on its own duration. `stop` ends one sooner: it
# removes one preset's fault, or with no preset every fault the proxy holds. `list` names the presets, what is active
# now, and how long each active fault has left.
#
# It needs curl and jq, and reaches the proxy's control API on 127.0.0.1 at MAILFATHOM_FAULTS_CONTROL_PORT, 8474 unless
# the overlay was started with another. docs/operations/fault-injection.md is the page.

readonly default_duration='60s'
readonly control_port="${MAILFATHOM_FAULTS_CONTROL_PORT:-8474}"
readonly control_address="http://127.0.0.1:$control_port"

# One file per fault a timer will end, named after its preset and holding when it ends and which start it belongs to.
# It is what `list` reads the time left from, and what a timer checks before it removes anything: a fault stopped
# sooner, or stopped and started again, is no longer the one that timer was left for. Per user and per control port,
# so two overlays on one machine keep their timers apart.
user_id="$(id -u)"
readonly timers="${XDG_RUNTIME_DIR:-${TMPDIR:-/tmp}}/mailfathom-faults-$user_id-$control_port"

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
  sed -n '/^# Usage:/,/^# the overlay was started/s/^# \{0,1\}//p' "$0"
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

  # A refusal carries the proxy's own reason in its body, which is what the operator needs rather than a guess at it.
  local response
  if ! response="$(curl "${arguments[@]}" "$control_address$path")"; then
    [[ -n "$response" ]] || fail "No proxy answers at $control_address. Is the overlay up?"
    fail "The proxy refused $method $path: $response"
  fi

  printf '%s' "$response"
}

preset_line() {
  local line
  line="$(printf '%s\n' "$presets" | awk -F'|' -v name="$1" '$1 == name')"
  [[ -n "$line" ]] || fail "No preset is called '$1'. scripts/inject-fault.sh list names them."
  printf '%s' "$line"
}

time_left() {
  local seconds="$1"

  if ((seconds >= 3600)); then
    printf '%dh %02dm' $((seconds / 3600)) $((seconds % 3600 / 60))
  elif ((seconds >= 60)); then
    printf '%dm %02ds' $((seconds / 60)) $((seconds % 60))
  else
    printf '%ds' "$seconds"
  fi
}

list_presets() {
  local state active proxy name ends now left

  printf 'Presets:\n'
  printf '%s\n' "$presets" | awk -F'|' '{ printf "  %-16s %s\n", $1, $5 }'

  # A refused connection is a proxy switched off rather than a toxic with a name, so it is listed with no name and
  # given its preset's below.
  state="$(control GET /proxies)"
  active="$(jq -r '.[] | (select(.enabled | not) | "\(.name)\t"), (.name as $proxy | .toxics[] | "\($proxy)\t\(.name)")' \
    <<< "$state")"

  printf '\nActive now:\n'
  if [[ -z "$active" ]]; then
    printf '  nothing\n'
    return
  fi

  now="$(date +%s)"
  while IFS=$'\t' read -r proxy name; do
    if [[ -z "$name" ]]; then
      name="$(printf '%s\n' "$presets" | awk -F'|' -v proxy="$proxy" '$2 == proxy && $3 == "disable" { print $1 }')"
    fi

    # What the proxy holds is the truth; the timer only adds when. A fault with none here was started on another
    # machine or by another tool, or its timer was lost, and it stays until somebody stops it.
    ends=''
    if [[ -f "$timers/$name" ]]; then
      read -r ends _ < "$timers/$name" || true
    fi

    if [[ "$ends" =~ ^[0-9]+$ ]] && ((left = 10#$ends - now, left > 0)); then
      printf '  %-16s ends in %s\n' "${name:-$proxy}" "$(time_left "$left")"
    else
      printf '  %-16s held until stopped\n' "${name:-$proxy}"
    fi
  done <<< "$active"
}

start_fault() {
  local line name proxy toxic attributes description state

  line="$(preset_line "$1")"
  IFS='|' read -r name proxy toxic attributes description <<< "$line"

  if [[ "$toxic" == 'disable' ]]; then
    # Two assignments rather than one pipeline in a test, so an unreachable proxy fails with its own message.
    state="$(control GET "/proxies/$proxy")"
    state="$(jq -r .enabled <<< "$state")"
    [[ "$state" == 'true' ]] || fail "The $proxy connection is already refused; another run holds that fault."
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
    rm -rf "$timers"
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

  # After the fault is gone rather than before: where the proxy did not answer, the file stays, so `list` still names
  # the fault and `stop` can still end it. Nothing tries again on its own.
  rm -f "$timers/$name"
  printf 'Removed %s.\n' "$name"
}

hold_fault() {
  local preset="$1" duration="$2" seconds claim

  [[ "$duration" =~ ^([1-9][0-9]*)([smh]?)$ ]] \
    || fail "A duration is a number of seconds, or a number followed by s, m, or h; '$duration' is neither."

  case "${BASH_REMATCH[2]}" in
    m) seconds=$((BASH_REMATCH[1] * 60)) ;;
    h) seconds=$((BASH_REMATCH[1] * 3600)) ;;
    *) seconds="${BASH_REMATCH[1]}" ;;
  esac

  # Before the fault exists, so a directory that cannot be written leaves no fault without a timer.
  mkdir -p "$timers"
  chmod 700 "$timers"
  start_fault "$preset"

  claim="$(($(date +%s) + seconds)) $$"
  printf '%s\n' "$claim" > "$timers/$preset"

  # The timer ignores the two signals a terminal sends, and holds none of this script's streams, so it outlives both
  # the script and the terminal. Nothing ever kills it: `stop` takes its file away, and a timer that wakes to a file
  # that is gone or belongs to a later start removes nothing.
  (
    trap '' HUP INT
    sleep "$seconds"
    [[ "$(cat "$timers/$preset" 2> /dev/null)" == "$claim" ]] || exit 0
    stop_fault "$preset"
  ) < /dev/null > /dev/null 2>&1 &

  printf 'It ends in %s; scripts/inject-fault.sh stop %s ends it sooner.\n' "$duration" "$preset"
}

if [[ "${1-}" == '-h' || "${1-}" == '--help' ]]; then
  usage
  exit 0
fi

for tool in curl jq; do
  command -v "$tool" > /dev/null || fail "scripts/inject-fault.sh needs $tool on the PATH."
done

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
  *)
    usage >&2
    exit 1
    ;;
esac
