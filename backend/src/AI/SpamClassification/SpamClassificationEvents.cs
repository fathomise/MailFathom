// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Spam.Model;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.SpamClassification;

/// <summary>What a spam judgement reports about itself, carrying no part of the message and no part of the answer.</summary>
/// <remarks>
/// A verdict, a subcategory, and a probability are a judgement about one person's message, so none of them is named:
/// the job, how it ended, and the model's alias are the whole of what is safe to report.
/// </remarks>
internal static partial class SpamClassificationEvents
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "The spam model stage was answered for {Job}.")]
    internal static partial void LogAnswered(ILogger logger, SpamModelJob job);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "The spam model stage was not answered for {Job} because {Outcome} at {ModelAlias}, so the deterministic verdict stands.")]
    internal static partial void LogUnanswered(
        ILogger logger,
        SpamModelJob job,
        SpamModelOutcome outcome,
        string modelAlias);
}
