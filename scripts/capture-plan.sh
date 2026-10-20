#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/fathomise/MailFathom

# What `scripts/capture-design.sh` and `scripts/capture-client.sh` have in common, sourced by both rather than written
# twice: which screens, size classes and compositions an invocation asked for, where a capture may be written, how many
# pairs one invocation produces, and how the browser half is reached. A rule stated once here is a rule the two sides cannot
# disagree about, which is the whole premise of comparing their output.
#
# It is sourced rather than run, so it defines and checks and never captures anything itself. The caller defines
# `usage` before sourcing this, and calls `run_capture` with the plan it built.

# Where the sibling scripts are, resolved from this file rather than from the repository root: a script calls the one
# beside it, which is what lets the whole set be run against a checkout that is not the one they were read from.
capture_scripts_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"

if ! repository_root="$(git rev-parse --show-toplevel 2>/dev/null)"; then
  printf 'Not inside a Git repository.\n' >&2
  exit 1
fi

if ! command -v jq >/dev/null 2>&1; then
  printf 'jq is required to read the screen manifest.\n' >&2
  exit 1
fi

if ! command -v pnpm >/dev/null 2>&1; then
  printf 'Capturing drives a browser out of the client workspace, which needs Node and pnpm on the path.\n' >&2
  printf 'See docs/operations/local-development.md.\n' >&2
  exit 1
fi

manifest="$repository_root/frontend/design-parity/screens.json"

if [[ ! -f "$manifest" ]]; then
  printf 'No screen manifest at %s.\n' "$manifest" >&2
  exit 1
fi

out=''
theme='light'
screens=()
classes=()
compositions=()

while (($# > 0)); do
  case "$1" in
    --out)
      out="${2:-}"
      shift 2
      ;;
    --screen)
      screens+=("${2:-}")
      shift 2
      ;;
    --class)
      classes+=("${2:-}")
      shift 2
      ;;
    --composition)
      compositions+=("${2:-}")
      shift 2
      ;;
    --theme)
      theme="${2:-}"
      shift 2
      ;;
    -h | --help | help)
      usage
      exit 0
      ;;
    *)
      printf 'Unknown argument: %s\n\n' "$1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

if [[ "$theme" != 'light' && "$theme" != 'dark' ]]; then
  printf 'A theme is light or dark, not %s.\n' "$theme" >&2
  exit 1
fi

# Where a capture may be written, and the reason it is checked rather than trusted. A capture is generated rather than
# authored — the design side rasterises files the tree already holds, and the client side shows whatever mail the run
# was answering with, which is the half that must never be committed. So the repository is refused outright and the two
# places that are not it are named.
scratch_root="$(realpath -m -- "${TMPDIR:-/tmp}")"
artifacts_root="$(realpath -m -- "$repository_root/artifacts")"

if [[ -z "$out" ]]; then
  printf 'Name where the captures go with --out.\n\n' >&2
  usage >&2
  exit 1
fi

out="$(realpath -m -- "$out")"
tree_root="$(realpath -m -- "$repository_root")"

