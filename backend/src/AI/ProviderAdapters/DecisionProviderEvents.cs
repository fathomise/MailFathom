// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.DecisionModels;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.ProviderAdapters;

/// <summary>Records what a decision call ended with, and nothing about what it was carrying.</summary>
/// <remarks>
/// Every parameter is a name the operator chose or a classification of this system's own. No input, no question, no
/// probability, and no provider response body reaches these events, for the reason <see cref="ChatProviderEvents" />
/// gives: the input is somebody's mail, and a provider's error text quotes the request that produced it.
/// </remarks>
internal static partial class DecisionProviderEvents
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "Decision model {ModelAlias} produced no answers; the call ended with {DecisionFailure}.")]
    internal static partial void LogCallFailed(
        ILogger logger,
        string modelAlias,
        DecisionFailure decisionFailure);
}
