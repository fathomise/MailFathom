---
name: start-task
description: Use when beginning repository work that may edit files, change dependencies, or require roadmap, documentation, or ADR context.
license: AGPL-3.0-only
metadata:
  author: Krzysztof Kasprowicz
  repository: https://github.com/fathomise/MailFathom
---

# Start Task

## Two roles

This repository is public and its roadmap board is not. The same contract therefore runs in two
places, and several steps below mean different things in each. Resolve which one applies before
the branch is renamed rather than assuming, and state it in the brief.

1. Run `scripts/inspect-workspace.sh` and read `Base branch`. `origin/main` means `origin` is
   MailFathom itself. Anything else — `upstream/main`, or `unresolved` — means `origin` is a fork,
   which is the **fork role**.
2. When `origin` is MailFathom, ask the repository what this credential may do there, because a
   clone is not a grant — anybody can clone a public repository, and an organization member holds no
   role on it until a team gives one:

   ```bash
   gh api repos/fathomise/MailFathom --jq '.permissions'
   ```

   `push: true` is the **owner's checkout**. `push: false` is neither role: the issue type, every
   label, the milestone, the claim, and the push would each be refused, so return `blocked` before the workflow
   below renames anything, and name the repair — fork the repository, repoint `origin` at the fork,
   and add MailFathom back as a second remote, which makes this the fork role. An answer with no
   `permissions` key is an unauthenticated `gh` rather than a refusal, since a public repository
   answers a read for anybody; that is a credential to repair, and it says nothing about access.
3. Probe the board, in either role, because access to it is a separate fact from which repository
   this is: the owner grants read or write on project `1` to a contributor whenever they decide to:

   ```bash
   gh api graphql -f query='{ organization(login: "fathomise") { projectV2(number: 1) { viewerCanUpdate } } }'
   ```

   `true` is write access and every board step below applies. `false` is read access: read the board
   for context and report each write as unavailable rather than attempting it. No access reads as
   `"projectV2": null` beside a `NOT_FOUND` error saying `Could not resolve to a ProjectV2 with the
   number 1` — GitHub hides a project the viewer cannot see rather than refusing it, so nothing in
   that answer says *permission* and it must not be reported as the board having been deleted or the
   number being wrong. A `CLAUDE.local.md` written by `$get-started-contributors` states the answer
   already, and this probe confirms it rather than replacing it.

   A negative is checked before it is reported, because the probe sees the credential's access rather
   than the account's: confirm `gh auth status` lists the `project` scope, and that no `GH_TOKEN` or
   `GITHUB_TOKEN` in the environment is displacing the stored credential — `gh` prefers one and cannot
   list its scopes. Without that scope the call fails identically whoever is running it, so report it
   as a credential to repair, never as the board being out of reach.

The fork role sets no issue type, assigns no label and no milestone, and never pushes to `fathomise/MailFathom`,
because those are write access to the repository rather than to the board, and no board grant confers
them. Those are not degraded versions of the owner's steps — they are steps that belong to a
maintainer, and attempting one produces a permission error rather than a partial result. The board
fields — `Area`, `Queue`, `Size` — follow the probe instead of the role, so a contributor the owner
granted write to sets them exactly as this file says, and the owner's own checkout would stop setting
them if that access were ever removed.

## Workflow

1. Resolve the repository root and run `scripts/inspect-workspace.sh`.
2. **Owner's checkout only:** rename the branch with `git branch -m agent/<short-description>` when
   `Worktree` is `linked worktree` and `Branch` matches `worktree-*`. That combination is the harness
   naming its own scratch branch, which this workflow rejects, so renaming it is the expected first
   move rather than a sign that the workspace is wrong. Never rename any other branch: in the primary
   checkout the branch is the developer's own, and renaming `main` there would destroy it while
   making no progress on the task. Any other mismatch is corrected by creating or entering a
   worktree, not by renaming. In the fork role the branch name is the contributor's to choose and
   nothing here renames it.
3. Fetch the base the change will merge into, then run workspace inspection again. In the owner's
   checkout that is `git fetch origin main`. In the fork role it is `git fetch upstream main`, and
   when `Base branch` reported `unresolved` the corrective step comes first:

   ```bash
   git remote add upstream https://github.com/fathomise/MailFathom.git
   git fetch upstream main
   ```

   That is a fix rather than a blocker. Without it every gate would compare the branch against the
   fork's own `main`, which is whatever was last synced rather than what the pull request merges
   into, and a green run would prove nothing.
