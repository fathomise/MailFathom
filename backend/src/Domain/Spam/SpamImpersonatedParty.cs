// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Spam;

/// <summary>Names the kind of party a phishing or business-email-compromise message pretends to come from.</summary>
/// <remarks>
/// <para>
/// The kind and never the name. A decision model writes no text, so a name would have to come from somewhere else and
/// would then be model-written text about a real person or organization stored beside their mail. Where a person needs a
/// name, the message's own sender display name is already in front of them.
/// </para>
/// <para>
/// The first six are the parties phishing and business email compromise are described by. A kind added later is
/// appended with the next value.
/// </para>
/// </remarks>
public enum SpamImpersonatedParty
{
    /// <summary>A bank or another financial institution.</summary>
    Bank = 0,

    /// <summary>A courier or a postal service.</summary>
    Courier = 1,

    /// <summary>An online service the reader holds an account with.</summary>
    OnlineService = 2,

    /// <summary>A colleague of the reader.</summary>
    Colleague = 3,

    /// <summary>An executive of the reader's own organization.</summary>
    Executive = 4,

    /// <summary>A supplier or another business the reader's organization pays.</summary>
    Supplier = 5,

    /// <summary>A party none of the other values describes.</summary>
    Other = 6,

    /// <summary>The message pretends to come from nobody in particular.</summary>
    NobodyEvident = 7,
}
