// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Spam;

namespace MailFathom.Application.Spam.Signals;

/// <summary>The three junk keywords RFC 9051 § 2.3.2 gives a mailbox, and how a message carrying them is read.</summary>
/// <remarks>
/// IMAP compares keywords without regard to case, and the stored set is folded the way
/// <see cref="RemoteEmailKeywords.Normalized" /> folds it, so every comparison here goes through that one reduction
/// rather than a second spelling of it. What MailFathom writes is the RFC's own spelling.
/// </remarks>
public static class JunkKeywords
{
    /// <summary>The keyword a message somebody judged junk carries.</summary>
    public const string Junk = "$Junk";

    /// <summary>The keyword a message somebody judged definitely not junk carries, which only a person can say.</summary>
    public const string NotJunk = "$NotJunk";

    /// <summary>The keyword a message highly likely to be phishing carries, beside <see cref="Junk" />.</summary>
    public const string Phishing = "$Phishing";

    private static readonly string[] All = [Junk, NotJunk, Phishing];

    /// <summary>Reports whether a stored keyword set carries one keyword, whatever case either is written in.</summary>
    /// <param name="keywords">The keywords the message carries.</param>
    /// <param name="keyword">The keyword asked about.</param>
    /// <returns><see langword="true" /> when the set carries it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="keywords" /> is <see langword="null" />.</exception>
    public static bool Carries(RemoteEmailKeywords keywords, string keyword)
    {
        ArgumentNullException.ThrowIfNull(keywords);

        var normalized = RemoteEmailKeywords.Normalized(keyword);

        return keywords.Values.Contains(normalized, StringComparer.Ordinal);
    }

    /// <summary>Reports whether a keyword change would put one of the three junk keywords on a message or take one off.</summary>
    /// <param name="mutation">The keyword mutation asked for: an addition, a removal, or a replacement.</param>
    /// <param name="named">The keywords the change names.</param>
    /// <param name="carried">The keywords the message carries now.</param>
    /// <returns><see langword="true" /> when the change would add or remove <see cref="Junk" />, <see cref="NotJunk" />, or <see cref="Phishing" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="named" /> or <paramref name="carried" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// An addition or a removal changes the junk keywords exactly where it names one. A replacement states the whole set,
    /// so it changes them where the set it names and the set the message carries disagree about any of the three — which
    /// is what lets a replacement that leaves them as they stand go ahead while one that drops a <c>$Junk</c> does not.
    /// </remarks>
    public static bool AreChangedBy(MailboxMutation mutation, AuthoredMailKeywords named, RemoteEmailKeywords carried)
    {
        ArgumentNullException.ThrowIfNull(named);
        ArgumentNullException.ThrowIfNull(carried);

        var namedSet = RemoteEmailKeywords.Create(named.Values);

        return mutation == MailboxMutation.SetKeywords
            ? All.Any(keyword => Carries(namedSet, keyword) != Carries(carried, keyword))
            : All.Any(keyword => Carries(namedSet, keyword));
    }

    /// <summary>Reports whether a recorded verdict rested on a <see cref="NotJunk" /> somebody set.</summary>
    /// <param name="signals">The signals a classification recorded.</param>
    /// <returns><see langword="true" /> when the record names <see cref="NotJunk" /> as a signal.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="signals" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A verdict that is not spam asks the mailbox for something only where a <c>$Junk</c> may stand beside the person's
    /// <c>$NotJunk</c>, and that needs the <c>$NotJunk</c> among its signals, so this is what lets every other such verdict
    /// end before the message's occurrence is read.
    /// </remarks>
    public static bool NameNotJunk(IEnumerable<SpamSignal> signals)
    {
        ArgumentNullException.ThrowIfNull(signals);

        return signals.Any(static signal =>
            signal.Kind is SpamSignalKind.JunkKeyword
            && string.Equals(signal.Name, NotJunk, StringComparison.OrdinalIgnoreCase));
    }
}
