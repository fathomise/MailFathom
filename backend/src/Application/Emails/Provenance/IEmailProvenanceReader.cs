// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Provenance;

/// <summary>Reads where stored emails came from, out of what the deployment recorded about each.</summary>
/// <remarks>
/// It reads by identity and applies no scope, because every caller hands it emails it has already read under its own:
/// the passages a search returned, the messages of a conversation a tool opened, the batch a background pass selected.
/// What it answers about a message beyond that message's own row is one yes or no — whether its mailbox held earlier
/// mail from or to the sender — and that is read only from mail still part of the mailbox in the folders a mapping
/// admits to tools, so it says nothing about any other message a caller could not read.
/// </remarks>
public interface IEmailProvenanceReader
{
    /// <summary>Reads the provenance of each named email.</summary>
    /// <param name="emails">The emails, which the caller has already read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>An entry for every named email still stored; one no longer stored is absent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="emails" /> is <see langword="null" />.</exception>
    Task<IReadOnlyDictionary<StoredEmailId, EmailProvenance>> ReadAsync(
        IReadOnlyCollection<StoredEmailId> emails,
        CancellationToken cancellationToken);
}
