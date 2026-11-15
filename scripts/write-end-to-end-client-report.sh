#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/fathomise/MailFathom

set -euo pipefail

### Writes what one end-to-end client run found, as report.json for a session and report.md for a person.
#
# scripts/run-end-to-end-client.sh calls it when its run ends, whatever the run concluded and wherever it stopped, so a
# report exists for a run that never reached the browser as much as for one that did. It reads two files the run wrote
# and judges nothing they do not say:
#
#   - Playwright's own JSON report, for every spec's outcome and duration and, for a failed one, its error and where its
#     trace and screenshot sit inside the browser artifact. A run that stopped before Playwright has none, and the
#     report says the specs did not run rather than that none failed.
#   - The service log, written as one JSON object a line, for every entry at Error or Critical. An entry is expected
#     when it was logged inside the span of a spec that declares it breaks the deployment on purpose — a test carrying
#     an `expected-service-errors` annotation — and unexpected everywhere else. The few lines the host writes before
#     its configuration exists keep the plain console format and carry no timestamp, so an error among them is never
#     inside a window.
#
# It never fails over what it read: the caller decides what an unexpected error costs the run, from the JSON.
#
# Usage:
#   scripts/write-end-to-end-client-report.sh --output <directory> --commit <sha> --started <epoch seconds> \
#     --finished <epoch seconds> --exit-code <n> --step <the step the run was in> \
#     --playwright-results <file> --service-log <file>

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

output_directory=''
commit=''
started=''
finished=''
exit_code=''
step=''
playwright_results=''
service_log=''

while (($# > 0)); do
  case "$1" in
    --output) output_directory="$2" ;;
    --commit) commit="$2" ;;
    --started) started="$2" ;;
    --finished) finished="$2" ;;
    --exit-code) exit_code="$2" ;;
    --step) step="$2" ;;
    --playwright-results) playwright_results="$2" ;;
    --service-log) service_log="$2" ;;
    *)
      printf 'write-end-to-end-client-report.sh does not take %s.\n' "$1" >&2
      exit 2
      ;;
  esac
  shift 2
done

for required in output_directory commit started finished exit_code step playwright_results service_log; do
  if [[ -z "${!required}" ]]; then
    printf 'write-end-to-end-client-report.sh needs --%s.\n' "${required//_/-}" >&2
    exit 2
  fi
done

if [[ ! "$started" =~ ^[0-9]+$ || ! "$finished" =~ ^[0-9]+$ || ! "$exit_code" =~ ^[0-9]+$ ]]; then
  printf 'write-end-to-end-client-report.sh takes --started and --finished in epoch seconds and a numeric --exit-code.\n' >&2
  exit 2
fi

mkdir --parents "$output_directory"

playwright_json='null'

if [[ -s "$playwright_results" ]] && jq --exit-status 'type == "object"' "$playwright_results" > /dev/null 2>&1; then
  playwright_json="$playwright_results"
fi

service_log_text=''

if [[ -f "$service_log" ]]; then
  service_log_text="$service_log"
fi

