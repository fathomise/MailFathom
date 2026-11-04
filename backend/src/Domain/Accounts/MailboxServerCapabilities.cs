// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Accounts;

/// <summary>The capabilities of an account's IMAP server that MailFathom decides anything from without a session open.</summary>
/// <remarks>
/// <para>
/// A set rather than a list of every atom the server sent, because what is recorded is what a custodian deciding at the
/// moment of a person's act reads: a capability nothing decides from is not worth a column's worth of stale truth. A
/// capability a later decision needs is a member added here and a line in the adapter that maps it, and an account
/// recorded before it existed reads as not advertising it until its next session says otherwise.
/// </para>
/// <para>
/// Stored by name, so a value never depends on the bit a member happens to hold.
/// </para>
/// </remarks>
[Flags]
public enum MailboxServerCapabilities
{
    /// <summary>Nothing MailFathom decides from, which is also what an account whose server was never reached reads as.</summary>
    None = 0,

    /// <summary>RFC 4315 <c>UIDPLUS</c>: the server names where it put a copied, moved, or appended message.</summary>
    UidPlus = 1,

    /// <summary>RFC 9051 <c>IMAP4rev2</c>, which folds <c>UIDPLUS</c> into the base protocol.</summary>
    Imap4Rev2 = 2,

    /// <summary>Gmail's <c>X-GM-EXT-1</c>: the server's folders are labels over one store of messages.</summary>
    /// <remarks>
    /// An expunge in a folder there takes a label off a message rather than removing it, so MailFathom removes nothing it
    /// did not compose from such a server.
    /// </remarks>
    FoldersAreLabels = 4,
}
