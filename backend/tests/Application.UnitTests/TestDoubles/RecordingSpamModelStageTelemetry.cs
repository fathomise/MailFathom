// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Spam.Model;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Keeps every consultation the model stage reported.</summary>
internal sealed class RecordingSpamModelStageTelemetry : ISpamModelStageTelemetry
{
    /// <summary>Gets every consultation, in the order it was reported.</summary>
    public List<(SpamModelJob Job, SpamModelOutcome Outcome)> Consultations { get; } = [];

    /// <inheritdoc />
    public void RecordConsultation(SpamModelJob job, SpamModelOutcome outcome) => this.Consultations.Add((job, outcome));
}
