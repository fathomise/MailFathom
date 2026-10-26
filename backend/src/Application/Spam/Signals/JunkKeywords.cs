// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;
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

    /// <summary>Reports whether a recorded verdict rested on both <see cref="Junk" /> and <see cref="NotJunk" /> somebody else set.</summary>
    /// <param name="signals">The signals a classification recorded.</param>
    /// <returns><see langword="true" /> when the record names both keywords as signals.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="signals" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// RFC 9051 has a client remove both when it finds them together, and the record is where that pair is answered
    /// from, because it names only the keywords somebody other than this deployment set — the one pair worth removing.
    /// </remarks>
    public static bool NameBoth(IEnumerable<SpamSignal> signals)
    {
        ArgumentNullException.ThrowIfNull(signals);

        var named = signals
            .Where(static signal => signal.Kind is SpamSignalKind.JunkKeyword)
            .Select(static signal => signal.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return named.Contains(Junk) && named.Contains(NotJunk);
    }
}
