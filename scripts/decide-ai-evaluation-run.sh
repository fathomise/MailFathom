#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/fathomise/MailFathom

set -euo pipefail

# Decides how an agent evaluation run ends, for scripts/run-ai-evaluations.sh:
#
#   decide-ai-evaluation-run.sh <test exit code> <repetitions passed> <repetitions> <floor percentage> <TRX file>...
#
# and exits with the code the run ends on, saying why.
#
# A model's answer varies between calls, so cases falling short is the measurement rather than a defect, and only a
# repetition share under the floor fails the run. Exit code 2 is the test platform's "at least one test failed"; every
# other code is carried as it is. A test can also fail without falling short — an exception the scenario did not turn
# into a shortfall, a crash — and such a test filed nothing the share could count, so it keeps the run red.
#
# A shortfall failure is told from any other by its own message, which opens with the `<n> shortfall(s):` header
# AiEvaluationRun.AssertNoShortfalls writes; scripts/test-agent-workflow.sh holds the two to each other. Each failed
# result is read on its own, so one failure can never be counted against another, and nothing reads the stack trace,
# whose frames the runtime may inline away.

if (( $# < 4 )); then
  printf 'Usage: %s <test exit code> <repetitions passed> <repetitions> <floor percentage> <TRX file>...\n' "$0" >&2
  exit 64
fi

test_exit_code="$1"
repetitions_passed="$2"
repetition_count="$3"
required_repetition_percentage="$4"
shift 4

if [[ "$test_exit_code" -ne 2 || $# -eq 0 ]]; then
  exit "$test_exit_code"
fi

# A TRX file is XML with several results to a line and a message running over several, so each result is read as one
# record. The first record of each file is what precedes its first result. A file awk cannot read ends this script
# rather than reading as no failures.
counts=$(
  awk '
    BEGIN { RS = "<UnitTestResult " }
    FNR > 1 && /^[^>]*outcome="Failed"/ {
      failed++
      if ($0 !~ /<Message>[0-9]+ shortfall\(s\):/) other++
    }
    END { print failed + 0, other + 0 }' "$@")
read -r failed_tests other_failures <<< "$counts"

if (( other_failures > 0 )); then
  printf '%d of %d failed test(s) ended other than on their shortfalls, which the repetition share cannot count, so the run fails.\n' \
    "$other_failures" "$failed_tests"
  exit 2
fi

if (( repetition_count > 0 && repetitions_passed * 100 >= required_repetition_percentage * repetition_count )); then
  printf 'Every one of the %d failed test(s) fell short, and %d of %d repetitions passed, at or above %d%%, so the run passes.\n' \
    "$failed_tests" "$repetitions_passed" "$repetition_count" "$required_repetition_percentage"
  exit 0
fi

printf '%d of %d repetitions passed, under %d%%, so the run fails.\n' \
  "$repetitions_passed" "$repetition_count" "$required_repetition_percentage"
exit 2
