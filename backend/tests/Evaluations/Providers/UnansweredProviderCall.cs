// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ClientModel;
using System.Globalization;

namespace MailFathom.Evaluations.Providers;

/// <summary>Finds and names a provider call that went unanswered, in the words a case's shortfall lines carry.</summary>
/// <remarks>
/// A failure is named by its type and the HTTP status it carries, such as <c>ClientResultException 429</c>, and never by
/// its message: a provider writes the model and the address into it, and a judge's model must not reach a failure line.
/// </remarks>
internal static class UnansweredProviderCall
{
    /// <summary>Names a failure by its type and, where it carries one, the HTTP status the provider answered with.</summary>
    /// <param name="failure">The failure the call ended in.</param>
    /// <returns>The name, such as <c>ClientResultException 429</c> or <c>TaskCanceledException</c>.</returns>
    public static string Describe(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return StatusOf(failure) is { } status
            ? string.Create(CultureInfo.InvariantCulture, $"{failure.GetType().Name} {status}")
            : failure.GetType().Name;
    }

    /// <summary>Finds, in a failure or anything it wraps, a provider call that failed only because the provider was busy or unreachable.</summary>
    /// <param name="failure">The failure a case ended in.</param>
    /// <param name="cancellationToken">The run's own token, whose cancellation is never a provider's failure.</param>
    /// <returns>The provider's failure, or <see langword="null" /> where the case failed for another reason.</returns>
    public static Exception? FindIn(Exception failure, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(failure);

        for (Exception? wrapped = failure; wrapped is not null; wrapped = wrapped.InnerException)
        {
            if (TransientProviderRetryChatClient.ClassifyTransient(wrapped, cancellationToken) is not null)
            {
                return wrapped;
            }
        }

        return null;
    }

    private static int? StatusOf(Exception failure) => failure switch
    {
        ClientResultException { Status: > 0 } refusal => refusal.Status,
        HttpRequestException { StatusCode: { } status } => (int)status,
        _ => null,
    };
}
