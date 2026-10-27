// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails.Authentication;

namespace MailFathom.Application.Mail;

/// <summary>Finds the DMARC record that governs a message's displayed domain.</summary>
/// <remarks>
/// <para>
/// It is the second of the two places MailFathom reaches the network to judge a sender, beside
/// <see cref="IDkimPublicKeyRecordResolver" />, and a named seam for the same reason: an implementation may answer from
/// DNS, from a cache, or from a fixture, and the reading above it never learns which.
/// </para>
/// <para>
/// <b>Every implementation is bounded and answers rather than raises.</b> No record anywhere is an answer, and a name
/// that could not be read is another — it ends the search and reads as <see cref="DmarcRecordLookup.NotReadable" />,
/// which leaves the author not established and says nothing against the sender.
/// </para>
/// <para>
/// What is disclosed is <c>_dmarc.&lt;domain&gt;</c> for the displayed domain and its parents: names a domain
/// publishes in order to be asked for, carrying nothing about the message, the mailbox, or the recipient. It is asked
/// when a message is stored, never when one is read.
/// </para>
/// </remarks>
public interface IDmarcRecordResolver
{
    /// <summary>Walks from the displayed domain up the naming tree to the first name that publishes a DMARC record.</summary>
    /// <param name="fromDomain">The domain of the message's first <c>From</c> mailbox.</param>
    /// <param name="cancellationToken">Cancels the walk and the lookups it makes.</param>
    /// <returns>The record, the answer that none is published, or the answer that a name could not be read.</returns>
    /// <remarks>
    /// Cancellation through <paramref name="cancellationToken" /> is the caller giving up and propagates. A deadline the
    /// implementation applies to a lookup is not: it answers <see cref="DmarcRecordLookup.NotReadable" />.
    /// </remarks>
    Task<DmarcRecordLookup> ResolveAsync(SenderDomain fromDomain, CancellationToken cancellationToken);
}
