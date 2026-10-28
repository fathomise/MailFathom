// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails.Authentication;

namespace MailFathom.Application.Mail;

/// <summary>Resolves what configuration states about the senders one account recognizes: its owner's own addresses and, where it opted in, domains.</summary>
/// <remarks>
/// <para>
/// The policy is per account because each account's owner is a different person, and an account opts into its owner's
/// domains on its own. Contacts are not part of it: they are looked up per message, when its verdict is stored.
/// </para>
/// <para>
/// It is validated at startup, so this never fails and never returns a policy a caller has to check again. An account
/// this deployment no longer configures answers with
/// <see cref="SenderTrustPolicy.RecognizingNobody" /> for the reason every per-account reader here does: a
/// reading over mail whose account a reload removed must recognize nobody rather than throw.
/// </para>
/// </remarks>
public interface ISenderTrustPolicyReader
{
    /// <summary>Gets the policy one account's mail is judged by.</summary>
    /// <param name="accountId">The local account identifier.</param>
    /// <returns>The effective policy, or <see cref="SenderTrustPolicy.RecognizingNobody" /> when the account is no longer configured.</returns>
    SenderTrustPolicy GetTrustPolicy(MailAccountId accountId);
}
