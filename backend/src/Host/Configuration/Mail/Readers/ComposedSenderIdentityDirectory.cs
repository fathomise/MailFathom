// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>Resolves the sending identities of a set of users' accounts from those users' own settings.</summary>
/// <remarks>
/// Each user is composed once, through the same served-user cache a request reads, so a user whose record has not moved
/// costs a lookup rather than a composition, and every account they are served is answered from that one composition.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this directory.")]
internal sealed class ComposedSenderIdentityDirectory(IMailSynchronizationAccountSource accounts) : ISenderIdentityDirectory
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<MailAccountId, OutgoingSenderIdentity>> ReadServedIdentitiesAsync(
        IReadOnlyCollection<UserId> users,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(users);

        var identities = new Dictionary<MailAccountId, OutgoingSenderIdentity>();

        foreach (var user in users.Distinct())
        {
            var settings = await accounts.ReadUserSettingsAsync(user, cancellationToken);

            foreach (var (account, identity) in ServedIdentitiesOf(settings))
            {
                identities.TryAdd(account, identity);
            }
        }

        return identities;
    }

    private static IEnumerable<(MailAccountId Account, OutgoingSenderIdentity Identity)> ServedIdentitiesOf(
        MailSynchronizationOptions settings) =>
        settings.DeclaredAccounts
            .Select(static declared => MailSynchronizationOptions.TryReadAccountId(declared.AccountId))
            .OfType<string>()
            .Select(static accountId => MailAccountId.Create(accountId))
            .Select(account => (Account: account, Identity: settings.Readers.OutgoingSenderIdentities.FindSenderIdentity(account)))
            .Where(static served => served.Identity is not null)
            .Select(static served => (served.Account, served.Identity!));
}
