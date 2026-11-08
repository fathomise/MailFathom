#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/fathomise/MailFathom

set -euo pipefail

# The agent evaluation suite runs on request and nowhere else. It is absent from scripts/verify-fast.sh
# and scripts/verify-full.sh because a scenario calls real models and a judge, and its GitHub workflow
# never runs on a pull request.
#
# It runs the paid scenarios and nothing else: their free counterparts are backend/tests/Evaluations.UnitTests,
# a unit suite every gate already runs. So without MAILFATHOM_AI_EVALUATIONS=true there is nothing here
# to run and the script says so. With it, the run needs MAILFATHOM_CHAT_API_KEY and MAILFATHOM_JUDGE_MODELS,
# and fails naming whichever is missing. MAILFATHOM_JUDGE_MODELS names the panel's one to three jurors,
# separated by commas, and MAILFATHOM_JUDGE_ARBITER_MODEL the arbiter asked only about a rating the jurors do
# not all pass; a run leaving the arbiter unset grades with the jurors alone and records on every rating
# whether it would have been escalated. Every judge answers from the same endpoint and the same key as the
# models under test, so their models are all that is declared apart — beside MAILFATHOM_JUDGE_REASONING_EFFORT,
# one effort for every judge, which a run may leave unset to send no reasoning parameter at all.
#
# MAILFATHOM_EVALUATION declares the run as one YAML block, in block style over several lines or in flow
# style on one: {MainModel: {Model: vendor/model, ReasoningEffort: medium}, ImageDescription: [model-a, model-b]}.
# docs/operations/local-development.md, section Agent evaluations, lists every key and its default.
# MainModel is the model every agent runs on and each agent may name its own under its Chat key; a role
# takes one model or a list, and an evaluation runs once per model of the agent it measures. The block also
# takes Repetitions, from 1 to 20, ConcurrentProviderCalls, the run's limit on calls in flight from 1 to 256,
# and EmbeddingModels and EmbeddingDimension for the retrieval scenario.
# Where it names no MainModel, MAILFATHOM_CHAT_MODEL is measured, and the run fails naming both when
# neither is set; where it names no EmbeddingModels, MAILFATHOM_EMBEDDING_MODEL is. The retrieval scenario
# reaches its models with MAILFATHOM_EMBEDDING_API_KEY, which a requested run cannot proceed without, and
# MAILFATHOM_EMBEDDING_ADDRESS, which it may leave unset for the provider's own address. The spam
# classification scenario measures the decision models the block names under SpamClassification, or
# MAILFATHOM_DECISION_MODEL where it names none, and reaches them with MAILFATHOM_DECISION_API_KEY and,
# optionally, MAILFATHOM_DECISION_ADDRESS.
# The block's Sections key names the sections the run measures, as one name or a list:
# {MainModel: vendor/model, Sections: [Discovery, SpamClassification]}. A section is a directory under
# backend/tests/Evaluations holding scenarios, and each becomes the test host's namespace filter, so a
# run pays for those sections alone; without the key every section runs. A name that is no section
# fails the run before the build, listing those there are. Reading the key takes yq, which a block
# not mentioning it does not need.
# MAILFATHOM_EVALUATION_DATABASE names a PostgreSQL server the retrieval scenario over PostgreSQL seeds a
# database of its own on and drops afterwards; unset, that one scenario skips.
#
# The store is kept rather than cleared: its results are what the report compares this run against,
# and its cache is what makes an unchanged prompt free. MAILFATHOM_AI_EVALUATIONS_STORE points it
# elsewhere, which is how the workflow hands in the store the previous run left.
#
# The store keeps only what each run measured, and the report is rendered from a copy of it in which
# this run also holds, for every scenario it did not run, that scenario's newest earlier result tagged
# `Carried from <run>`. So a run of one section still opens on every section, and the tag says which
# results it measured and which it carried.

if ! repository_root="$(git rev-parse --show-toplevel 2>/dev/null)"; then
  printf 'run-ai-evaluations.sh must run inside a Git repository.\n' >&2
  exit 1
fi

cd "$repository_root"

requested="${MAILFATHOM_AI_EVALUATIONS:-}"
if [[ "${requested,,}" != 'true' ]]; then
  printf 'MAILFATHOM_AI_EVALUATIONS is not true, so no scenario would run; the free tests are Evaluations.UnitTests.\n'
  exit 0
fi

evaluation_project='backend/tests/Evaluations/Evaluations.csproj'
output_directory='artifacts/ai-evaluations'

