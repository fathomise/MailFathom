// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Paging;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.BrowseThreadList;

/// <summary>The opaque value a page of the conversation list hands back so the next page continues after it.</summary>
/// <remarks>
/// It carries the position of the last conversation on the page and a fingerprint of the list it was read under — the
/// account it was narrowed to, or none — so a cursor taken from one account's list and handed to another's is refused
/// rather than answered with a page of the wrong list. Nothing on the server remembers it.
/// </remarks>
public readonly record struct EmailThreadListCursor
{
    private EmailThreadListCursor(EmailThreadListPosition position, string filterFingerprint)
    {
        this.Position = position;
        this.FilterFingerprint = filterFingerprint;
    }

    /// <summary>Gets the position the next page continues after.</summary>
    public EmailThreadListPosition Position { get; }

    /// <summary>Gets the fingerprint of the list the cursor was issued for.</summary>
    public string FilterFingerprint { get; }

    /// <summary>Fingerprints the list one request reads.</summary>
    /// <param name="account">The account the list is narrowed to, or <see langword="null" /> for every account the reader is assigned.</param>
    /// <returns>The fingerprint a cursor of that list carries.</returns>
    public static string FingerprintOf(MailAccountSelector? account) =>
        PageFilterFingerprint.Of("threads", account?.Value);

    /// <summary>Builds the cursor that continues after one conversation.</summary>
    /// <param name="position">The position of the last conversation on the page.</param>
    /// <param name="filterFingerprint">The fingerprint of the list the page was read under.</param>
    /// <returns>The cursor.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="filterFingerprint" /> is empty.</exception>
    public static EmailThreadListCursor After(EmailThreadListPosition position, string filterFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filterFingerprint);

        return new EmailThreadListCursor(position, filterFingerprint);
    }

    /// <summary>Reads a cursor a client handed back.</summary>
    /// <param name="text">The encoded cursor.</param>
    /// <param name="cursor">The cursor read, or the default where the text is not one this deployment issues.</param>
    /// <returns>Whether the text is a well-formed cursor.</returns>
    public static bool TryDecode(string? text, out EmailThreadListCursor cursor)
    {
        cursor = default;

        if (!KeysetCursorPayload.TryDecode(text, out var payload))
        {
            return false;
        }

        cursor = new EmailThreadListCursor(
            new EmailThreadListPosition(payload.Position, EmailThreadId.Create(payload.Identity)),
            payload.FilterFingerprint);

        return true;
    }

    /// <summary>Encodes the cursor for the wire.</summary>
    /// <returns>The opaque text a client hands back.</returns>
    public string Encode() => KeysetCursorPayload
        .At(this.Position.NewestArrival, this.Position.ThreadId.Value, this.FilterFingerprint)
        .Encode();
}
