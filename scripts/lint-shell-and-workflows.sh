#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/fathomise/MailFathom

set -euo pipefail

### Lints every tracked shell script with ShellCheck, the workflows and the shell inside their `run:` blocks with
### actionlint, and the workflows and composite actions with zizmor, each at the version pinned below.
#
# Usage:
#   scripts/lint-shell-and-workflows.sh
#
# `Repository contracts` runs this on every pull request, and it is the same command locally, so a finding reaches a
# contributor from here before it reaches them as a red check. It runs all three tools and reports every one that
# failed rather than stopping at the first.
#
# The tools are downloaded from their own GitHub releases, checked against the SHA-256 recorded here, and kept under
# `${XDG_CACHE_HOME:-~/.cache}/mailfathom-lint/` — `MAILFATHOM_LINT_TOOLS` names another directory — so the first run
# needs the network and every later one does not. Nothing is taken from `PATH`: a runner image and a developer machine
# each carry their own ShellCheck, and the two disagreeing is how a script passes here and fails there. An archive whose
# checksum does not match is refused and nothing from it is installed.
#
# ShellCheck reports at `warning` and above, in scripts and in `run:` blocks alike. Below that its notes are mostly
# SC2016, which flags the single-quoted `printf` and `jq` arguments this repository writes on purpose, and SC2030 and
# SC2031, which flag the subshell each contract in `scripts/test-agent-workflow.sh` runs in by design. zizmor runs
# offline, so a verdict depends on the tree alone and never on an advisory published since; what each tool is told to
# accept beyond that is in `.github/actionlint.yaml` and `.github/zizmor.yml`, each with its reason.
#
# Release binaries exist for every platform this repository is developed on, but only Linux x86_64 is pinned, because
# that is what the runner and the development container are. Another platform is refused by name.

readonly shellcheck_version='0.11.0'
readonly shellcheck_sha256='b7af85e41cc99489dcc21d66c6d5f3685138f06d34651e6d34b42ec6d54fe6f6'
readonly actionlint_version='1.7.12'
readonly actionlint_sha256='8aca8db96f1b94770f1b0d72b6dddcb1ebb8123cb3712530b08cc387b349a3d8'
readonly zizmor_version='1.32.0'
readonly zizmor_sha256='bb110cc765eca9cd7efb2e284a956be5974c60027b0970f16397d86208a9957b'

if [[ $# -gt 0 ]]; then
  printf 'Usage: scripts/lint-shell-and-workflows.sh\n' >&2
  exit 1
fi

if ! repository_root="$(git rev-parse --show-toplevel 2> /dev/null)"; then
  printf 'lint-shell-and-workflows.sh must run inside a Git worktree.\n' >&2
  exit 1
fi

cd "$repository_root"

platform="$(uname -s)-$(uname -m)"
if [[ "$platform" != 'Linux-x86_64' ]]; then
  printf 'The linters are pinned for Linux-x86_64 alone, and this is %s.\n' "$platform" >&2
  exit 1
fi

tool_directory="${MAILFATHOM_LINT_TOOLS:-${XDG_CACHE_HOME:-$HOME/.cache}/mailfathom-lint}"

# Downloads, verifies, and unpacks one archive into `<tool_directory>/<name>-<version>/`, and prints the binary's path.
# The directory is staged and renamed into place, so an interrupted run or a concurrent one never leaves a half-written
# tool where the next run would trust it.
install_tool() {
  local name="$1" version="$2" sha256="$3" url="$4" member="$5"
  local destination="$tool_directory/$name-$version"
  local staging

  if [[ ! -x "$destination/$name" ]]; then
    mkdir -p "$tool_directory"
    staging="$(mktemp -d "$tool_directory/.$name-XXXXXX")"

    if ! curl --fail --silent --show-error --location --max-time 120 --retry 2 \
      --output "$staging/archive" "$url"; then
      rm -rf "$staging"
      printf 'Could not download %s %s from %s.\n' "$name" "$version" "$url" >&2
      return 1
    fi

    if ! printf '%s  %s\n' "$sha256" "$staging/archive" | sha256sum --check --status; then
      rm -rf "$staging"
      printf '%s %s does not match the checksum pinned in this script; nothing was installed.\n' "$name" "$version" >&2
      return 1
    fi

    # Checked by hand: a function called inside `$(…)` does not inherit `set -e`.
    if ! tar -xzf "$staging/archive" -C "$staging" "$member" \
      || ! mkdir "$staging/tool" || ! mv "$staging/$member" "$staging/tool/$name"; then
      rm -rf "$staging"
      printf 'Could not unpack %s from the %s %s archive.\n' "$member" "$name" "$version" >&2
      return 1
    fi
    mv -T "$staging/tool" "$destination" 2> /dev/null || true
    rm -rf "$staging"
  fi

  printf '%s\n' "$destination/$name"
}

shellcheck="$(install_tool shellcheck "$shellcheck_version" "$shellcheck_sha256" \
  "https://github.com/koalaman/shellcheck/releases/download/v$shellcheck_version/shellcheck-v$shellcheck_version.linux.x86_64.tar.gz" \
  "shellcheck-v$shellcheck_version/shellcheck")"
actionlint="$(install_tool actionlint "$actionlint_version" "$actionlint_sha256" \
  "https://github.com/rhysd/actionlint/releases/download/v$actionlint_version/actionlint_${actionlint_version}_linux_amd64.tar.gz" \
  'actionlint')"
zizmor="$(install_tool zizmor "$zizmor_version" "$zizmor_sha256" \
  "https://github.com/zizmorcore/zizmor/releases/download/v$zizmor_version/zizmor-x86_64-unknown-linux-gnu.tar.gz" \
  'zizmor')"

# ShellCheck's text output aborts on a non-ASCII character under a locale that cannot encode it, and the runner's
# default is one.
export LC_ALL=C.UTF-8

failed=()

printf '== ShellCheck %s ==\n' "$shellcheck_version"
if ! git ls-files -z -- '*.sh' | xargs -0 "$shellcheck" --external-sources --source-path=SCRIPTDIR --severity=warning; then
  failed+=('ShellCheck')
fi

# `-pyflakes=` because a `run:` block written in Python would otherwise be linted by whichever pyflakes happens to be on
# `PATH`, which is exactly the variance this script exists to remove; no workflow here writes one.
printf '== actionlint %s ==\n' "$actionlint_version"
if ! SHELLCHECK_OPTS='--severity=warning' "$actionlint" -config-file .github/actionlint.yaml \
  -shellcheck "$shellcheck" -pyflakes=; then
  failed+=('actionlint')
fi

printf '== zizmor %s ==\n' "$zizmor_version"
if ! "$zizmor" --offline --quiet --no-progress --config .github/zizmor.yml .github; then
  failed+=('zizmor')
fi

if [[ ${#failed[@]} -gt 0 ]]; then
  printf '\nFailed: %s\n' "${failed[*]}" >&2
  exit 1
fi

printf '\nNo findings.\n'
