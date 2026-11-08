// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;

namespace MailFathom.Evaluations.Providers;

/// <summary>How many provider calls the whole run has in flight at once, which the block's <c>ConcurrentProviderCalls</c> declares.</summary>
/// <remarks>
/// <para>
/// One limit for the process rather than one per client, because the account a provider rate-limits is one for the whole
/// run: every case, every model under test, every judge, and every repetition opens clients of its own, and with nothing
/// bounding them an Agent section sends about a thousand calls in under a minute, which the account refuses whichever
/// model they were for. A call waits for a slot only while it is sent, never while it waits to be retried, so a rate-limited
/// call does not hold one of the slots the others need.
/// </para>
/// <para>
/// It bounds the chat calls of the models under test and of the judges, and the decision calls, which are the calls a
/// section makes by the hundred. The embedding scenarios send few requests, each a batch, so they are left outside it.
/// </para>
/// </remarks>
internal static class ProviderCallLimit
{
    /// <summary>The limit a block declaring none runs under.</summary>
    /// <remarks>
    /// Sixteen: a quarter of the sixty-four cases the assembly runs at once, which keeps a section to a pace an account
    /// admits while a run of a thousand calls still ends in minutes.
    /// </remarks>
    public const int Default = 16;

    /// <summary>The highest limit a block may declare.</summary>
    public const int Most = 256;

    private static readonly Lazy<SemaphoreSlim> SharedLimit =
        new(static () => new SemaphoreSlim(EvaluationDeclaration.Read().ConcurrentProviderCalls));

    /// <summary>Gets the slots every provider client of the run sends through, sized from the run's declaration on first use.</summary>
    public static SemaphoreSlim Shared => SharedLimit.Value;

    /// <summary>Reads a declared limit.</summary>
    /// <param name="declared">The <c>ConcurrentProviderCalls</c> the run's block declares, or <see langword="null" /> where it declares none.</param>
    /// <returns>The limit, which is <see cref="Default" /> where nothing was declared.</returns>
    /// <exception cref="InvalidOperationException">Thrown, naming the variable, when the declaration is not a limit the run accepts.</exception>
    public static int Parse(string? declared)
    {
        if (declared is null)
        {
            return Default;
        }

        return int.TryParse(declared.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var limit)
            && limit is >= 1 and <= Most
            ? limit
            : throw new InvalidOperationException(
                $"{EvaluationDeclaration.Variable} declares ConcurrentProviderCalls that is not a whole number from 1 to {Most}.");
    }
}
