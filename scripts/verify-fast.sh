#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/fathomise/MailFathom

set -euo pipefail

if ! repository_root="$(git rev-parse --show-toplevel 2>/dev/null)"; then
  printf 'verify-fast.sh must run inside a Git repository.\n' >&2
  exit 1
fi

cd "$repository_root"

# Resolved from this script's own location rather than from the repository it verifies, because the
# workflow contract suite runs the committed scripts against a fixture checkout that carries none.
# shellcheck source=scripts/resolve-base-remote.sh
source "$(dirname "${BASH_SOURCE[0]}")/resolve-base-remote.sh"
# shellcheck source=scripts/list-branch-changes.sh
source "$(dirname "${BASH_SOURCE[0]}")/list-branch-changes.sh"
# shellcheck source=scripts/resolve-changed-stacks.sh
source "$(dirname "${BASH_SOURCE[0]}")/resolve-changed-stacks.sh"
# shellcheck source=scripts/resolve-changed-client-files.sh
source "$(dirname "${BASH_SOURCE[0]}")/resolve-changed-client-files.sh"
# shellcheck source=scripts/verification-record.sh
source "$(dirname "${BASH_SOURCE[0]}")/verification-record.sh"

# The integration branch is never the subject of a change, so a run there reports on code nobody is
# about to modify. verify-full.sh cannot catch this through its base check: origin/main is trivially
# its own ancestor. A detached HEAD is left alone, because it is not a branch anyone pushes to.
current_branch="$(git symbolic-ref --quiet --short HEAD || true)"
if [[ "$current_branch" == 'main' || "$current_branch" == 'master' ]]; then
  printf 'verify-fast.sh must not run on %s. Switch to the branch that carries the change.\n' \
    "$current_branch" >&2
  exit 1
fi

# The base is asked here rather than only in the full gate, because this loop is what a change is
# actually held to locally: the full gate's steps are all asserted again by `CI` on the pull request,
# and this question is not one of them. It is asked before the record is consulted for the reason the
# full gate gives about its own digest — whether the branch still contains `main` is a fact about the
# branch rather than about the tree, so no record can stand in for it — and before anything expensive
# runs, so a branch that has to be rebased learns that in a second rather than after a build.
#
# A checkout whose remotes name no MailFathom at all is the one case that carries on regardless, and
# it is a different case from a branch that fell behind: there is no base to be behind, so there is
# nothing to refuse against and nothing a refusal would teach that the hint does not. That is the
# fork whose second remote has not been added yet, and blocking it would stop a contributor from
# building and testing while they fix exactly what the hint names. The full gate still refuses it.
base_remote=''

if resolve_base_remote > /dev/null; then
  if ! base_remote="$(require_base_is_contained 'verify-fast.sh')"; then
    exit 1
  fi
else
  base_remote_resolution_hint
  printf 'The fast loop carries on without that answer; scripts/verify-full.sh and the pull request will not.\n'
fi

verification_digest="$(resolve_verification_digest)"

# What the branch changed is read here rather than beside the flows below, because one of the things
# it decides is whether this run happens at all. It costs a handful of git invocations.
if ! changed_paths="$(list_changed_paths)"; then
  printf 'verify-fast.sh cannot determine which files the branch changed. Verifying the wrong scope proves nothing, so fix the repository state instead.\n' >&2
  exit 1
fi

if ! removed_paths="$(list_removed_or_renamed_paths)"; then
  printf 'verify-fast.sh cannot determine which files the branch removed. Verifying the wrong scope proves nothing, so fix the repository state instead.\n' >&2
  exit 1
fi

# Which stack's flow this change earns, decided from the paths rather than by whoever started the
# run — the same question `ci.yml` answers in `detect-changes`, from the same two lists.
# `scripts/resolve-changed-stacks.sh` carries them and the reasoning. Removals feed the decision
# beside the changes, because deleting the last file of a project moves what the solution builds as
# surely as editing one does, and `list_changed_paths` deliberately reports no deletion.
mapfile -t touched_paths < <(printf '%s\n%s\n' "$changed_paths" "$removed_paths" | grep -Ev '^$' | sort --unique)

run_service_stack=''
run_client_stack=''
changed_client_files=()

if change_reaches_service_stack "${touched_paths[@]}"; then
  run_service_stack='yes'
fi

if change_reaches_client_stack "${touched_paths[@]}"; then
  run_client_stack='yes'
fi

# The whitespace repair below runs over `backend/` as a folder, which reads an `--include` path against
# that folder rather than the working directory and repairs nothing at a path it cannot resolve, so
# this keeps the C# under `backend/` and names it relative to there. No project builds a `.cs` file
# outside `backend/`, so nothing the loop judges is left unrepaired.
mapfile -t changed_backend_csharp_files < <(printf '%s\n' "$changed_paths" | grep -E '^backend/.*\.cs$' | sed 's|^backend/||' | sort --unique)

