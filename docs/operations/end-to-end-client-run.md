# The end-to-end client run

<!-- describes: scripts/run-end-to-end-client.sh, scripts/write-end-to-end-client-report.sh, .github/workflows/end-to-end-client.yml, frontend/playwright.end-to-end.config.ts, frontend/tests/end-to-end/**, backend/src/AppHost/Program.cs, backend/src/AppHost/OrchestrationContract.cs -->

One pipeline, run every night `main` moved and whenever somebody asks for it, that stands a throwaway MailFathom up the
way an operator stands one up, drives the real client against it in a real browser, and leaves a report of what it
found.

It exists because nothing else does that. The client's browser suite answers every request from the page's own routing,
which is what keeps it fast and hermetic and what makes it silent on whether the client and the service agree; the
service's integration suite starts a composed host and proves the pipeline behind the HTTP boundary, and stops there.
Between them sits the question of whether a person can sign in, read the mail the deployment synchronized, and work the
calendar, the task list and the address book it holds for them — and that question is what this run answers.

## It runs every night `main` moved, and gates nothing

Nothing in this repository waits on it. It is not a pull-request check, it is not a required status, and neither
[the nightly](agent-workflow.md) nor [a release](release-procedure.md) calls it. A change that would break it still
merges green, and the run that notices is the one the following night.

That is the decision rather than an omission, and it follows from what the run costs. Two container images are pulled,
the client bundle and the service are both built, the schema artifact is generated through `aspire publish`, a mailbox
is filled one message at a time over SMTP, and a browser then drives the result — roughly fifteen to twenty minutes of
runner time. It calls no AI provider, so it spends no provider credit. What it answers is *does the whole thing still
hold together*, which is not worth asking on every push — and is worth asking every day something merged, because the
client is where the defects are and nobody tries it by hand.

So `End-to-end client` runs on a schedule at 03:00 UTC, and its first job decides whether there is anything to prove.
It asks the workflow's own history whether a completed scheduled run has already run the commit `main` is at, whatever
that run concluded, and when one has, the night ends there and its summary names that run. A commit that failed last
night is therefore not run again tonight: its report already says why, and a run on the same commit would say it a
second time. A quiet `main` costs one job of a few seconds. A dispatch is exempt and always runs, with or without a
`ref`, because asking for a run is a reason to have one.

## Running it

```bash
bash scripts/run-end-to-end-client.sh                 # the whole run
bash scripts/run-end-to-end-client.sh --grep thread   # trailing arguments are forwarded to Playwright
```

`End-to-end client` in `.github/workflows/` is the same script on a runner, on its schedule or by dispatch, taking a
`ref` to run against. It uploads [the report](#the-report), the browser's traces and screenshots, and the logs each
step wrote.

The machine needs what the two stacks already need — the .NET SDK, Node and pnpm, and a container runtime — plus two
things a verification gate does not ask for: the Aspire CLI, at the version `backend/Directory.Packages.props` pins the
hosting packages to, because the schema artifact comes from `aspire publish`; and a Chromium for Playwright, from
`pnpm --dir frontend exec playwright install chromium`. `MAILFATHOM_CONTAINER_RUNTIME` selects a runtime other than
`docker`.

## What it stands up, in order

Each step is named, and a failure reports the step it happened in rather than only a non-zero exit — because a corpus
that never reached the mailbox and a client that cannot draw a list are two different defects.

| Step | What it produces |
| --- | --- |
| The client bundle | `pnpm install --frozen-lockfile` and `pnpm build`, the two commands the published image's client stage runs |
| The schema artifact | `scripts/build-schema-artifact.sh`, the same artifact a release attaches and an operator applies |
| The database, the mail server, and the fault proxy | The app model, on the topology `EndToEndClient=true FaultProxy=true` selects: PostgreSQL, GreenMail, and the Toxiproxy instance [Injecting network faults](fault-injection.md#in-the-end-to-end-client-run) describes in front of GreenMail, under the ephemeral run prefix, and no MailFathom |
| The schema | The artifact applied with `psql` inside the database container, the route [the schema page](database-schema.md) gives an operator whose database is not reachable from where they stand |
| MailFathom | `dotnet publish` of `Host`, the bundle copied to `wwwroot/` beside it as the image does, started with the client endpoint on and serving the page |
| The mailbox | `POST /api/admin/users` recording the user this run serves — the database it created holds nobody — then `POST /api/admin/mail-accounts` creating the mail account — reading the mailbox through the fault proxy's IMAP port rather than GreenMail's own — and assigning it to that user, because no configuration source declares a mailbox — the mailbox is served from that write, without a restart. The record states the account's language and carries an `env:` reference to the mailbox password rather than the password, because MailFathom refuses a secret's material into a persisted record whatever wrote it |
| The role | `POST /api/admin/role-assignments` giving that user the seeded `User` role at their own scope, found by its name in `GET /api/admin/roles`, because a recorded user holds no permission until a role is assigned and the credential below takes the user's own |
| The credential | `POST /api/admin/users/{id}/credentials` on [the administrative endpoint](admin-endpoint.md), so the password this run signs in with was made the way an operator makes one |
| The mail | `SyntheticMail replay` of `backend/tools/SyntheticMail/corpora/office-en.zip` into the mailbox over SMTP |
| The synchronization | Polling [the folders route](client-endpoint.md) until the account's run reports no failed folder and at least one synchronized one |
| The client | The end-to-end Playwright suite, against the origin the deployment serves — whose global setup writes the sample calendar, task list and address book over the client API before the first browser opens |
| The service log | Every error-level entry the service logged, held against the windows the specs declared; one outside all of them fails the run |

Everything created is removed when the run ends, whether it passed, failed, or was interrupted: the containers and their
volumes are named under this run's own identifier and deleted by name, so a concurrent integration suite keeps its own.

**The app model owns the servers and this script owns nothing that runs in a container.** The pipeline declares no
image, no tag, and no container configuration of its own, for the reason [the integration suite](local-development.md)
gives about the same thing: what the run exercises has to be the PostgreSQL and the mail server every other run of this
repository exercises, at the same pins. `EndToEndClient=true` is an argument rather than a variable, so an ambient value
cannot divert an ordinary `aspire run` onto it.

**The client endpoint's per-user budget is raised for this run, and the limiter stays on.** The default allows one user
a burst of 120 requests restored at 120 a minute, queueing none beyond it — a bound written for a person reading their
mail. What drives this endpoint is a browser suite signing in once per case and drawing five spaces as fast as it can,
so the suite spends that budget inside its first minute and every request after it is refused; the client reports a
refusal as *the deployment did not answer*, which reads as a broken screen and is not one. The run therefore states a
budget no suite can exhaust rather than switching the limiter off, so a request still takes the path it takes in a
deployment.

**The mail connection's budgets are shortened for this run, for the outage spec.** At the shipped values an account
runs every five minutes, waits up to half an hour between runs while they keep failing, makes three attempts at a session
it could not establish — the first and two retries — over a growing backoff, and keeps the circuit of one it gave up on open for half a minute — so a
mailbox taken away would be noticed minutes late and its return later still. The run sets
`MailSynchronization:Interval` and `MailSynchronization:MaxFailureBackoff` to ten seconds, and for both
`Resilience:MailboxSessionEstablishment` and `Resilience:MailboxDataRetrieval` two attempts, a backoff of one to two
seconds, and a five-second break. Each is a value an operator could write, and each moves when an outage is noticed and
when its end is, never whether it is: the fault proxy passes every byte untouched until a spec injects a fault, so every
other spec meets the mailbox exactly as before, only read more often.

**Every wait is on a condition rather than on a duration.** The database is waited for with `pg_isready` over TCP —
over the socket it would report ready against the temporary server the image runs while it initializes its data
directory, and that server is stopped, and its socket removed, under the command that follows — the mail
server on its own readiness route, MailFathom on its `started` probe, and the mailbox on what the account's
synchronization run reports. A sleep long enough for a slow machine is time every fast run pays, and one short enough
for a fast machine is a flake — and the last of those four is the one that matters most, because a first synchronization
of a filled mailbox takes as long as it takes.

## What the specs assert

`frontend/tests/end-to-end/` holds them and `frontend/playwright.end-to-end.config.ts` runs them. They cover the path a
person takes through each of the client's five built spaces: signing in, the mail list, opening a message, the
conversation it belongs to, a search over the mailbox, and signing out; the calendar in each of its four views and an
event written, amended and deleted; the task list under its three headings, the day beside it, and a task written,
marked done and deleted; the address book, somebody opened out of it, and a contact written and deleted; and Discover as
far as this run can reach it, which the paragraph below says is the screen rather than an answer. Everything is
asserted by role and by the words a person reads, as the client's unit and browser suites are. Its fourth suite is the
exception that proves nothing about this one: the desktop suite reaches its WebView over the WebDriver protocol, which
has no locator for a role and a name at all.

**Only Mail arrives by itself, so the other three are seeded.** Mail reaches the deployment because it was delivered to
a mail server and synchronized out of it; a calendar, a task list and an address book are written by the person who
owns them, so a deployment nobody has used holds none of them. `frontend/tests/end-to-end/seed.ts` is this repository's
sample set — three people, five events and five tasks, every name invented and every host a reserved one — and the
suite's global setup writes it over the client API with the credential the run provisioned, before the first browser
opens. Every day in it is an offset from the day the run happens on, stated in UTC against the zone the configuration
pins, so *Today*, *This week* and *Later* are each reachable whichever weekday a run lands on. Each of the three spaces
also writes one record of its own through its screen and removes it again, which is where the client's own write path
is held to account and what keeps the files runnable in any order.

**One spec takes the mailbox away.** `outage.spec.ts` opens Mail on a deployment whose freshness line says every
account is current, starts `imap-down` through `scripts/inject-fault.sh` on the proxy the run asked for, and waits for
the line to say the account is not current — which `design/` draws once an account's mail server has been unreachable
for a minute — and to open onto *Mail server unreachable*. It then ends the fault with the same script's `stop` and
waits for every account to be current again. It is the only spec that injects a fault, and it ends the fault whatever
happened in between, so the file after it meets the mailbox it met. It is also the only spec that declares the service
will log errors while it runs, which [the report](#the-report) holds every other error against.

**Discover is held to the screen it opens on, and not to an answer.** The client offers it, as it does Tasks and
Calendar, only while developer mode is on, so a spec reaching one of the three switches that on from the version line
in *Settings* first. Pressing one of the questions it offers starts a real run — but composing an answer needs a chat provider and this run configures
none, so a spec that waited for a block would be waiting on a model nothing here provides. What the spec holds is that
the space reaches its screen, that the questions it offers are there to press, and that its own question field stands on
it.

**They route nothing.** The bundle the deployment serves reaches the surface that deployment serves, over one origin,
and every answer comes from mail that arrived at a mail server and was synchronized out of it. A `page.route` here would
make this suite a slower copy of the pull-request one.

**They assert what the client says rather than what the corpus says.** The mail is generated, so no subject, sender, or
sentence in it is a value to write down; a spec restating one would be asserting against the archive rather than against
the service.

**It is a configuration of its own, and the pull-request suite is unchanged.** `frontend/playwright.config.ts` ignores
this directory so it cannot pick up a spec that needs a deployment, and that is the whole of the change to it. Folding
the two together would put a hermetic suite one mistake away from reaching a service.

## The report

Whatever the run concluded and wherever it stopped, it ends by writing what it found — `report.json` for a session
triaging the night and `report.md` for a person — under `artifacts/end-to-end-client-report/`, through
`scripts/write-end-to-end-client-report.sh`. The workflow uploads the two as the `end-to-end-client-report` artifact
and writes the Markdown to the job summary; a run that reached the pipeline and left no report fails that upload,
because a missing report is a defect in the script rather than a quiet night. The Markdown is rendered from the JSON,
so the two never say different things.

| Field of `report.json` | What it holds |
| --- | --- |
| `commit`, `startedAt`, `durationSeconds` | The commit the run checked out, when it started, and how long it took |
| `outcome`, `exitCode`, `failedStep` | `passed` or `failed`, and for a failure the step of the table above the script was in — the same name its own log prints |
| `specsRan` | Whether Playwright reached a report at all; `false` for a run that stopped before the browser, so an empty `specs` is never read as nothing having failed |
| `specs` | Every spec with its file, line, outcome, and duration; a failed one adds its errors — message, file, and line — and the paths of its trace and screenshot inside the `end-to-end-client-browser` artifact |
| `suiteErrors` | What failed outside any spec, the global setup's seeding among it |
| `unexpectedServiceErrors` | Every error-level service entry outside a declared window, with its time, category, event, message, and the first line of its exception |
| `expectedServiceErrorCount` | How many error-level entries fell inside one |

Every spec's outcome is read from Playwright's own JSON reporter, which the configuration writes beside the logs as
`artifacts/end-to-end-client/playwright.json`, rather than from what the `list` reporter printed.

**An error-level service entry nobody declared fails the run.** A client that draws the right screen over a service
that logged a failure is a defect no screen assertion catches, so once the specs pass the run reads the service log and
fails on any entry at `Error` or `Critical` that no spec declared it would cause — and the report lists every such
entry whatever the specs concluded. A spec declares it with an `expected-service-errors` annotation, whose description
says why, and the window is that spec's own span: `outage.spec.ts` carries one, because a synchronization run that
cannot reach the mail server fails and says so. An error logged before the fault or after the recovery is outside it
and still fails the run. To make the instants comparable the run starts MailFathom with the console's `json`
formatter, stamped in UTC to the millisecond — a formatter an operator chooses wherever something parses the lines.
The few lines the host writes before its configuration exists keep the plain format and carry no time, so an error
among them is never inside a window.

Everything in the report is fabricated mail and a credential that lived for one run, which is the reason the next
section gives for keeping the traces as well.

## What it keeps, and why that is allowed here

The run keeps its traces and screenshots, and the workflow uploads them. That is the opposite of what the pull-request
browser suite does, and the difference is the mail rather than the storage: every message here is a corpus this
repository generated, delivered into a container that is destroyed with the run, and the credential is a password that
exists for the length of one run. A capture shows nobody's mailbox and a trace carries nobody's credential.

The other suite's rule is unchanged and is the general one: the moment a capture could show real mail it is personal
data, whatever produced it, and it stays on the machine that took it.
