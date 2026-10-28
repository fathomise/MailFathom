// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>Whether this deployment counts a message's authenticated author as somebody it knows.</summary>
/// <remarks>
/// <para>
/// This is a decision about a list an operator and a reader write to, and it is deliberately independent of what the
/// receiving server established: <see cref="SenderAuthentication" /> answers whether an identity held, and this answers
/// whether the identity that held is one this deployment recognizes. Reading the two together is what makes a message
/// legible — an authenticated author nobody has named reads very differently from an author whose authentication failed,
/// and both are <see cref="Unknown" /> here.
/// </para>
/// <para>
/// <see cref="Unknown" /> is therefore the ordinary answer rather than a negative one. Most legitimate mail arrives from
/// a correspondent nobody has ever named and reads exactly as a forgery does at this level, so nothing derived from this
/// value alone may read as an accusation. Whether a message is unwanted is spam classification's question and is reached
/// by other means entirely.
/// </para>
/// </remarks>
public enum SenderTrustLevel
{
    /// <summary>This deployment recognizes nobody in the message, whether or not an author was authenticated.</summary>
    Unknown = 0,

    /// <summary>The authenticated author is one this deployment recognizes, and the verdict says what named them.</summary>
    Trusted = 1,

    /// <summary>The <c>From</c> address is one this deployment recognizes, while nothing established that its domain sent the message.</summary>
    /// <remarks>
    /// A weaker claim than <see cref="Trusted" /> and never to be read as one: the address is a header anybody can write,
    /// so a forged <c>From</c> showing a contact's address reaches this level too. Only an account that is not strict
    /// produces it, and never under a server's DMARC <c>fail</c> unless the domain publishes a monitoring-only policy.
    /// </remarks>
    Known = 2,
}
