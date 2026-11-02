#!/usr/bin/env bash
# Copyright © 2026 Krzysztof Kasprowicz
# Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
# Project repository: https://github.com/fathomise/MailFathom


# Whether the client's lint, formatting, and unit suite can be narrowed to the files a change touched.
# Sourced by the fast loop after `scripts/resolve-changed-stacks.sh`, whose `change_reaches_client_stack`
# it calls; it defines two functions and runs nothing on its own.
#
# Over the whole workspace those three are most of what a client change costs the fast loop, and almost
# none of it reads the files the change touched: ESLint and Prettier judge a file by itself, and
# `vitest related` finds the test files that import a changed source through Vitest's own module graph.
# The type check is not narrowed, because it is what catches a changed type breaking an unchanged
# importer.
#
# What the narrowing costs is a type-aware lint rule's verdict on an unchanged file, which can move when
# a changed file's types move. `CI` lints the whole workspace, checks the whole tree's formatting, and runs
# the whole suite on the pull request, so what stays local is an earlier verdict rather than the verdict,
# and the fast loop says as much when it narrows.

# Why this change's client checks run over the whole workspace, as one sentence, or nothing and a non-zero
# status when they can be narrowed to the files it touched. Run from the repository root. The first
# argument is the branch's removed or renamed paths, one per line, as `list_removed_or_renamed_paths`
# reports them; the rest are every path the change touched.
#
# A removed client file refuses the narrowing because nothing is left to name it: the importers it broke
# are unchanged files, and `vitest related` cannot be handed a path that no longer exists. A client path
# outside `frontend/` refuses it because the client reads it through its build rather than through an
# import — `.editorconfig` through Prettier, `Version.props` and the script reading it through
# `Client.App/vite.config.ts`.
explain_whole_client_workspace() {
  local removed_paths="$1"
  local path
  shift

  while IFS= read -r path; do
    if [[ "$path" == frontend/* ]]; then
      printf '%s was removed or renamed, and the files that imported it are not among the files the change touched.\n' "$path"
      return 0
    fi
  done <<< "$removed_paths"

  for path in "$@"; do
    change_reaches_client_stack "$path" || continue

    if [[ "$path" != frontend/* ]] || is_shared_client_input "${path#frontend/}"; then
      printf '%s decides how every client file is checked, not only the files that import it.\n' "$path"
      return 0
    fi
  done

  return 1
}

# Whether a path under `frontend/`, given relative to it, is read by the toolchain for every file rather
# than imported by some of them: the manifests and the lock file deciding what is installed, and the
# configuration of the compiler, the linter, the formatter, and the suite.
is_shared_client_input() {
  case "$1" in
    pnpm-lock.yaml | pnpm-workspace.yaml | eslint.config.ts | vitest.config.ts | .prettierignore | \
      src/Client.App/vite.config.ts | src/Client.App/vitest.setup.ts)
      return 0
      ;;
  esac

  case "${1##*/}" in
    package.json | .npmrc | .editorconfig | .prettierrc* | tsconfig*.json)
      return 0
      ;;
  esac

  return 1
}
