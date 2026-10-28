// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>Whether this deployment recognizes a message's author, and how much the recognition rests on.</summary>
/// <remarks>
/// <para>
/// This is a decision about the addresses people and administrators wrote down, read beside what the receiving server
/// established: <see cref="SenderAuthentication" /> answers whether an identity held, and this answers whether the
/// author is one this deployment recognizes and whether anything tied the message to them. An authenticated author
/// somebody named is <see cref="Trusted" />; a recognized <c>From</c> address that nothing tied the message to is
/// <see cref="Known" /> on an account that is not strict; and an author nobody named is <see cref="Unknown" />, whether
/// their authentication held or failed.
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
    /// <summary>This deployment recognizes nobody in the message, or recognizes the address on a strict account where nothing tied the message to it.</summary>
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