# A section is a directory holding a scenario, and a scenario is what carries the run's skip reason.
mapfile -t available_sections < <(
  grep -rl --include='*.cs' 'AiEvaluationRun.SkipReason' backend/tests/Evaluations |
    sed -nE 's#^backend/tests/Evaluations/([^/]+)/.*#\1#p' | sort -u)

# Only a block that mentions the key is parsed, so a run naming no sections needs no yq.
requested_sections=()
if [[ "${MAILFATHOM_EVALUATION:-}" =~ [Ss][Ee][Cc][Tt][Ii][Oo][Nn][Ss] ]]; then
  if ! command -v yq > /dev/null; then
    printf 'MAILFATHOM_EVALUATION mentions Sections, and reading that key takes yq, which is not on PATH.\n' >&2
    exit 1
  fi

  if ! sections_text=$(printf '%s' "$MAILFATHOM_EVALUATION" |
    yq -r '[to_entries[] | select(.key | test("(?i)^sections$")) | .value][0] // [] | [.] | flatten | .[]'); then
    printf 'MAILFATHOM_EVALUATION is not a YAML mapping the Sections key can be read from.\n' >&2
    exit 1
  fi
  if [[ -n "$sections_text" ]]; then
    mapfile -t requested_sections <<< "$sections_text"
  fi
fi

# Each requested name is matched without regard to case, as the suite reads every key, and filtered by
# the directory's own spelling, which is the namespace's.
selected_sections=()
for requested_section in "${requested_sections[@]}"; do
  matched_section=''
  for available_section in "${available_sections[@]}"; do
    if [[ "${requested_section,,}" == "${available_section,,}" ]]; then
      matched_section="$available_section"
    fi
  done

  if [[ -z "$matched_section" ]]; then
    printf "MAILFATHOM_EVALUATION names the section '%s', which is not one. The sections are %s.\n" \
      "$requested_section" "$(printf '%s\n' "${available_sections[@]}" | paste -sd ' ' | sed 's/ /, /g')" >&2
    exit 1
  fi

  selected_sections+=("$matched_section")
done

