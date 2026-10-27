// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Spam.Model;

/// <summary>Counts how each consultation of the model stage ended, without describing the message it was about.</summary>
/// <remarks>
/// The job and the outcome are the whole of what is recorded. A verdict, a subcategory, or a probability is a judgement
/// about one person's message, and none of them reaches a counter.
/// </remarks>
public interface ISpamModelStageTelemetry
{
    /// <summary>Counts one consultation.</summary>
    /// <param name="job">Which of the stage's two jobs the model was asked to do.</param>
    /// <param name="outcome">How it ended.</param>
    void RecordConsultation(SpamModelJob job, SpamModelOutcome outcome);
}