under() {
  [[ "$1" == "$2" || "$1" == "$2"/* ]]
}

# Under `artifacts/`, which the repository ignores, or in the session's own scratch directory and outside the
# repository. The second condition is not redundant: a checkout can itself sit under the scratch directory — every
# fixture repository this suite builds does — and without it every path in such a tree would pass for being scratch.
if ! under "$out" "$artifacts_root" && ! { under "$out" "$scratch_root" && ! under "$out" "$tree_root"; }; then
  printf 'Refusing to write captures to %s.\n' "$out" >&2
  printf 'They go to this session'\''s scratch directory under %s, or under %s. A capture is design or client\n' \
    "$scratch_root" "$artifacts_root" >&2
  printf 'material and never enters the tree.\n' >&2
  exit 1
fi

# A screen is captured at the whole window-size matrix unless it narrows itself, and a narrowing states why in the
# manifest. Both are resolved here, once, into the manifest both callers read, so the two sides cannot disagree about
# which sizes a screen is held at.
unexplained="$(jq -r '[.screens[] | select((has("compositions") or has("classes")) and ((.narrowedBecause // "") == "")) | .id] | join(", ")' "$manifest")"

if [[ -n "$unexplained" ]]; then
  printf 'A screen captured at less than the whole matrix says why in narrowedBecause, and these do not: %s\n' "$unexplained" >&2
  exit 1
fi

# A narrowing naming a size or a class the matrix does not hold would quietly hold the screen at less than it says.
unknown="$(jq -r '
  [.compositions[].name] as $names | [.compositions[].class] as $classes
  | [.screens[] | (.compositions[]? | select(IN($names[]) | not)), (.classes[]? | select(IN($classes[]) | not))]
  | unique | join(", ")' "$manifest")"

if [[ -n "$unknown" ]]; then
  printf 'A screen narrows itself to sizes or classes the matrix does not hold: %s\n' "$unknown" >&2
  exit 1
fi

resolved_manifest="$(
  jq '
    .compositions as $matrix
    | .screens |= map(
        if has("compositions") or has("classes") then
          . as $screen
          | .compositions = [
              $matrix[]
              | select(.name as $name | .class as $class
                  | any($screen.compositions[]?; . == $name) or any($screen.classes[]?; . == $class))
              | .name
            ]
        else
          .compositions = [$matrix[].name]
        end
      )' "$manifest"
)"

if ((${#screens[@]} == 0)); then
  mapfile -t screens < <(jq -r '.screens[].id' <<<"$resolved_manifest")
fi

for class in "${classes[@]}"; do
  if ! jq -e --arg class "$class" 'any(.compositions[]; .class == $class)' >/dev/null "$manifest"; then
    printf 'The manifest names no size class %s. It holds: %s\n' \
      "$class" "$(jq -r '[.compositions[].class] | unique | join(", ")' "$manifest")" >&2
    exit 1
  fi

  mapfile -t -O "${#compositions[@]}" compositions < <(jq -r --arg class "$class" '.compositions[] | select(.class == $class) | .name' "$manifest")
done

if ((${#compositions[@]} == 0)); then
  mapfile -t compositions < <(jq -r '.compositions[].name' "$manifest")
fi

for screen in "${screens[@]}"; do
  if ! jq -e --arg screen "$screen" 'any(.screens[]; .id == $screen)' >/dev/null "$manifest"; then
    printf 'The manifest names no screen %s. It holds: %s\n' \
      "$screen" "$(jq -r '[.screens[].id] | join(", ")' "$manifest")" >&2
    exit 1
  fi
done

for composition in "${compositions[@]}"; do
  if ! jq -e --arg name "$composition" 'any(.compositions[]; .name == $name)' >/dev/null "$manifest"; then
    printf 'The manifest names no composition %s. It holds: %s\n' \
      "$composition" "$(jq -r '[.compositions[].name] | join(", ")' "$manifest")" >&2
    exit 1
  fi
done

# How many pairs one invocation produces: one screen at the whole matrix.
#
# The bound is the point of the loop rather than a safety margin. A capture costs a browser and a development server's
# worth of time per pair, and the report a comparison prints grows with every pair it reads, so a run that captured
# every screen at every size would take the better part of an hour and answer with more than anybody reads at once. A
# parity pass proceeds one screen at a time, which is what this allows: that screen at every size, at one size class
# with `--class`, or at one size with `--composition` — or several screens at a narrower set.
maximum_pairs="$(jq '.compositions | length' "$manifest")"

# How many pairs this invocation would produce, counted from the manifest before either caller does anything at all.
# It is checked here rather than on the built plan so the refusal costs neither a browser nor a development server —
# a caller that had already started one would be reporting "nothing was captured" while tearing something down.
asked_for="$(
  jq -r \
    --argjson screens "$(printf '%s\n' "${screens[@]}" | jq -R . | jq -s .)" \
    --argjson compositions "$(printf '%s\n' "${compositions[@]}" | jq -R . | jq -s .)" '
      [.screens[] | select(.id | IN($screens[])) | .compositions[] | select(IN($compositions[]))] | length' \
    <<<"$resolved_manifest"
)"

if ((asked_for == 0)); then
  printf 'Nothing to capture: no screen you named is drawn at any composition you named.\n' >&2
  exit 1
fi

if ((asked_for > maximum_pairs)); then
  printf 'That asks for %s pairs and one invocation produces at most %s.\n' "$asked_for" "$maximum_pairs" >&2
  printf 'Name fewer screens, a size class, or fewer compositions; nothing was captured.\n' >&2
  exit 1
fi

# Runs one built plan. Everything that decides whether it may run has already been decided above.
run_capture() {
  mkdir -p "$out"

  printf '%s' "$1" | pnpm --dir "$repository_root/frontend" exec jiti design-parity/capture.ts
}
