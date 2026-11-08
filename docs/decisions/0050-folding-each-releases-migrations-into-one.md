---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-11-08
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Fold the migrations a release cycle adds into one migration for that release, under the identifier of the last one it replaces, by concatenating them rather than regenerating them, and publish no release whose fold cannot be told apart from the migrations it replaced

<!-- describes: backend/src/Infrastructure/Persistence/Migrations/**, scripts/squash-migrations.sh, scripts/compare-migration-squash.sh, scripts/add-migration.sh, .agents/skills/prepare-release/SKILL.md, .agents/skills/add-migration/SKILL.md -->

## Context and Problem Statement

Every EF Core migration ships with a `.Designer.cs` file whose `BuildTargetModel` carries a full copy of the model as it stood at that migration. The copy grows with the model and every migration adds one, so `backend/src/Infrastructure/Persistence/Migrations` grew quadratically: on 2026-11-08 it held 162 migrations and about 597,000 lines, nearly all of them in Designer files. That was most of `Infrastructure`, and `Infrastructure` sits on the build's critical path — `Domain`, `Application`, `Infrastructure`, `Host`, the host suites — where its compile had become the largest single share of the `CI` build and of `dotnet format`.

[ADR 0004](0004-versioning-and-release-policy.md) made every migration permanent, and for a reason that still holds: a migration identifier is written into `__EFMigrationsHistory` on every database that applies it, so a regenerated baseline leaves that database recording an identifier no later migration can reach. The question is whether the folder can stop growing with the length of the project's history without breaking a single database a release or a nightly could have reached.

## Decision Drivers

- **No published artifact ever ships a migration that later changes.** A nightly image is installable, so a migration a nightly carried is one a database may have recorded.
- **An installation of the newest release upgrades exactly as it would have.** The squash has to be invisible to an operator on a supported path.
- **Hand-written SQL survives.** Many migrations carry `migrationBuilder.Sql(…)` — role seeding, data rewrites, backfills — that `dotnet ef migrations add` would not write again, and a migration applied over a release's data has to run the same steps in the same order.
- **Equivalence is proven, never assumed.** EF Core generates an operation against the migration's target model, so the same operation can generate different SQL inside a different migration.
- **The fold is mechanical.** A release is cut from a script, not from a hand-assembled migration somebody has to get right.

## Considered Options

- **R1: Fold each release cycle into one migration by concatenation, under the identifier of the last migration it replaces, proven equivalent before the release is published.** Nightly stays incremental; the release pull request folds the cycle.
- **R2: Keep every migration forever.** The status quo ADR 0004 recorded.
- **R3: Regenerate one baseline per release** with `dotnet ef migrations remove` and `add`.
- **R4: Keep every migration and drop the Designer files of the old ones**, moving `[Migration]` and `[DbContext]` onto the migration class and leaving it without a target model.
- **R5: Fold on every merge**, keeping one migration per cycle at all times.

## Decision Outcome

Chosen option: **R1**, because it is the only one that bounds the folder by the number of releases while keeping every identifier a published artifact could have recorded and every statement a database has to run.

### What a fold is

- **The release migration takes the identifier of the last migration it replaces** — the string in `[Migration("…")]` — and a class named for its release, `Release_0_9_0` in `20261105194555_Release_0_9_0.cs`. A database that recorded that identifier skips it; a database that recorded none of the cycle applies it whole; a fresh database applies every release migration in turn. The history rows of the replaced migrations stay in existing databases and do no harm: the host's startup gate asks `GetPendingMigrationsAsync`, which looks only for migrations the assembly defines and the history lacks.
- **Its Designer file is the last replaced migration's**, which already carries the model at the end of the cycle; only the class name in it changes. The model snapshot does not change at all.
- **Its Up is the replaced migrations' Up in the order they were added, and its Down is their Down in reverse.** Each replaced migration is kept as a private nested class of the release migration, with its own comments, constants, and helpers, and with Up and Down turned into static methods the release migration calls. That is concatenation of the operations without concatenating the source text, so two migrations that each declared a helper or a local of the same name do not collide, and the reasoning written beside a hand-written statement stays beside it.
- **`scripts/squash-migrations.sh` writes it**, and `$prepare-release` runs it as a step of the release pull request: `scripts/squash-migrations.sh <version> <previous release tag>` folds every migration added after the newest one the previous release carries.
- **The baseline is the one fold made outside a release.** Everything up to and including the last migration `v0.8.0` carried became `20261011174705_Release_0_8_0` in the pull request that made this decision, because `0.8.0` was already released and every database it could reach recorded that identifier. From `0.9.0` on each release adds exactly one migration, so the folder holds the baseline, one migration per release, and the nightly migrations added since the newest release.

### What proves a fold

EF Core passes `migration.TargetModel` to the SQL generator, and Npgsql looks columns up in it. Folded operations are generated against the model at the end of the cycle rather than their own, and that changes the output in two known ways: an `AlterColumn` that only changes nullability, of a column the final model no longer has in that shape, can emit a redundant `ALTER COLUMN … TYPE`; and a generated `tsvector` column whose source column the final model no longer has fails to generate at all. Each one is resolved in the release migration — an explicit type, an explicit computed expression — so the SQL stays what it was, rather than accepted as a changed result.

`scripts/compare-migration-squash.sh` is the proof, and `$prepare-release` publishes no release pull request unless it passes. It builds the schema artifact from before the fold and from the fold with `scripts/build-schema-artifact.sh`, applies them to a PostgreSQL running the image the app model declares, and requires five things identical:

1. a fresh install of each, as a full `pg_dump` of schema and seeded rows, leaving out `__EFMigrationsHistory`;
2. the fold's artifact applied a second time, which changes nothing;
3. an upgrade from the newest release — its own published artifact, then the fold — against the same upgrade done with the artifact from before the fold, and against a fresh install;
4. an upgrade from the last nightly before the fold: a database that recorded every individual migration finds nothing to apply;
5. the generated SQL itself, statement by statement, once the per-migration history guards and history rows are set aside.

The `CI` job `Apply migrations` keeps proving an upgrade from the newest release on every pull request that touches a migration. It does not also prove an upgrade from the last nightly, because only a fold can make that upgrade differ from the release one, and the release pull request that folds is the one place the comparison above already proves it.

### What a fold no longer supports

These are the operator's actions, and a release that folds carries them into `CHANGELOG.md`:

- **An installation older than the previous release** has recorded some of the baseline's or an earlier release's replaced identifiers but not the last one, so it would apply the release migration whole over a schema that already holds part of it and fail. Upgrade to each release in turn: from a release older than `0.8.0`, to `0.8.0` first.
- **A nightly from the middle of a cycle**, upgraded across the release that folds that cycle, records some of the cycle's identifiers but not the last one, and fails the same way. Move to the last nightly of the cycle first, or reinstall.
- **A persistent local development database** caught mid-cycle is the same case, and is reset with `ef-database-reset`.

### Consequences

- Good, because the folder, the `Infrastructure` compile, and `dotnet format` grow with the number of releases rather than with the square of the number of migrations; the baseline fold alone took the folder from 326 files and about 597,000 lines to 88 files and about 264,000.
- Good, because every supported upgrade runs exactly the statements it ran before, which the comparison proves on every release rather than once.
- Good, because nothing changes for a migration author between releases: `$add-migration` appends a migration exactly as before, and the fold is the release's.
- Neutral, because a database records a history row for every individual migration it applied before a fold and only the release identifier for a cycle it applied after one; nothing reads the history but the startup gate, and the gate is satisfied either way.
- Bad, because an upgrade that skips a release, and a nightly upgraded across a fold from mid-cycle, are no longer supported, where R2 supported both. Both are paths ADR 0004 already gave no promise for below `1.0.0`.
- Bad, because a release pull request now builds the schema artifact twice and runs PostgreSQL, which needs a container engine on the machine cutting the release.
- Bad, because a test that named a migration class has to name what its statements do instead, since a fold turns the class into a nested one. `RoleSeedingMigrationTests` is the worked example.

## Pros and Cons of the Options

### R2: Keep every migration forever

- Good, because every upgrade path stays supported.
- Bad, because the folder grows quadratically and sits on the build's critical path, which is the cost this decision exists to stop.

### R3: Regenerate one baseline per release

- Good, because it produces the smallest possible migration.
- Bad, because regeneration drops every hand-written statement, so a release migration applied over real data would skip the seeding, the rewrites, and the backfills the individual migrations ran.
- Bad, because a regenerated migration takes a new identifier unless one is forced, and with a forced one it still runs different statements than the database it upgrades expects.

### R4: Drop the Designer files of old migrations

- Good, because it removes nearly all the lines while keeping every migration and identifier.
- Bad, because a migration without a target model generates different SQL: the redundant `ALTER COLUMN … TYPE` and the `tsvector` failure above, in every migration rather than only across a fold.
- Bad, because the folder still grows by a file per migration, and the drift between a migration's operations and the model it no longer carries is proven by nothing.

### R5: Fold on every merge

- Good, because the folder would hold the fewest migrations at all times.
- Bad, because a nightly image would ship a migration that the next merge changes, which is the break ADR 0004 exists to prevent.

## More Information

Recorded on issue 3089. This amends [ADR 0004](0004-versioning-and-release-policy.md)'s clause that a schema change is a new migration and never a regenerated baseline: a schema change between releases is still a new migration, and a release folds the cycle's migrations into one under the last replaced identifier. Root `AGENTS.md`, `backend/src/Infrastructure/AGENTS.md`, `$add-migration`, `$prepare-release`, and `docs/operations/release-procedure.md` state the lifecycle.