section_filter=()
if (( ${#selected_sections[@]} > 0 )); then
  section_filter=(--filter-namespace "${selected_sections[@]/#/MailFathom.Evaluations.}")
  printf 'Measuring %s alone.\n' "$(printf '%s\n' "${selected_sections[@]}" | paste -sd ' ' | sed 's/ /, /g')"
fi

MAILFATHOM_AI_EVALUATIONS_STORE="$(realpath -m "${MAILFATHOM_AI_EVALUATIONS_STORE:-$output_directory/store}")"
export MAILFATHOM_AI_EVALUATIONS_STORE

# One name for every scenario in the run, so the report shows it as one column. A timestamp sorts in
# the order the runs happened, which is the order the report reads them in.
MAILFATHOM_AI_EVALUATIONS_EXECUTION="${MAILFATHOM_AI_EVALUATIONS_EXECUTION:-$(date -u +%Y%m%dT%H%M%SZ)}"
export MAILFATHOM_AI_EVALUATIONS_EXECUTION

# The aieval tool reports usage to Microsoft unless told not to, and nothing about this repository's
# runs is anybody else's to collect.
export DOTNET_AIEVAL_TELEMETRY_OPTOUT=1
export DOTNET_AIEVAL_SKIP_FIRST_TIME_EXPERIENCE=1

# How many results the store keeps of each scenario, whichever runs they came from, so however often
# the other sections run, a section's own history stays. The report reads the newest ten runs; the rest
# is history a comparison can still be widened to without paying for any of it again.
retained_results_per_scenario=30

# Written the moment the test host starts, holding its PID until the host is reaped and empty after, so
# whoever keeps the store can tell a run that never reached a provider — no file — from one that did,
# and wait for a host still writing the store after this shell was killed.
test_host_file="$output_directory/test-host.pid"

mkdir -p "$MAILFATHOM_AI_EVALUATIONS_STORE" "$output_directory"
rm -rf "$output_directory/results" "$output_directory/report.html" "$output_directory/report-store" "$test_host_file"

dotnet tool restore
dotnet restore "$evaluation_project" --locked-mode
dotnet build "$evaluation_project" --configuration Release --no-restore
evaluation_assembly="$(dotnet msbuild "$evaluation_project" -getProperty:TargetPath -p:Configuration=Release)"

# The test host is started directly rather than through `dotnet run`, and the project opts out of
# discovery for the reason the integration suite gives, so a solution-wide run never starts it.
#
# A cancel reaches this shell alone: a GitHub runner signals the step's process and none of its
# children, and `kill 0` would reach the runner too, which shares this shell's process group. So the
# shell traps SIGINT and SIGTERM, passes each on to the host — Microsoft Testing Platform reads SIGINT
# as a cancelled session, and SIGTERM ends the process — and waits for it. Job control is on while the
# host starts because a non-interactive shell starts a background command with SIGINT ignored, and
# .NET then never sees one.
#
# The exit code is carried so the report is still rendered for a run that failed, which is exactly
# when it is worth reading.
forwarded_signal=''
test_host=''
trap 'forwarded_signal=INT; kill -INT "$test_host" 2> /dev/null || true' INT
trap 'forwarded_signal=TERM; kill -TERM "$test_host" 2> /dev/null || true' TERM

set -m
dotnet "$evaluation_assembly" --report-xunit-trx --results-directory "$output_directory/results" "${section_filter[@]}" "$@" < /dev/null &
test_host=$!
set +m
printf '%d\n' "$test_host" > "$test_host_file"

# A signal that arrived while the host was being started had no host to reach yet.
if [[ -n "$forwarded_signal" ]]; then
  kill "-$forwarded_signal" "$test_host" 2> /dev/null || true
fi

# A trapped signal ends `wait` early, so the shell waits again for as long as the host lives.
test_exit_code=0
wait "$test_host" || test_exit_code=$?
while kill -0 "$test_host" 2> /dev/null; do
  wait "$test_host" || test_exit_code=$?
done

trap - INT TERM
: > "$test_host_file"

# A stopped run renders nothing: the runner kills this shell seconds after its signal, and what the
# host paid for is already in the store.
if [[ -n "$forwarded_signal" ]]; then
  printf 'Stopped by SIG%s once the test host had exited.\n' "$forwarded_signal" >&2
  exit $((128 + $(kill -l "$forwarded_signal")))
fi

# Results are filed as <run>/<scenario>/<iteration>.json, and a run's name is a timestamp, so a glob
# lists a scenario's results oldest first.
retain_newest_results() {
  local results="$1" retained="$2" scenario surplus
  local -a scenarios held

  mapfile -t scenarios < <(find "$results" -mindepth 2 -maxdepth 2 -type d -printf '%f\n' | sort -u)
  for scenario in "${scenarios[@]}"; do
    held=("$results"/*/"$scenario")
    surplus=$(( ${#held[@]} - retained ))
    if (( surplus > 0 )); then
      rm -rf "${held[@]:0:surplus}"
    fi
  done

  find "$results" -mindepth 1 -maxdepth 1 -type d -empty -delete
}

# The report reads runs newest first by when their directory was created, so the copy creates the
# earlier runs in the order they ran and this run last. A run that wrote nothing carries nothing, so
# its report opens on the newest run that did. A scenario the suite no longer has is carried as well,
# until its results are deleted from the store by hand; telling it apart would need the suite to
# list its scenarios, which is worth adding once a removed one is in the way.
compose_report_results() {
  local results="$1" execution="$2" composed="$3" run_directory run scenario newest file
  local -a scenarios held

  # A glob matching nothing stays literal, and copying that would end the script before it exits with the
  # run's own code, so each loop skips what is not there.
  mkdir -p "$composed"
  for run_directory in "$results"/*/; do
    run="$(basename "$run_directory")"
    if [[ -d "$run_directory" && "$run" != "$execution" ]]; then
      cp -R "$run_directory" "$composed/$run"
    fi
  done

  if [[ ! -d "$results/$execution" ]]; then
    return
  fi

  cp -R "$results/$execution" "$composed/$execution"

  mapfile -t scenarios < <(find "$results" -mindepth 2 -maxdepth 2 -type d -printf '%f\n' | sort -u)
  for scenario in "${scenarios[@]}"; do
    if [[ -d "$results/$execution/$scenario" ]]; then
      continue
    fi

    held=("$results"/*/"$scenario")
    newest="${held[-1]}"
    run="$(basename "$(dirname "$newest")")"
    mkdir -p "$composed/$execution/$scenario"
    for file in "$newest"/*.json; do
      [[ -f "$file" ]] || continue
      jq --arg execution "$execution" --arg tag "Carried from $run" \
        '.executionName = $execution | .tags = ((.tags // []) + [$tag])' \
        "$file" > "$composed/$execution/$scenario/$(basename "$file")"
    done
  done
}

# Each half is tidied only once a run has written it: the tool fails on a directory that does not
# exist yet, which is the state of a store no requested run has touched.
if [[ -d "$MAILFATHOM_AI_EVALUATIONS_STORE/cache" ]]; then
  dotnet tool run aieval clean-cache --path "$MAILFATHOM_AI_EVALUATIONS_STORE"
fi

if [[ -d "$MAILFATHOM_AI_EVALUATIONS_STORE/results" ]]; then
  retain_newest_results "$MAILFATHOM_AI_EVALUATIONS_STORE/results" "$retained_results_per_scenario"

  report_store="$output_directory/report-store"
  compose_report_results "$MAILFATHOM_AI_EVALUATIONS_STORE/results" "$MAILFATHOM_AI_EVALUATIONS_EXECUTION" "$report_store/results"
  dotnet tool run aieval report --path "$report_store" --output "$output_directory/report.html"
  rm -rf "$report_store"
else
  printf 'The store holds no results yet, so there is no report to render.\n' >&2
fi

# A case keeps its cached answers whatever its verdict, so an answer an earlier run wrote replays the
# sample that run drew rather than measuring the model again. Saying how much of this run was
# replayed is what keeps it from being compared with a run whose answers were asked afresh. An answer
# this run wrote and read back is still this run's measurement, so it is counted apart from the
# replays. The three metrics are the ones `EvaluationRepetitions` files on every repetition.
execution_results="$MAILFATHOM_AI_EVALUATIONS_STORE/results/$MAILFATHOM_AI_EVALUATIONS_EXECUTION"
if [[ -d "$execution_results" ]]; then
  read -r replayed_answers reused_answers asked_answers < <(
    find "$execution_results" -name '*.json' -exec jq -r \
      '.evaluationResult.metrics as $metrics | [$metrics["Answers replayed from an earlier run"].value // 0, $metrics["Answers reused within the run"].value // 0, $metrics["Answers asked afresh"].value // 0] | @tsv' {} + |
      awk '{ replayed += $1; reused += $2; asked += $3 } END { print replayed + 0, reused + 0, asked + 0 }')
  printf '%d answers of the models under test were replayed from an earlier run, %d were reused from answers this run asked, and %d were asked afresh.\n' \
    "$replayed_answers" "$reused_answers" "$asked_answers"

  # Every judged rating records whether the panel escalated it, so a run with the jurors alone says how
  # many ratings an arbiter would have been asked about before anybody pays for one.
  judge_outcomes=$(
    find "$execution_results" -name '*.json' -exec jq -r '.evaluationResult.metrics[].metadata["judge-escalation"] // empty' {} + |
      sort | uniq -c |
      awk '{ count = $1; $1 = ""; sub(/^ /, ""); printf "%s%d %s", separator, count, $0; separator = ", " }')
  if [[ -n "$judge_outcomes" ]]; then
    printf 'Judged ratings by escalation: %s.\n' "$judge_outcomes"
  fi
fi

# A case a provider or a judge left unanswered says so in its shortfall lines as `did not answer (<type>
# <status>)` — EvaluationRepetitions for a provider that stayed busy past every retry, the judge panel for
# a judge — so counting the failed results whose message carries it tells a provider's failures from the
# model's without opening the TRX.
if trx_files=("$output_directory"/results/*.trx) && [[ -f "${trx_files[0]}" ]]; then
  unanswered_cases=$(
    awk '
      /<UnitTestResult / { failed = ($0 ~ /outcome="Failed"/); unanswered = 0 }
      failed && /did not answer \(/ { unanswered = 1 }
      /<\/UnitTestResult>/ { if (failed && unanswered) count++; failed = 0 }
      END { print count + 0 }' "${trx_files[@]}")
  printf 'Cases that failed because a provider or judge did not answer: %d.\n' "$unanswered_cases"
fi

# A model's answer varies between calls, so a few scenarios falling short is the measurement rather
# than a defect, and only a pass rate under this floor fails the run. Every result here is a paid
# scenario's, the free tests being a unit suite of their own. Exit code 2 is the test platform's "at
# least one test failed"; every other code is carried as it is.
required_pass_percentage=85

if [[ "$test_exit_code" -eq 2 ]] && trx_files=("$output_directory"/results/*.trx) && [[ -f "${trx_files[0]}" ]]; then
  read -r passed executed < <(
    grep -ho '<UnitTestResult testName="[^"]*" outcome="[A-Za-z]*"' "${trx_files[@]}" |
      sed -E 's/^<UnitTestResult testName="[^"]*" outcome="([A-Za-z]*)"$/\1/' |
      awk '
        $1 == "NotExecuted" { next }
        { executed++; if ($1 == "Passed") passed++ }
        END { print passed + 0, executed + 0 }')

  printf '%d of %d evaluation scenarios passed; the run fails under %d%%.\n' \
    "$passed" "$executed" "$required_pass_percentage"

  if (( executed > 0 && passed * 100 >= required_pass_percentage * executed )); then
    test_exit_code=0
  fi
fi

exit "$test_exit_code"
