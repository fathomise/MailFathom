// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>Reads which senders an account recognizes from the bound section.</summary>
/// <remarks>
/// The policies are built once for the snapshot this reader was constructed over rather than per lookup, because every
/// arriving message asks for one and building a policy derives a revision over everything it states. The build is
/// deferred rather than done in the constructor, so a deployment that never reads a message never walks the accounts.
/// </remarks>
internal sealed class ConfiguredSenderTrustPolicyReader : ISenderTrustPolicyReader
{
    private readonly MailSynchronizationOptions settings;

    private readonly Lazy<IReadOnlyDictionary<string, SenderTrustPolicy>> policiesByAccount;

    /// <summary>Initializes the reader over one snapshot of the mail section.</summary>
    /// <param name="settings">The snapshot the policies are read from.</param>
    internal ConfiguredSenderTrustPolicyReader(MailSynchronizationOptions settings)
    {
        this.settings = settings;
        this.policiesByAccount = new(this.ReadPolicies, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    /// <remarks>
    /// An account this snapshot no longer names recognizes nobody, for the reason the trusted authority answers with
    /// none: a reload can remove an account while a message of it is still being read.
    /// </remarks>
    public SenderTrustPolicy GetTrustPolicy(MailAccountId accountId) =>
        this.policiesByAccount.Value.TryGetValue(accountId.Value, out var policy)
            ? policy
            : SenderTrustPolicy.RecognizingNobody;

    /// <summary>Builds every account's verification policy once, keyed by the account identifier the lookups arrive with.</summary>
    /// <remarks>
    /// Two accounts configured under one identifier keep the first, for the reason the folder mappings do — startup
    /// validation refuses that, and a reload being rejected must not make a lookup throw. The accounts come from
    /// <see cref="MailSynchronizationOptions.DeclaredAccountsByOwner" />, so each person's own addresses and domains
    /// are trusted inside their own mailboxes rather than inside everybody's. Contacts are not read here: they are
    /// looked up per message when its verdict is stored.
    /// </remarks>
    private Dictionary<string, SenderTrustPolicy> ReadPolicies() =>
        this.settings.DeclaredAccountsByOwner
            .SelectMany(PoliciesOf)
            .Where(static account => account.Id is not null)
            .GroupBy(static account => account.Id!, StringComparer.Ordinal)
            .ToDictionary(
                static account => account.Key,
                static account => account.First().Policy,
                StringComparer.Ordinal);

    /// <summary>Builds the policies of the mailboxes one person owns, over the addresses and domains those same mailboxes state.</summary>
    /// <remarks>
    /// The addresses count on every one of the owner's mailboxes, and the domains only on a mailbox that set
    /// <see cref="MailSynchronizationAccountOptions.TrustOwnAccountDomains" />, so opting one account in leaves the
    /// owner's other accounts as they were.
    /// </remarks>
    private static IEnumerable<(string? Id, SenderTrustPolicy Policy)> PoliciesOf(
        IReadOnlyList<MailSynchronizationAccountOptions> owned)
    {
        IReadOnlyList<EmailAddress> ownAccountAddresses = ReadOwnAccountAddresses(owned);
        IReadOnlyList<SenderDomain> ownAccountDomains = ReadOwnAccountDomains(owned);

        return owned.Select(account => (
            MailSynchronizationOptions.TryReadAccountId(account.AccountId),
            SenderTrustPolicy.Create(
                ownAccountAddresses,
                account.TrustOwnAccountDomains ? ownAccountDomains : [])));
    }

    /// <summary>Reads the addresses one person's own accounts state: each user name that is an address, and each address an account sends as.</summary>
    /// <remarks>
    /// The sending address counts only where the account configures a submission endpoint, because that is the only
    /// case in which it is an address the account actually writes as.
    /// </remarks>
    private static IReadOnlyList<EmailAddress> ReadOwnAccountAddresses(
        IReadOnlyList<MailSynchronizationAccountOptions> owned) =>
    [
        .. owned
            .SelectMany(static account => new[]
            {
                account.UserName,
                account.Delivery is { IsConfigured: true } delivery ? delivery.ResolveFromAddress(account.UserName) : null,
            })
            .Select(static text => EmailAddress.TryCreate(displayName: null, text, out var address) ? address : (EmailAddress?)null)
            .OfType<EmailAddress>()
            .Distinct(),
    ];

    /// <summary>Reads the domains one person's own accounts send and receive under.</summary>
    /// <remarks>
    /// Derived from each account's user name, which is the only mailbox identity an IMAP account states: a server is
    /// reached at a host that is rarely the mail domain, and the account identifier is a key an operator invented. An
    /// account whose user name is a bare login rather than an address therefore contributes nothing, which is the
    /// honest answer — inventing a domain out of the host would recognize senders nobody named.
    /// </remarks>
    private static IReadOnlyList<SenderDomain> ReadOwnAccountDomains(
        IReadOnlyList<MailSynchronizationAccountOptions> owned) =>
    [
        .. owned
            .Select(static account => TryReadOwnDomain(account.UserName))
            .OfType<SenderDomain>()
            .Distinct(),
    ];

    /// <summary>Reads the mail domain one account's user name states, or nothing when it states none.</summary>
    /// <remarks>
    /// The at-sign is what separates the two shapes an IMAP user name takes. Without one the value is a login and
    /// naming a domain from it would be a guess; with one it is the mailbox address the account belongs to, and the
    /// domain is what follows the last at-sign for the reason every address in this system is split there.
    /// </remarks>
    private static SenderDomain? TryReadOwnDomain(string? userName) =>
        userName is not null
        && userName.Contains('@', StringComparison.Ordinal)
        && SenderDomain.TryCreateFromMailbox(userName, out var domain)
            ? domain
            : null;
}