# The full gate builds, tests, and verifies the formatting this loop repairs, in whichever stacks
# this change reaches, so a green run of it over this content leaves nothing here to find. The
# implication runs one way only: passing the loop says nothing about coverage or the contract suite.
if [[ -z "${VERIFY_FORCE:-}" ]]; then
  for proving_gate in 'verify-fast' 'verify-full'; do
    if verification_already_recorded "$proving_gate" "$verification_digest"; then
      report_verification_already_recorded 'verify-fast' "$proving_gate"
      exit 0
    fi
  done
fi

# The loop runs in three phases rather than one stack after another, so a failure ends the run as
# early as it can and nothing the loop can repair fails it. First the repairs, which need no
# compilation: whitespace and the final newline in the changed C# files, and Prettier over the changed
# client files. Then everything that compiles or checks, in every stack the change reaches, judging
# the tree those repairs left — so a misplaced indent is repaired rather than reported, while a build,
# lint, or type error still fails in seconds rather than behind somebody else's suite. Then the unit
# suites, `--no-build` against the binaries the build produced, so nothing builds twice. No pass here
# loads the MSBuild workspace to format.

# Locked mode here and not only in the final gate: a pin moved without regenerating the lock files
# fails restore with NU1004, and discovering that after the whole coverage collection has already run
# wastes the loop this script exists to shorten. Regenerate with
# `dotnet restore backend/MailFathom.slnx --force-evaluate` as part of the change that moves the pin.
if [[ -n "$run_service_stack" ]]; then
  dotnet restore backend/MailFathom.slnx --locked-mode
fi

# `frontend/` is a pnpm workspace, so every client step runs from that directory and resolves against
# `frontend/pnpm-lock.yaml`; `--frozen-lockfile` fails rather than rewriting it, which is what locked
# mode is to the service restore above. The install comes before the repairs because Prettier is one
# of the workspace's own packages.
if [[ -n "$run_client_stack" ]]; then
  if ! command -v pnpm > /dev/null 2>&1; then
    printf 'This change reaches the client stack, which needs Node and pnpm on the path. See docs/operations/local-development.md.\n' >&2
    exit 1
  fi

  pnpm --dir frontend install --frozen-lockfile

  # `scripts/resolve-changed-client-files.sh` decides whether the lint, the suite, and the formatting
  # pass narrow to the client files the branch changed, and refuses where the change reaches a shared
  # client input or removes a client file. Said either way, for the reason the service suites are.
  if client_whole_workspace_reason="$(explain_whole_client_workspace "$removed_paths" "${touched_paths[@]}")"; then
    printf 'The client lint, suite, and formatting run over the whole workspace, because %s\n' "$client_whole_workspace_reason"
  else
    mapfile -t changed_client_files < <(printf '%s\n' "$changed_paths" | sed -n 's#^frontend/##p' | sort --unique)

    printf 'The client lint, suite, and formatting are narrowed to the %d changed client files below, and the type check runs whole. A type-aware lint rule can change its verdict on an unchanged file when a changed file'\''s types move, so the whole workspace'\''s lint, suite, and formatting are left to the pipeline:\n' \
      "${#changed_client_files[@]}"
    printf '  frontend/%s\n' "${changed_client_files[@]}"
  fi
fi

# The repairs, one per stack, and the only steps in either gate that rewrite a file rather than
# report on it. Each runs over what the branch changed and needs no compilation: `dotnet format
# whitespace --folder` reads the C# files alone, without loading the MSBuild workspace, and repairs
# indentation, spacing, line endings, and a missing final newline in about three seconds, so a change
# reaches the pipeline formatted and the build below judges an already formatted tree.
#
# What the build judges is everything a text pass cannot repair: `backend/Directory.Build.props` sets
# `EnforceCodeStyleInBuild` beside `TreatWarningsAsErrors` and `.editorconfig` gives the IDE rules
# severity `warning`, so an unnecessary using, a missing licensing header, an analyzer finding, and a
# compile error each fail it with the file and line. The order of using directives is the one
# formatting rule nothing here repairs or reports: sorting them needs the workspace, which costs more
# than the rule has ever cost the loop, so the full gate and `CI` are what report it, together with a
# diagnostic with no code fix — `IDE0060` the one that reaches this repository — and any change never
# run through this loop, since they verify the whole formatting instead of repairing it.
digest_before_repair="$(resolve_verification_digest)"

