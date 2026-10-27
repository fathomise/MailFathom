// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Persistence.Users;

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>Resolves the sending identities of a set of users' accounts from those users' own settings.</summary>
/// <remarks>
/// Each user is composed once, through the same served-user cache a request reads, so a user whose record has not moved
/// costs a lookup rather than a composition, and every account they are served is answered from that one composition.
/// A user whose record the reader refuses for what it holds is left out, so their accounts send as nobody rather than
/// one operator's oversized record failing the extraction of every signed arrival in the organization.
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
            if (await this.ReadSettingsAsync(user, cancellationToken) is not { } settings)
            {
                continue;
            }

            foreach (var (account, identity) in ServedIdentitiesOf(settings))
            {
                identities.TryAdd(account, identity);
            }
        }

        return identities;
    }

    /// <summary>Reads one user's settings, or nothing where the reader refused their record for what it holds.</summary>
    /// <remarks>
    /// A refusal carrying no inner failure is the record's own and leaves the user out, as an account run does; one with
    /// an inner failure is the database declining the read, which fails the extraction so it is read again.
    /// </remarks>
    private async Task<MailSynchronizationOptions?> ReadSettingsAsync(UserId user, CancellationToken cancellationToken)
    {
        try
        {
            return await accounts.ReadUserSettingsAsync(user, cancellationToken);
        }
        catch (UserSettingsUnreadableException refused) when (refused.InnerException is null)
        {
            return null;
        }
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