# Both inputs are read as files when they exist and as an empty value when they do not, so the one filter below covers
# a run that stopped before the host started and one that stopped before Playwright did.
jq --null-input \
  --arg repositoryRoot "$repository_root" \
  --arg commit "$commit" \
  --argjson started "$started" \
  --argjson finished "$finished" \
  --argjson exitCode "$exit_code" \
  --arg step "$step" \
  --slurpfile playwright <(if [[ "$playwright_json" != 'null' ]]; then cat "$playwright_json"; else printf 'null'; fi) \
  --rawfile serviceLog <(if [[ -n "$service_log_text" ]]; then cat "$service_log_text"; fi) \
  '
  def plain: gsub("\u001b\\[[0-9;]*m"; "");

  def relative_to($root): if type == "string" and startswith($root + "/") then .[($root | length) + 1:] else . end;

  # Playwright states instants to the millisecond and the service log is configured to; jq parses whole seconds alone.
  def epoch_milliseconds:
    capture("^(?<seconds>[0-9T:-]+)(\\.(?<fraction>[0-9]+))?Z$")
    | ((.seconds + "Z") | fromdateiso8601) * 1000 + (((.fraction // "") + "000")[0:3] | tonumber);

  def specs($root; $outputDirectory; $prefix):
    (.specs[]? as $spec
      | $spec.tests[] as $test
      | ($test.results | last) as $result
      | {
          title: (($prefix + [$spec.title]) | join(" › ")),
          file: ("\($root)/\($spec.file)" | relative_to($repositoryRoot)),
          line: $spec.line,
          outcome: ($result.status // "not run"),
          durationMilliseconds: ($result.duration // 0),
          annotations: $test.annotations,
          spans: [$test.results[] | (.startTime | epoch_milliseconds) as $from | {from: $from, to: ($from + .duration)}]
        }
      + if ($result.status // "") as $status | ["passed", "skipped"] | index($status) then {}
        else {
          errors: [($result.errors // [])[] | {
            message: (.message // "" | plain),
            file: (.location.file // null | relative_to($repositoryRoot)),
            line: (.location.line // null)
          }],
          trace: ([($result.attachments // [])[] | select(.name == "trace") | .path | relative_to($outputDirectory)] | first),
          screenshot: ([($result.attachments // [])[] | select(.name == "screenshot") | .path | relative_to($outputDirectory)] | first)
        }
        end),
    (.suites[]? as $suite | $suite | specs($root; $outputDirectory; $prefix + [$suite.title]));

  ($playwright[0]) as $report
  | ($report.config.rootDir // "") as $root
  | ($report.config.projects[0].outputDir // "") as $outputDirectory
  | (if $report == null then [] else [$report.suites[] | specs($root; $outputDirectory; [])] end) as $specs
  | [$specs[] | select(any(.annotations[]?; .type == "expected-service-errors")) | .spans[]] as $windows
  | ($serviceLog | split("\n")) as $lines
  | [range(0; $lines | length) as $index
      | $lines[$index] as $line
      | ($line | fromjson? // null) as $entry
      | if ($entry | type) == "object" then
          select($entry.LogLevel == "Error" or $entry.LogLevel == "Critical")
          | {
              timestamp: ($entry.Timestamp // null),
              level: $entry.LogLevel,
              category: ($entry.Category // null),
              eventId: ($entry.EventId // null),
              message: ($entry.Message // null),
              exception: (($entry.Exception // null) | if . == null then null else split("\n")[0] end)
            }
        else
          ($line | capture("^(?<level>fail|crit): (?<category>.*)$")? // empty)
          | {
              timestamp: null,
              level: (if .level == "crit" then "Critical" else "Error" end),
              category: .category,
              eventId: null,
              message: (($lines[$index + 1] // "") | ltrimstr(" ") | sub("^\\s+"; "")),
              exception: null
            }
        end
    ] as $errors
  | [$errors[]
      | . + {
          expected: (.timestamp != null and ((.timestamp | epoch_milliseconds? // null) as $at
            | $at != null and any($windows[]; .from <= $at and $at <= .to)))
        }
    ] as $judged
  | {
      commit: $commit,
      startedAt: ($started | todate),
      durationSeconds: ($finished - $started),
      outcome: (if $exitCode == 0 then "passed" else "failed" end),
      exitCode: $exitCode,
      failedStep: (if $exitCode == 0 then null else $step end),
      specsRan: ($report != null),
      specs: [$specs[] | del(.annotations, .spans)],
      suiteErrors: [($report.errors // [])[] | {
        message: (.message // "" | plain),
        file: (.location.file // null | relative_to($repositoryRoot)),
        line: (.location.line // null)
      }],
      expectedServiceErrorCount: ([$judged[] | select(.expected)] | length),
      unexpectedServiceErrors: [$judged[] | select(.expected | not) | del(.expected)],
      artifacts: {
        browser: "end-to-end-client-browser — every trace and screenshot path below is relative to it",
        logs: "end-to-end-client-logs — service.log, orchestration.log, and Playwright'"'"'s own playwright.json"
      }
    }
  ' > "$output_directory/report.json"

# The Markdown is rendered from the JSON alone, so the two can never say different things.
jq --raw-output '
  def cell: tostring | gsub("\\|"; "\\|") | gsub("\n"; " ");

  "# End-to-end client run: \(.outcome)",
  "",
  "- Commit: `\(.commit)`",
  "- Started: \(.startedAt), ran for \(.durationSeconds / 60 | floor) min \(.durationSeconds % 60) s",
  (if .failedStep then "- Stopped while: **\(.failedStep)** (exit \(.exitCode))" else empty end),
  "- Specs: " + (if .specsRan
      then "\([.specs[] | select(.outcome == "passed")] | length) passed, \([.specs[] | select(.outcome != "passed" and .outcome != "skipped")] | length) failed, \([.specs[] | select(.outcome == "skipped")] | length) skipped"
      else "did not run" end),
  "- Service errors: \(.unexpectedServiceErrors | length) unexpected, \(.expectedServiceErrorCount) inside a window a spec declared",
  "",
  (if (.suiteErrors | length) > 0 then
    "## Errors outside any spec", "",
    (.suiteErrors[] | "- \(.file // "?"):\(.line // "?") — \(.message | split("\n")[0])"), ""
  else empty end),
  (if ([.specs[] | select(.errors)] | length) > 0 then
    "## Failed specs", "",
    (.specs[] | select(.errors) |
      "### \(.title)", "",
      "- \(.file):\(.line), \(.outcome) after \(.durationMilliseconds / 1000 | floor) s",
      (if .trace then "- Trace: `\(.trace)`" else empty end),
      (if .screenshot then "- Screenshot: `\(.screenshot)`" else empty end),
      "",
      (.errors[] | "```text", (.message | split("\n")[0:20] | join("\n")), "```", "")
    )
  else empty end),
  (if (.unexpectedServiceErrors | length) > 0 then
    "## Unexpected service errors", "",
    "| When | Level | Category | Message |",
    "| --- | --- | --- | --- |",
    (.unexpectedServiceErrors[] | "| \(.timestamp // "before configuration" | cell) | \(.level | cell) | \(.category // "" | cell) | \((.message // "") + (if .exception then " — " + .exception else "" end) | cell) |"),
    ""
  else empty end),
  (if .specsRan then
    "## Every spec", "",
    "| Spec | Outcome | Seconds |",
    "| --- | --- | --- |",
    (.specs[] | "| \(.file | split("/") | last | cell) › \(.title | cell) | \(.outcome) | \(.durationMilliseconds / 1000 | floor) |"),
    ""
  else empty end),
  "Traces and screenshots are in the `end-to-end-client-browser` artifact, the service and orchestration logs in `end-to-end-client-logs`, and this report as JSON in `end-to-end-client-report`."
' "$output_directory/report.json" > "$output_directory/report.md"
