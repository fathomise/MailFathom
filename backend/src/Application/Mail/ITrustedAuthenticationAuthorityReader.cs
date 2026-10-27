// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails.Authentication;

namespace MailFathom.Application.Mail;

/// <summary>Resolves the one server whose sender-authentication statements an account believes.</summary>
/// <remarks>
/// <para>
/// An account's own <c>TrustedAuthenticationServiceIdentifier</c> always wins. An account that names none believes the
/// server the deployment's list of known providers names for the IMAP host it synchronizes from, and an account whose
/// host has no entry believes no header: that is an ordinary state, not a failure, and every message it holds carries
/// the not-established verdict.
/// </para>
/// <para>
/// The configured value is validated at startup, so this never fails over configuration. An account this deployment no
/// longer configures answers with <see cref="TrustedAuthenticationAuthority.None" />: a reading over mail whose account
/// a reload removed must believe nothing rather than throw.
/// </para>
/// </remarks>
public interface ITrustedAuthenticationAuthorityReader
{
    /// <summary>Gets the authority one account believes.</summary>
    /// <param name="accountId">The local account identifier.</param>
    /// <param name="cancellationToken">Cancels the read of the known-provider list.</param>
    /// <returns>The account's own authority, else its host's known provider's, else <see cref="TrustedAuthenticationAuthority.None" />.</returns>
    Task<TrustedAuthenticationAuthority> GetTrustedAuthorityAsync(MailAccountId accountId, CancellationToken cancellationToken);
}
