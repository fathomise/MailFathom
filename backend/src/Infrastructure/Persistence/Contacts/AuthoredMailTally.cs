// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Contacts.Collection;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Contacts;

/// <summary>Counts an account's stored mail by the address that wrote it, out of PostgreSQL.</summary>
/// <remarks>
/// <para>
/// Answered from the index over the sender's comparison form, which the stored mail already carries because the mailbox
/// timeline filters on it. That is what lets collection have a threshold without a ledger of its own: the count is read
/// from mail that is already held rather than accumulated beside it.
/// </para>
/// <para>
/// Both halves stop at the ceiling the caller asked for, so the cost of the question is the threshold rather than the
/// number of messages one prolific correspondent wrote. The split is what makes a message stored in two folders of one
/// account count once: the copies of one message share the identifier its sender wrote, so the identified half counts
/// distinct identifiers, while a message whose sender wrote none has nothing to be recognized by and is counted per
/// stored copy.
/// </para>
/// <para>
/// Nothing logs. The address is personal data and so is the count of somebody's mail, which is why the answer goes back
/// to the caller rather than to an instrument.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class AuthoredMailTally(MailFathomDbContext readContext) : IAuthoredMailTally
{
    /// <inheritdoc />
    public async Task<int> CountMessagesAuthoredByAsync(
        MailAccountId account,
        EmailAddress author,
        int ceiling,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ceiling);

        var authored = Authored(readContext.StoredEmails.AsNoTracking(), account, author);

        var identified = await IdentifiedUpTo(authored, ceiling).CountAsync(cancellationToken);

        if (identified >= ceiling)
        {
            return ceiling;
        }

        var unidentified = await UnidentifiedUpTo(authored, ceiling - identified).CountAsync(cancellationToken);

        return identified + unidentified;
    }

    /// <summary>Narrows an account's stored mail to what one address wrote.</summary>
    internal static IQueryable<StoredEmailEntity> Authored(
        IQueryable<StoredEmailEntity> emails,
        MailAccountId account,
        EmailAddress author)
    {
        var accountValue = account.Value;
        var normalizedAddress = author.NormalizedAddress;

        return emails.Where(email =>
            email.MailboxAccountId == accountValue
            && email.SenderNormalizedAddress == normalizedAddress);
    }

    /// <summary>Selects the distinct identifiers the author's messages carry, stopping at the ceiling.</summary>
    /// <remarks>
    /// Ordered by the identifier, which <c>Distinct</c> has made unique, so the limit names its rows rather than leaving
    /// them to the plan; EF Core logs a limit nothing orders on every deployment that compiles it. The ordering follows
    /// <c>Distinct</c> rather than preceding it, because an ordering placed before it is erased.
    /// </remarks>
    internal static IQueryable<string?> IdentifiedUpTo(IQueryable<StoredEmailEntity> authored, int ceiling) => authored
        .Where(email => email.InternetMessageId != null)
        .Select(email => email.InternetMessageId)
        .Distinct()
        .OrderBy(internetMessageId => internetMessageId)
        .Take(ceiling);

    /// <summary>Selects the author's stored copies that carry no identifier, stopping at the ceiling.</summary>
    internal static IQueryable<StoredEmailEntity> UnidentifiedUpTo(IQueryable<StoredEmailEntity> authored, int ceiling) =>
        authored
            .Where(email => email.InternetMessageId == null)
            .OrderBy(email => email.Id)
            .Take(ceiling);
}
