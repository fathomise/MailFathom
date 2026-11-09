// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Decisions;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.EmailCategorization;

/// <summary>What a categorization judgement reports about itself, carrying no part of the message and no part of the answer.</summary>
internal static partial class EmailCategorizationEvents
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "Categorization was answered.")]
    internal static partial void LogAnswered(ILogger logger);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Categorization was not answered because {Outcome} at {ModelAlias}, so the message has no category.")]
    internal static partial void LogUnanswered(
        ILogger logger,
        DecisionModelOutcome outcome,
        string modelAlias);
}
