// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.DecisionModels;
using MailFathom.Application.Decisions;
using MailFathom.Application.Retrieval.AskMail;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.Decisions;

/// <summary>Makes one call to a decision model, and reads every way it can fail into an outcome rather than an exception.</summary>
internal static class DecisionConsultation
{
    /// <summary>Makes the one call, holding one of the replica's call slots for exactly as long as it runs.</summary>
    /// <param name="client">The client composed for the function that asks.</param>
    /// <param name="input">The input the questions read.</param>
    /// <param name="questions">The questions.</param>
    /// <param name="callSlots">The slots bounding how many calls this function runs at once on this replica.</param>
    /// <param name="cancellationToken">Withdraws the call.</param>
    /// <returns>The response where there is one, how the call ended, and the alias of the model a failure names.</returns>
    /// <remarks>
    /// A request too large for the declared model is refused by the adapter before it is sent, as an argument it names
    /// <c>input</c>; that refusal is permanent for the message, so it is an outcome rather than a failure to retry. A
    /// cancellation stays outside every catch, being the caller withdrawing the work rather than a provider failing.
    /// </remarks>
    internal static async Task<(DecisionResponse? Response, DecisionModelOutcome Outcome, string? Alias)> AskAsync(
        IDecisionClient client,
        string input,
        IReadOnlyList<DecisionQuestion> questions,
        SemaphoreSlim callSlots,
        CancellationToken cancellationToken)
    {
        await callSlots.WaitAsync(cancellationToken);

        try
        {
            var response = await client.DecideAsync(
                [new ChatMessage(ChatRole.User, input)],
                questions,
                options: null,
                cancellationToken);

            return (response, DecisionModelOutcome.Answered, null);
        }
        catch (DecisionFailedException failure)
        {
            return (
                null,
                failure.Failure is DecisionFailure.AnswerUnreadable
                    ? DecisionModelOutcome.AnswerUnreadable
                    : DecisionModelOutcome.ProviderUnavailable,
                failure.ModelAlias);
        }
        catch (MailAnsweringBudgetExhaustedException)
        {
            return (null, DecisionModelOutcome.BudgetExhausted, null);
        }
        catch (ArgumentException refusal) when (refusal.ParamName == "input")
        {
            return (null, DecisionModelOutcome.InputTooLarge, null);
        }
        finally
        {
            callSlots.Release();
        }
    }
}