if [[ -n "$run_service_stack" ]] && ((${#changed_backend_csharp_files[@]} > 0)); then
  dotnet format whitespace backend --folder --include "${changed_backend_csharp_files[@]}"
fi

# The cache Prettier keeps sits under `frontend/node_modules/`, never in the tree.
if [[ -n "$run_client_stack" ]]; then
  if ((${#changed_client_files[@]} > 0)); then
    pnpm --dir frontend exec prettier --write --cache --ignore-unknown "${changed_client_files[@]}"
  else
    pnpm --dir frontend run format
  fi
fi

digest_after_repair="$(resolve_verification_digest)"

# The service build. It departs from the full gate's in three properties, and the full gate and `CI`
# keep all three. The SDK stamps the HEAD commit into every assembly's informational version and
# every project's Source Link file, so a new commit would recompile the whole solution and re-run
# every analyzer whatever it touched; without the stamp a commit recompiles what it reached and what
# depends on it. `SkipPipelineOnlyAnalyzers=true` leaves out `CA2000`, two fifths of every analyzer
# second the solution spends, and `IDE0055`, whose whitespace the repair above has already made;
# `backend/Directory.Build.props` says how. Because the two gates build with different properties,
# switching between them in one worktree rebuilds the solution once.
if [[ -n "$run_service_stack" ]]; then
  dotnet build backend/MailFathom.slnx --configuration Release --no-restore \
    -p:IncludeSourceRevisionInInformationalVersion=false -p:EnableSourceLink=false -p:SkipPipelineOnlyAnalyzers=true
fi

# The client's checks. The lint is the client's `TreatWarningsAsErrors`: every rule reports as an error
# and `--max-warnings 0` refuses a warning too, so there is no severity below failing. The type check
# is the closest thing the client has to the Release build, because Vite's build strips types rather
# than checking them.
if [[ -n "$run_client_stack" ]]; then
  # Narrowed, the configuration's own ignores still decide what is linted, so a changed path ESLint
  # does not lint is passed over rather than reported.
  if ((${#changed_client_files[@]} > 0)); then
    pnpm --dir frontend exec eslint --max-warnings 0 --no-warn-ignored "${changed_client_files[@]}"
  else
    pnpm --dir frontend run lint
  fi

  pnpm --dir frontend run typecheck
fi

# The service suites run whole, in one invocation that runs the test modules side by side. Narrowing
# them to the projects a change names costs more than it saves: a change reaching most of the core
# projects names nine suites, which one `dotnet test --project` after another takes about 225 seconds
# over, while the whole solution in one parallel run takes about eighty, and a change reaching a
# single project would save some fifty seconds against that. `Infrastructure.UnitTests` alone sets
# the floor.
if [[ -n "$run_service_stack" ]]; then
  dotnet test --solution backend/MailFathom.slnx --configuration Release --no-build
fi

# Narrowed, Vitest runs the test files whose imports reach a changed file and prints how many of the
# workspace's that was. Coverage is off there: a figure over a few test files describes nothing, and
# writing it would share `artifacts/coverage/client/` with whatever whole run is beside it.
if [[ -n "$run_client_stack" ]]; then
  if ((${#changed_client_files[@]} > 0)); then
    pnpm --dir frontend exec vitest related --run --coverage.enabled=false "${changed_client_files[@]}"
  else
    pnpm --dir frontend run test
  fi
fi

# A rewrite by the repairs is their own: whitespace, a final newline, Prettier's layout. A second run
# over the repaired files would rebuild and retest only to say what this one said, so the run records
# the repaired tree instead of the one it started from — provided nothing else moved it: not before
# the repairs, and not while the build or the suites ran after them. A tree that changed then records
# nothing; an edit landing while a repair itself runs is indistinguishable from its rewrite and is
# recorded with it. The record settles no more than a run that rewrote nothing does: what no repair
# acts on is still left to the full gate and `CI`, as the comment above the repairs says.
digest_after_judging="$(resolve_verification_digest)"
formatters_rewrote_the_tree=''

if [[ -n "$digest_before_repair" && "$digest_after_repair" != "$digest_before_repair" ]]; then
  if [[ "$verification_digest" == "$digest_before_repair" && "$digest_after_judging" == "$digest_after_repair" ]]; then
    formatters_rewrote_the_tree='yes'
    verification_digest="$digest_after_repair"
  else
    printf 'The formatting pass rewrote files, but the tree also changed while the gate ran, so nothing is recorded: run the gate again.\n'
  fi
fi

# Whitespace errors in a file no formatter reads — a page, a workflow, a Helm template — are checked
# here rather than only in the full gate, because this loop is the gate a change is held to locally
# and nothing in the pipeline asks the question at all. It costs milliseconds and reads the diff
# rather than the tree, so it runs last, over whatever the formatting passes left. The committed
# range needs the base, so it is asked only where the base resolved; the other two are about this
# working tree and are always asked.
if [[ -n "$base_remote" ]]; then
  git diff --check "$base_remote/main...HEAD"
fi

git diff --cached --check
git diff --check

# A change no build reads — documentation, a skill, a deployment asset — runs neither flow and says
# so, because a gate that prints nothing and exits zero reads as a gate that was not run.
if [[ -z "$run_service_stack" && -z "$run_client_stack" ]]; then
  printf 'This change reaches neither stack, so no solution was restored, built, or tested. The whole-tree contracts are what answers for it, and they run on the pull request; scripts/verify-full.sh is how to have that answer now.\n'
fi

# Records nothing when the tree changed while the run went on, because what passed is then not what
# is here now.
record_verification 'verify-fast' "$verification_digest"

# Said only once the record exists, because the sentence is an instruction to skip the next run, and
# a whitespace error or a tree that moved in the last seconds would have left nothing to skip to.
if [[ -n "$formatters_rewrote_the_tree" ]] && verification_already_recorded 'verify-fast' "$verification_digest"; then
  printf 'The formatting pass rewrote files and nothing else moved, so the formatted tree is recorded and no second run is needed: commit the rewrite with the change.\n'
fi
