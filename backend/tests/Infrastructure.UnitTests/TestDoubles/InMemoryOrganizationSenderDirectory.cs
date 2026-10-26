// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Mail.Dkim;

namespace MailFathom.Infrastructure.UnitTests.TestDoubles;

/// <summary>Holds the deployment's state the way the persisted directory answers it, including what it leaves out.</summary>
/// <remarks>
/// A key answers only for its own organization and only while unrevoked, and a blind copy only for the accounts it was
/// sent through — the same filters the persisted directory's queries apply, so a verifier test exercises the verifier
/// rather than a double that answers more than PostgreSQL would.
/// </remarks>
internal sealed class InMemoryOrganizationSenderDirectory : IOrganizationSenderDirectory
{
    /// <summary>Gets which organization each account belongs to.</summary>
    public Dictionary<MailAccountId, Guid> Memberships { get; } = [];

    /// <summary>Gets every key the deployment holds, by identifier.</summary>
    public Dictionary<Guid, StoredOrganizationKey> Keys { get; } = [];

    /// <summary>Gets every blind copy a send record names.</summary>
    public List<(MailAccountId SentThrough, string MessageId, string Recipient)> BlindCopies { get; } = [];

    /// <summary>Gets how many times a key was looked up.</summary>
    public int KeyReads { get; private set; }

    /// <inheritdoc />
    public Task<Guid?> FindOrganizationAsync(MailAccountId account, CancellationToken cancellationToken) =>
        Task.FromResult(this.Memberships.TryGetValue(account, out var organization) ? organization : (Guid?)null);

    /// <inheritdoc />
    public Task<ReadOnlyMemory<byte>?> FindVerifyingKeyAsync(Guid organizationId, Guid keyId, CancellationToken cancellationToken)
    {
        this.KeyReads++;

        return Task.FromResult(
            this.Keys.TryGetValue(keyId, out var key) && key.OrganizationId == organizationId && !key.Revoked
                ? key.PublicKey.AsMemory()
                : (ReadOnlyMemory<byte>?)null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MailAccountId>> ListAccountsAsync(Guid organizationId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MailAccountId>>(
            [.. this.Memberships.Where(membership => membership.Value == organizationId).Select(static membership => membership.Key)]);

    /// <inheritdoc />
    public Task<IReadOnlyList<EmailAddress>> ListBlindCopyRecipientsAsync(
        IReadOnlyCollection<MailAccountId> sentThrough,
        string internetMessageId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EmailAddress>>(
        [
            .. this.BlindCopies
                .Where(copy => sentThrough.Contains(copy.SentThrough) && copy.MessageId == internetMessageId)
                .Select(static copy => EmailAddress.TryCreate(displayName: null, copy.Recipient, out var parsed) ? parsed : default),
        ]);

    /// <summary>Holds an organization's key the way the persisted directory and the minter leave it.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="key">The key, which is held unrevoked.</param>
    /// <returns>The directory, for chaining.</returns>
    public InMemoryOrganizationSenderDirectory Holding(Guid organizationId, OrganizationSignatureFixtures.OrganizationKey key)
    {
        this.Keys[key.KeyId] = new StoredOrganizationKey(organizationId, key.PublicKey, Revoked: false);

        return this;
    }

    /// <summary>One key as the store holds it.</summary>
    /// <param name="OrganizationId">The organization it belongs to.</param>
    /// <param name="PublicKey">The public half.</param>
    /// <param name="Revoked">Whether it was revoked.</param>
    internal sealed record StoredOrganizationKey(Guid OrganizationId, byte[] PublicKey, bool Revoked);
}
