// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Domain.AutomaticReplies;

/// <summary>Decides from an address alone whether a reply sent to it would reach a program rather than a person.</summary>
/// <remarks>
/// RFC 3834 asks a responder not to answer the addresses a mailing list, a bounce, or a notification is sent from,
/// because the answer reaches nobody and, at worst, a program that answers back. The headers a message carries say most
/// of that; this is what the address says when they do not. A miss costs one unread reply rather than a loop, because
/// every reply carries <c>Auto-Submitted</c> and is answered once per sender.
/// </remarks>
public static class AutomaticReplyRecipientPolicy
{
    /// <summary>The longest address an automatic reply is sent to, which is the longest RFC 5321 lets a path carry.</summary>
    public const int MaximumAddressLength = 320;

    /// <summary>The local parts, with their separators removed, that name a mailbox nobody reads.</summary>
    private static readonly string[] UnattendedPrefixes = ["NOREPLY", "DONOTREPLY", "MAILERDAEMON", "POSTMASTER"];

    /// <summary>Answers whether an address is one an automatic reply is never sent to.</summary>
    /// <param name="address">The address the reply would go to.</param>
    /// <returns><see langword="true" /> for a no-reply, bounce, or list-administration mailbox, and for one longer than <see cref="MaximumAddressLength" />.</returns>
    public static bool IsUnattended(EmailAddress address)
    {
        if (address.Address.Length > MaximumAddressLength
            || !EmailAddress.TrySplit(address.Address, out var localPart, out _))
        {
            return true;
        }

        var local = localPart.ToString().ToUpperInvariant();

        // RFC 3834 section 2 names the two forms a list's administrative addresses take.
        if (local.StartsWith("OWNER-", StringComparison.Ordinal) || local.EndsWith("-REQUEST", StringComparison.Ordinal))
        {
            return true;
        }

        var bare = new string([.. local.Where(static character => character is not ('-' or '_' or '.'))]);

        return UnattendedPrefixes.Any(prefix => bare.StartsWith(prefix, StringComparison.Ordinal));
    }
}