4. Stop before edits unless `Contains base branch` is `yes` and the working tree is either clean or
   fully inventoried under a user-approved preservation plan. In the owner's checkout also require
   `Branch` matching `agent/*` and `Worktree` reporting `linked worktree`; in the fork role neither
   applies, and an ordinary clone on a contributor-named branch is `safe`.
   - If the working tree is dirty, run `git status --short --untracked-files=all` and inventory every
     existing path.
   - Return `blocked` until every path is inventoried and the user explicitly approves its
     preservation plan, or the changes are moved to a separate worktree. Never assume existing
     changes are unrelated.
5. Classify the task by what governs it: an ADR, an existing issue, or documentation-only work. Work
   nothing backs is an ordinary case and a feature can be one, so never invent a governing document
   to supply the classification — say that nothing governs it and let the issue body carry the scope
   instead.
6. Read whatever step 5 named, plus affected implemented-behavior documentation and relevant ADRs.
7. Name the protected paths the task reaches, so the brief says whether the pull request will wait
   on a maintainer's approval. `.github/`, `.config/`, `.agents/`, `.claude/`, and `docs/decisions/`, an `.editorconfig`,
   `.gitattributes`, `.worktreeinclude`, `AGENTS.md`, `CLAUDE.md`, or `Directory.Build.props` at any
   depth, and the repository-root `CHANGELOG.md`, `CLA.md`, `LICENSE`, `NOTICE`, `NuGet.config`,
   `Version.props`, and `global.json` are open to every author in either role: the `Protected paths`
   check holds a pull request that changes one from an author who is not a maintainer until a
   maintainer approves its current head, passes a maintainer's own on authorship, and does nothing
   more. Reaching one is never a reason to stop, to split the change out, or to raise an
   issue instead of implementing it — make the change in the same pull request. A rule a path
   carries on its own grounds still holds: an ADR needs the owner's explicit approval, and
   `CHANGELOG.md` is written only by the release pull request.
8. Identify the GitHub issue that governs the task, reading
   `docs/operations/issue-tracking.md` first. Create it when none exists; its body draws on what
   step 6 read and ends with the `## Baseline` section that page § *Issue content* asks for, naming
   the commit `git rev-parse` reads from the base step 3 fetched — `origin/main`, or `upstream/main`
   in the fork role. Below, `<base>` is that same ref.

   An issue that already existed was written against an older `main`, and its body says which. Read
   that commit and what landed on top of it before trusting anything the body found:

   ```bash
   baseline=$(gh issue view <number> --repo fathomise/MailFathom --json body --jq .body \
     | sed -n '/^##* Baseline/,$p' | grep -oE '[0-9a-f]{40}' | head -n 1)
   if [ -n "$baseline" ] && git cat-file -e "$baseline^{commit}" 2>/dev/null; then
     git log --oneline "$baseline"..<base>
   else
     echo 'no usable baseline: a reading of unknown age'
   fi
   ```

   The guard is not decoration: an empty `$baseline` turns the range into `..<base>`, which git reads
   as `HEAD..<base>`, and step 4 has already made that empty — so without it an issue with no
   section would read as current. Hold each commit the log lists against what the body names — a
   file, a type, a route, a rule, the cause of a defect — and re-check in the current tree every
   finding one of them touched; one that changed is corrected in the body and the section moved to
   `<base>`, so the next reader inherits the current reading rather than this one. In the fork role
   the body is usually the maintainer's rather than the contributor's to edit, so the changed
   findings and the new SHA go in the brief's `Baseline:` line instead and triage carries them into
   the body. An empty list means the body is as current as the tree. `no usable baseline` means the
   issue predates the rule, came from outside the project, or names a commit this clone does not
   have: say so in the brief, and read the body as a reading of unknown age.

   Then read what it waits on, before it is placed and before it is claimed:

   ```bash
   gh api repos/fathomise/MailFathom/issues/<number>/dependencies/blocked_by \
     --jq '.[] | select(.state == "open") | "#\(.number) \(.title)"'
   ```

   Every line it returns is a refusal. Return `blocked`, name each one, and start nothing: those
   relations are where the order between issues is recorded, and an issue taken past one produces a
   change written against a contract that has not landed. Say which blockers would have to close,
   so the answer is a next step rather than a stop.

   An empty answer is read against the body rather than taken as permission. An issue whose order
   was never recorded returns nothing while its `## Dependencies` section names what it waits on,
   so compare the two and write the missing relations — the call is in
   `docs/operations/issue-tracking.md` § *The order between issues* — before concluding the issue is
   free. A blocker that has since closed stops nothing and is why the query filters on `open` rather
   than on the list being empty.
