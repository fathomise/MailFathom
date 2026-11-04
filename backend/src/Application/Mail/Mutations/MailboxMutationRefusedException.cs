// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Failures;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations;

/// <summary>Indicates that a mail server was reached, answered a change, and did not take it.</summary>
/// <remarks>
/// <para>
/// It exists so the two callers that act on that fact name one type instead of each keeping a list of the concrete
/// refusals: the performer, which decides from <see cref="RefusalClass" /> whether the change ends now or is asked again
/// within the refusal window, and a convergence pass, which reads the performer's answer rather than this exception.
/// </para>
/// <para>
/// What every subclass asserts is the opposite of what
/// <see cref="Synchronization.Sessions.MailboxUnavailableException" /> asserts: the server did not fail to answer, it
/// answered. That is what makes a refusal an attempt and an outage none. A failure to reach the server is not one of
/// these, however certain it looks.
/// </para>
/// <para>
/// It carries no <see cref="MailFathomErrorCode" /> of its own, because a code names one failure a boundary reports and
/// each subclass has one. Being abstract, it is never raised and never caught in place of knowing which refusal
/// happened where that matters.
/// </para>
/// </remarks>
public abstract class MailboxMutationRefusedException : MailFathomException
{
    /// <summary>Initializes a new refusal with a message safe to surface.</summary>
    /// <param name="operatorSafeMessage">A message free of credentials, hosts, remote paths, message content, and personal data.</param>
    protected MailboxMutationRefusedException(string operatorSafeMessage)
        : base(operatorSafeMessage)
    {
    }

    /// <summary>Initializes a new refusal with a message safe to surface and the failure that revealed it.</summary>
    /// <param name="operatorSafeMessage">A message free of credentials, hosts, remote paths, message content, and personal data.</param>
    /// <param name="innerException">The failure this refusal was raised for.</param>
    protected MailboxMutationRefusedException(string operatorSafeMessage, Exception innerException)
        : base(operatorSafeMessage, innerException)
    {
    }

    /// <summary>Gets whether asking again could alter the server's answer.</summary>
    public abstract MailboxMutationRefusalClass RefusalClass { get; }
}
