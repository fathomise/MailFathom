// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Emails.Census;

/// <summary>What a caller asks for when counting the local mailbox copy.</summary>
/// <remarks>
/// The filters are a listing's, read the way <see cref="ListEmails.ListEmailsRequest" /> reads them, so a count and a
/// listing given the same filters count and list the same mail. Nothing here has been validated:
/// <see cref="MailboxCensusReader" /> does that.
/// </remarks>
public sealed record CountEmailsRequest
{
    /// <summary>Gets the text naming the accounts to count, or empty for every account this deployment serves the caller.</summary>
    public IReadOnlyList<MailAccountSelector> Accounts { get; init; } = [];

    /// <summary>Gets the folders to count, or empty for every folder of the named accounts.</summary>
    public IReadOnlyList<MailFolderReference> Folders { get; init; } = [];

    /// <summary>Gets the address the sender must carry, in any case, or <see langword="null" /> for any sender.</summary>
    public string? SenderAddress { get; init; }

    /// <summary>Gets the address a <c>To</c> or <c>Cc</c> recipient must carry, or <see langword="null" /> for any recipient.</summary>
    public string? RecipientAddress { get; init; }

    /// <summary>Gets the fragment the subject must contain, compared without regard to case, or <see langword="null" /> for any subject.</summary>
    public string? SubjectFragment { get; init; }

    /// <summary>Gets the inclusive start of the received range, or <see langword="null" /> for no start.</summary>
    public DateTimeOffset? ReceivedOnOrAfter { get; init; }

    /// <summary>Gets the exclusive end of the received range, or <see langword="null" /> for no end.</summary>
    public DateTimeOffset? ReceivedBefore { get; init; }

    /// <summary>Gets the <c>\Seen</c> mark to require, or <see langword="null" /> for either.</summary>
    public bool? IsMarkedSeen { get; init; }

    /// <summary>Gets the <c>\Flagged</c> state to require, or <see langword="null" /> for either.</summary>
    public bool? IsFlagged { get; init; }

    /// <summary>Gets the keyword an email must carry, in any case, or <see langword="null" /> for any keyword.</summary>
    public string? Keyword { get; init; }

    /// <summary>Gets whether attachments are required, or <see langword="null" /> for either.</summary>
    public bool? HasAttachments { get; init; }

    /// <summary>Gets the category an email must be recorded under, in any case, or <see langword="null" /> for any category.</summary>
    public string? Category { get; init; }

    /// <summary>Gets who the count is answering for, which decides the folders it reaches, exactly as a listing's reader does.</summary>
    public MailboxReaderKind Reader { get; init; }

    /// <summary>Gets what the mail is grouped by.</summary>
    public MailCensusDimension Dimension { get; init; }

    /// <summary>Gets how many groups the reading carries at most, or <see langword="null" /> for <see cref="MailboxCensusReader.DefaultLeadingGroups" />.</summary>
    /// <remarks>One outside the accepted range is refused rather than clamped, as a listing's page size is.</remarks>
    public int? MaximumGroups { get; init; }

    /// <summary>Gets the time zone a day, a week, or a month is read on, which is the person's own.</summary>
    public UserTimeZone Zone { get; init; } = UserTimeZone.Coordinated;
}
