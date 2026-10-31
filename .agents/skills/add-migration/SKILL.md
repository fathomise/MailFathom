---
name: add-migration
description: Use when an EF Core model change needs a migration, or when a migration has to be reviewed as SQL before it is committed.
license: AGPL-3.0-only
metadata:
  author: Krzysztof Kasprowicz
  repository: https://github.com/fathomise/MailFathom
---

# Add Migration

Appends one EF Core migration for the current model and reviews it as SQL. Applying it is the pipeline's: nothing here
starts Docker, the orchestration, or a database.

**Every migration in this repository is permanent.** Nothing regenerates, renames, reorders, or deletes one, and no
command here does either. A migration identifier is written into `__EFMigrationsHistory` on every database that applies
it, so a regenerated baseline leaves that database recording an identifier no later migration can reach — it can then
only be recreated, which means destroying whatever mail it held. The workflow is additive for every database that
exists, which includes the persistent local one, not only the ones a release will create.

Clearing local data is a different operation with a different command, and it touches no file:

```bash
aspire resource mailfathom-migrations ef-database-reset --apphost backend/src/AppHost/AppHost.csproj --non-interactive
```

## Preconditions

- The model change is saved. Docker is not required at any step.
- A deployment that configures a non-default `Persistence:TextSearchConfiguration` exports it first, because the value
  is compiled into the search vector's stored generated column and a migration is generated for exactly one
  configuration:

  ```bash
  export Persistence__TextSearchConfiguration=english
  ```

  The host compares the configured value against the live schema at startup and refuses to run on a mismatch, so
  skipping this produces a startup failure rather than a silently wrong index.

## Workflow

### 1. Generate

```bash
scripts/add-migration.sh <MigrationName>
```

The name describes what the migration does to the schema, in PascalCase — `AddEmailRetentionPolicy`, not `Update2`. It
becomes part of a permanent identity, so it is chosen once. The script refuses `Initial`, refuses a name already in
use, builds the startup project so the tool reads the current model, generates the migration, and writes the copyright
and license header into it.

Nothing here starts the orchestration or touches a database. Generating a migration compares the model against the
committed model snapshot and writes files, which has the same answer whether or not PostgreSQL is running, so the
script points the design-time factory at a port nothing listens on — if generation ever starts requiring a database, it
fails rather than silently reaching one.

Three files change under `backend/src/Infrastructure/Persistence/Migrations/`: the new migration, its designer file, and the
model snapshot. The snapshot is the only existing file the workflow rewrites, and EF owns it — never hand-edit it. The
`.editorconfig` beside them is hand-written and relaxes the two diagnostics EF's generator trips.

**An empty migration means the model did not change.** EF generates one with empty `Up` and `Down` methods rather than
failing, so delete it and find out why the change did not reach the model — a configuration not applied, a property on
a type the context does not map, a build that did not run. An intentionally SQL-only migration is the one exception: it
is written deliberately, says in a comment why it carries no model change, and is reviewed as SQL like any other.

### 2. Review it as SQL, not only as C#

This is the step the whole workflow exists for, and it is the one no script performs: the generated C# hides what
PostgreSQL was actually asked to do.

```bash
scripts/script-migration.sh
```

With no arguments this scripts the newest migration from the one before it, which is the range a review has to cover.
`scripts/script-migration.sh 0` produces the whole schema from an empty database instead.

Read it for: destructive operations, and in particular a rename EF inferred as a drop followed by an add, which loses
every value in the column; the PostgreSQL types, nullability, and defaults each column ends up with; the indexes and
constraints the documented query shapes need; `ON DELETE` behavior on each foreign key; the generated `tsvector` column
and its text search configuration; and the lock and duration a statement implies on a table that already holds mail.

Fix the model and regenerate rather than hand-editing the generated migration — but once a migration has merged, it is
fixed forward by another migration instead.

### 3. Commit it with the model change

Update `docs/architecture/stored-email-schema.md` when the change is visible to a reader of the schema, and commit the
migration together with the model change that caused it. A migration committed on its own cannot be reviewed, because
the reason for it is in the other commit.

### 4. Let the pipeline apply it

Nothing here applies the migration to a database: the pull request does. Two `Backend` jobs answer for it.

- `Pending model changes` runs `dotnet ef migrations has-pending-model-changes` on any pull request touching
  `backend/src/`, so a model change that reaches `main` without its migration fails before merge rather than at a
  host's startup.
- `Apply migrations` runs on any pull request touching a migration, the app model, or the tooling that generates the
  schema artifact. It builds the artifact an operator applies, applies it to an empty PostgreSQL twice and over the
  newest release's schema and the rows that release seeded, and fails when the upgraded schema differs from the fresh
  one. The schema PostgreSQL ended up holding is the run's `applied-schema` artifact — read it beside the SQL from
  step 2.

A local orchestration still applies pending migrations when it starts, which is how a developer's own database keeps
up; that is a convenience of `aspire start`, not a step this workflow asks for.

## When something is stuck

- **`Changes have been made to the model since the last migration` in CI, but nothing looks changed.** The model
  snapshot is stale rather than the model. Configuration that produces no SQL — a constraint name, an index filter —
  still moves the snapshot, so the fix is a migration whose `Up` is empty by design or a regenerated snapshot as part
  of the next real migration.

- **`Apply migrations` fails on the upgrade but not on the empty database.** The migration assumes a table holds
  nothing, or holds only what a fresh installation seeds: a `NOT NULL` column with no default, a constraint the
  release's seeded rows violate. Fix the model and regenerate, as step 2 says.

- **`Apply migrations` reports that the upgraded schema differs.** The diff in its log is what an existing installation
  would carry and a fresh one would not. A migration whose effect depends on what the database already holds is the
  usual cause; make it produce the same schema either way.

- **`error IDE0073` on a generated migration.** The formatter did not reach it. `--include` has to name the files
  rather than their directory, and the scope has to be the solution: these files belong to `Infrastructure`, and an
  `--include` filter evaluated against another project's workspace silently matches nothing and still exits `0`.