9. Place the issue. The issue type, the labels, and the milestone need write access to the
   repository, so they belong to the owner's checkout; the board fields need write access to the
   board, which the board probe established. It carries one issue type — `gh issue create --type`, or
   `gh issue edit --type` on an issue that already exists — a `backend` or `frontend` label where the work
   lands in one of the two stacks and neither where it lands in neither, an `Area` and a `Queue`
   value on the board, a milestone when the milestone rule assigns one, and the `Size` placeholder
   that page's one rule produces — a placeholder rather than an estimate, because the value the board
   keeps is the one `$finish-change` measures off the diff. Decide each from the rules on that page rather than asking. `Queue: Next` is never
   one of them: the owner chooses it, and `$finish-change` writes it once the pull request exists, so
   a new issue takes `Later`, `Needs decision`, or `Parked`, and a parent takes one of those three by
   the same rules as any other issue. What marks a parent instead is the `parent` label and the `[P] `
   prefix every parent's title begins with, whatever `Queue` value it holds.
   Verify the values landed, because the built-in workflows set `Status` and nothing else, an
   unplaced issue disappears from the views the owner reads, and GitHub drops a type it will not let
   this credential set without reporting an error.

   An issue this step opened also carries its order. Write one `blocked by` relation for each issue
   its `## Dependencies` section names that is still open, and none for one already closed, with the
   call `docs/operations/issue-tracking.md` § *The order between issues* gives. Where the issue is a
   parent, its `## Delivery order` table is what those relations are read from, and that page
   requires the table on every parent — so a parent is not placed until it has one.

   In the fork role, open the issue and stop at what the probe allows. Without board write that is
   the issue alone: an arrival carries no type, no label, no milestone, no board fields, and no relation by
   design, and the maintainer's triage pass supplies them. With board write it is the issue plus its
   `Area`, `Queue`, and `Size`, and the type, the labels, the milestone, and the relations still wait for
   triage — a relation is write access to this repository exactly as a label is. Step 8 is
   unaffected either way, the dependencies of a public issue being public, so the refusal it carries
   holds in both roles. Say which of the two happened in the brief, so nobody reads the absence as a
   step that failed.
10. Claim the issue, in the owner's checkout, once the brief below will read `safe`:

    ```bash
    claim_label=agent:claude
    if [[ -n "${CODEX_THREAD_ID:-}" ]]; then
      claim_label=agent:codex
    fi
    gh issue edit <number> --repo fathomise/MailFathom --add-label "$claim_label"
    ```

    This is the step that says work has begun, so it belongs here rather than at step 8: an issue
    is marked with the runtime that took it (`agent:claude` or `agent:codex`). An issue
    identified while the workspace turns out to be `blocked` was read and not taken. Nothing reads
    the label, so re-applying one already present is a harmless no-op and nothing has to look first,
    and it is never removed — `docs/operations/issue-tracking.md` § *Labels* holds what it claims and
    why the claim is the weaker of the two available. In the fork role this step does not exist, for
    the reason every label step does not: it is write access to this repository.
11. Say whether the change looks large enough to split, in the `Scale` line of the brief. This is the
    board's `XL` warning, and it is raised here because this is the last point before the first edit
    at which anything has read the issue against the repository: the surfaces it reaches, the tests
    each one obliges, the pages whose `describes:` marker covers them, and whether a migration or a
    second stack is in it. `docs/operations/issue-tracking.md` § *Board fields* holds why it is not
    the `Size` field's warning — read off an issue body it finds a third of the changes that land
    there and is wrong about half of the ones it names, which is why it needs a reading rather than a
    rule.

    It is a report and never a refusal. Name what makes the change large and where the seam between
    two issues would fall, then carry on: whether to split is the owner's call, and a warning nobody
    acts on costs a sentence, while an unsplit `XL` is discovered when the pull request is already
    open.
12. For dependency, CLI, protocol, service, or external API changes, consult current official
    documentation and flag licensing review.

Return:

```text
Role: <owner's checkout or fork, what resolved it — the base branch and, for origin/main, the push probe — and what the board probe returned>
Workspace: <safe or blocked, branch, base branch>
Scope: <what governs the task, or that nothing does>
Protected paths: <none reached, or which — and whether the pull request waits for a maintainer's approval of its head, which it does unless its author is a maintainer>
Issue: <number and title, or created with reason>
Baseline: <the main commit the body was written against, what landed since that touches it and what that changed, or none recorded>
Blocked by: <the open blockers refusing the work, or none — and any relation written>
Placement: <issue type, stack label or neither, Area, Queue, Size placeholder, milestone or none — or what the board probe left to triage>
Scale: <no split warning, or what makes this look XL, which surfaces it reaches, and where a seam between two issues would fall>
Claim: <agent:claude or agent:codex applied, already present, or not applicable in the fork role>
Required context: <files read>
Assumptions or blockers: <none or explicit list>
Verification: <fast loop and final gate>
```

Do not begin edits while the brief says `blocked`.
