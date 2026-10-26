// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Domain.Accounts;

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>Resolves an account's sending identity by composing that account's own settings, as a run of it would.</summary>
/// <remarks>
/// Each answer is remembered for the scope this instance lives in, because one scope asks about the accounts of one
/// organization at most and composing an account reads its assigned users' records. An account an administrator
/// changes is read again by the next scope.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this directory.")]
internal sealed class ComposedSenderIdentityDirectory(IMailSynchronizationAccountSource accounts) : ISenderIdentityDirectory
{
    private readonly Dictionary<MailAccountId, OutgoingSenderIdentity?> identities = [];

    /// <inheritdoc />
    public async Task<OutgoingSenderIdentity?> FindSenderIdentityAsync(MailAccountId accountId, CancellationToken cancellationToken)
    {
        if (!this.identities.TryGetValue(accountId, out var identity))
        {
            identity = await accounts.ReadRunSettingsAsync(accountId, previous: null, cancellationToken) is { } settings
                ? settings.Readers.OutgoingSenderIdentities.FindSenderIdentity(accountId)
                : null;
            this.identities[accountId] = identity;
        }

        return identity;
    }
}
