## Context

Part of #{{PARENT}}. The scanner stage of [spam classification](https://github.com/fathomise/MailFathom/blob/main/docs/features/spam-classification.md#the-scanner-stage) goes, entirely. Scoring mail with a rule corpus is the receiving mail server's job, and a SpamAssassin sidecar makes every operator run a second mail filter beside it. The deterministic stage, which reads `Authentication-Results`, `ARC-Authentication-Results`, the provider's `X-Spam-*` headers, and the junk-folder placement, is not touched.

What goes, as of the baseline:

- **Application**: the `ISpamScanner` and `ISpamScannerProbe` ports, `SpamScanResult`, `SpamScanOutcome`, `SpamScannerUnavailableException`, the scanner branch of `EmailSpamClassifier` (`JudgedBy`, the scanner half of `Decide`, `ScannerSignals`), and `UsesScanner` / `ScannerThreshold` on `SpamClassificationSettings`.
- **Domain**: `SpamClassificationStage.Scanner`, `SpamSignalKind.ScannerRule`, the scanner field of the `SpamClassificationProfile` digest, and the scanner-only uses of `CorpusRevision`. `SpamAssessment` stays only if the deterministic stage still produces one from a provider header carrying its own threshold, which it does today.
- **Infrastructure**: all six files under `Infrastructure/Spam` and their registration.
- **Host**: `SpamScannerOptions`, `SpamScannerStartupGate` and its place in `HostStartupGate`, the `SpamClassification:Scanner:*`, `SpamClassification:UseScanner`, and `SpamClassification:ScannerThreshold` keys, and `UseScanner` / `ScannerThreshold` on the account record.
- **Error code** `SpamScannerUnavailable` (`81003`), removed the way ADR 0003 says a code leaves the register.
- **AppHost**: the SpamAssassin container in the orchestration contract.
- **Deployment**: the Helm `spamScanning` block, `spam-scanner-deployment.yaml`, `spam-scanner-service.yaml`, the helpers and schema entries, the golden manifests, the Compose `spam-scanning` profile and its `.env.example` lines, and `deploy/quadlet/mailfathom-spamassassin.container` plus its reference in `mailfathom.container`.
- **Register and tooling**: the image's row in `THIRD_PARTY_LICENSES.md` and its pin family in `$update-dependencies`.
- **Tests**: every SpamAssassin, spamd, scanner-options, scanner-gate, and orchestrated-scanner test, and the scanner cases in the classifier, settings, profile, and policy tests.
- **Docs**: the scanner sections of `spam-classification.md`, and the scanner passages in `configuration-ai.md`, `configuration-mail.md`, the three deployment pages, `health-endpoints.md`, `admin-endpoint.md`, `arrival-pipeline.md`, `outbound-resilience.md`, and `local-development.md`.

`SpamClassification:ClassificationWait` and the derived-work gate stay. The model stage in #{{MODEL}} is also a verdict that takes time, and the gate's wait is what keeps a slow verdict from turning into a dead index.

## User stories

**As the operator of a deployment** I want no scanner to deploy, configure, or keep updated, **so that** spam filtering stays my mail server's job and MailFathom only reads what that server decided.

**As a maintainer** I want the scanner's port, adapter, and assets gone rather than left switched off, **so that** the classification code has one stage to reason about until the model stage arrives.

## Acceptance

- `git grep -i -E "spamassassin|spamd|spam-scanner|spamScanning|ISpamScanner|UseScanner|ScannerThreshold"` finds nothing outside `CHANGELOG.md`.
- The deterministic stage reaches the same verdict, with the same signals, on every case its existing tests cover.
- The profile digest no longer carries a scanner field, and the profile tests pin the new digests.
- Existing rows the scanner decided (stage `Scanner`, signals of kind `ScannerRule`) are handled by an additive migration. The pull request states which treatment it takes and what that does to mail only the scanner called spam: that mail loses its verdict, and the derived-work gate then admits it to chunking, embedding, and enrichment unless the deterministic stage or #{{MODEL}} calls it junk again.
- The host starts with no scanner gate. `/started` no longer names a spam daemon.
- `scripts/render-helm-manifests.sh` passes against updated golden manifests, and no chart, Compose, or Quadlet asset renders a scanner.
- The admin API and `mfctl spam` report no scanner stage, and their contract tests are updated.
- The issue and the pull request record each break with the operator's action:
  - **configuration**: the removed deployment keys, and an account record still naming `UseScanner` or `ScannerThreshold`, which binds nothing and has to be edited;
  - **deployment**: the removed Helm values, Compose profile, and Quadlet unit, and the sidecar to delete;
  - **database**: what the migration did to scanner-decided rows;
  - **error codes**: `81003` retired.
- `THIRD_PARTY_LICENSES.md` no longer lists the image, and `$check-docs-licenses` passes.

## Out of scope

- Any change to the deterministic stage's reading.
- The spam subcategory and the model stage: #{{RECORD}} and #{{MODEL}}.

## Dependencies

None. #{{RECORD}} waits on this, because both reshape `SpamClassificationStage`, the profile digest, and `EmailSpamClassifier.Decide`.

## Baseline

1d26b66df368199d215814baacb461e90f902748
