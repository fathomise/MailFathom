// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.Emails.Provenance;
using MailFathom.Application.Spam.Model;
using MailFathom.TestSupport;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Builds the model stage a classification is composed through.</summary>
internal static class SpamModelStages
{
    /// <summary>Builds a stage over a judge, a guard scanning nothing, and the given content and provenance readers.</summary>
    /// <param name="judge">What the model answers, defaulting to a judge nobody expects to be asked.</param>
    /// <param name="telemetry">Where consultations are counted, defaulting to a recorder nobody reads.</param>
    /// <param name="renderer">What renders the message, defaulting to a short plain-text body.</param>
    /// <param name="provenance">What the deployment knows of the sender, defaulting to a known correspondent.</param>
    /// <returns>The stage.</returns>
    internal static SpamModelStage Over(
        ISpamModelJudge? judge = null,
        ISpamModelStageTelemetry? telemetry = null,
        IEmailContentRenderer? renderer = null,
        IEmailProvenanceReader? provenance = null) =>
        new(
            judge ?? ScriptedSpamModelJudge.Unused(),
            new SpamModelEvidenceReader(
                renderer ?? new BoundedBodyEmailContentRenderer("A short synthetic body."),
                provenance ?? StubEmailProvenanceReader.AllKnownCorrespondents,
                SpamModelStageBounds.Default),
            SensitiveContentEgressGuards.Inactive(),
            telemetry ?? new RecordingSpamModelStageTelemetry());
}
