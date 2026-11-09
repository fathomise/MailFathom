// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Domain.Emails;

/// <summary>States what becomes of the local copy of an email MailFathom itself deleted on the mail server.</summary>
/// <remarks>
/// <para>
/// It is a separate decision from <see cref="RemotelyDeletedEmailDisposition" /> because the two answer for different
/// acts. That one governs a disappearance somebody else caused and MailFathom observed afterwards; this one governs a
/// deletion the mailbox user authored through MailFathom. Inheriting one value for both would make an account
/// configured to keep what its server loses also keep what MailFathom was just told to delete, and a delete a person
/// asked for is a delete: this setting has no value that leaves the message readable.
/// </para>
/// <para>
/// The value is resolved when the mutation is written down and travels on its record, so changing the setting while a
/// delete is in flight governs the deletes authored after the change and leaves that one exactly as it was begun.
/// </para>
/// </remarks>
[SuppressMessage("Design", "CA1008:Enums should have zero value", Justification = "Zero named the value this setting no longer takes, and reusing the number would give a configuration stating it another member's meaning, so it stays unused and validation refuses it.")]
public enum AuthoredDeleteEmailDisposition
{
    /// <summary>Keeps the local row as a tombstone that every mailbox query excludes.</summary>
    /// <remarks>
    /// The record that the email existed survives and the mail itself stops being reachable, which is what makes an
    /// authored delete auditable without leaving the message readable. It is the exact counterpart of
    /// <see cref="RemotelyDeletedEmailDisposition.RetainTombstone" />.
    /// </remarks>
    RetainTombstone = 1,

    /// <summary>Removes the local row together with its raw MIME and everything derived from it.</summary>
    /// <remarks>
    /// Nothing of the email survives locally, which is what deleting a message means, so it is the default. On a delete
    /// that reaches the server, the removal happens once the server is seen to no longer serve the message as live — gone,
    /// or flagged <c>\Deleted</c> by a delete that issued no expunge — and is deliberate. Where the server still holds the
    /// message and somebody removes the flag, synchronization fetches it again rather than leaving it lost.
    /// </remarks>
    EraseLocalCopy = 2,
}
