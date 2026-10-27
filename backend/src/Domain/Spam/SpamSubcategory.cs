// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Spam;

/// <summary>Names what kind of spam a message with a <see cref="SpamVerdict.Spam" /> verdict is.</summary>
/// <remarks>
/// <para>
/// A subcategory of the spam verdict and nothing wider. It is not a mail category: sorting every message by topic,
/// whatever its verdict, is a different question with a different answer, which is why nothing here takes that word on
/// its own.
/// </para>
/// <para>
/// The member name is the published identity, as it is for the verdict and the stage beside it in the same record: it is
/// what the column holds and what both administrative surfaces print. A member is therefore never renamed.
/// </para>
/// </remarks>
public enum SpamSubcategory
{
    /// <summary>Harvesting credentials, payment details, or one-time codes, usually by impersonating a bank, a courier, or a service.</summary>
    Phishing = 0,

    /// <summary>Impersonating a colleague, an executive, or a supplier, typically to redirect a payment, often with no link at all.</summary>
    BusinessEmailCompromise = 1,

    /// <summary>A pretext for opening an attachment or a download. The pretext is judged; the file itself is not scanned.</summary>
    MalwareLure = 2,

    /// <summary>Advance-fee, lottery, inheritance, investment, and callback scams.</summary>
    Scam = 3,

    /// <summary>Blackmail and sextortion demanding payment.</summary>
    Extortion = 4,

    /// <summary>Unsolicited commercial mail: unwanted, but harmless.</summary>
    Marketing = 5,
}
