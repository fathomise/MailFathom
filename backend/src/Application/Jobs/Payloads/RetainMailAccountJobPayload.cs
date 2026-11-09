// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Jobs.Payloads;

/// <summary>Names the one account a night's retention walk owes a run of the retention steps.</summary>
public sealed record RetainMailAccountJobPayload : IJobPayload
{
    /// <summary>Gets the walk that asked for this account's run.</summary>
    public required string WalkId { get; init; }

    /// <summary>Gets the account, by its generated identifier.</summary>
    public required string AccountId { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public JobType JobType => JobType.RetainMailAccount;

    /// <summary>Gets the account the steps run for.</summary>
    [JsonIgnore]
    public MailAccountId Account => MailAccountId.Create(this.AccountId);

    /// <summary>Describes one account's run within one walk.</summary>
    /// <param name="walkId">The walk asking for it.</param>
    /// <param name="account">The account.</param>
    /// <returns>The payload.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="walkId" /> is blank.</exception>
    public static RetainMailAccountJobPayload For(string walkId, MailAccountId account)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walkId);

        return new RetainMailAccountJobPayload { WalkId = walkId, AccountId = account.Value };
    }

    /// <summary>Composes the identity under which this run is enqueued.</summary>
    /// <returns>The idempotency key, one per account per walk.</returns>
    public JobIdempotencyKey ToIdempotencyKey() =>
        JobIdempotencyKey.Create($"{JobType.RetainMailAccount.Name}:{this.WalkId}:{this.AccountId}");
}
