// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Synchronization;

/// <summary>A draft revision MailFathom filed locally first, and what its append reported about the copy it put on the server.</summary>
/// <param name="Email">The stored message the revision is.</param>
/// <param name="Placement">Where the server put the appended copy, as far as it said.</param>
/// <param name="InternetMessageId">The <c>Message-ID</c> the appended copy carries.</param>
public sealed record FiledDraftCopy(StoredEmailId Email, RemoteEmailPlacement Placement, string? InternetMessageId)
{
    /// <summary>Reports whether a discovered occurrence is this revision's appended copy.</summary>
    /// <param name="occurrenceId">The occurrence synchronization discovered.</param>
    /// <param name="discoveredMessageId">The <c>Message-ID</c> the server reported for it.</param>
    /// <returns><see langword="true" /> when the server named this occurrence, or named none and the identity matches.</returns>
    /// <remarks>
    /// The identity is compared only where the append named no placement: a draft keeps one <c>Message-ID</c> across its
    /// revisions, so a copy of an earlier revision carries the same one, and only the server's own statement tells them
    /// apart where it made one.
    /// </remarks>
    public bool IsAppendedCopyAt(EmailOccurrenceId occurrenceId, string? discoveredMessageId)
    {
        ArgumentNullException.ThrowIfNull(occurrenceId);

        return this.Placement is { UidValidity: { } uidValidity, Uid: { } uid }
            ? uidValidity == occurrenceId.UidValidity && uid == occurrenceId.Uid
            : this.InternetMessageId is { } appended
                && string.Equals(appended, discoveredMessageId, StringComparison.Ordinal);
    }
}
