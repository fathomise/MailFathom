// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using MailFathom.Application.Emails.Provenance;

namespace MailFathom.AI.Orchestration;

/// <summary>Writes one message as the passages a derivation reads, each under the position an answer cites it by.</summary>
/// <remarks>
/// One shape for every derivation that reads a message passage by passage, so two readings of the same message are
/// shown it the same way and an evaluation of one can be compared with the other. The position is an attribute this
/// service writes, so no passage can claim to be another one; the subject and the passages are quoted text.
/// </remarks>
internal static class MessagePassages
{
    /// <summary>Names the element the message is written in.</summary>
    internal const string MessageElementName = "message";

    /// <summary>Names the attribute carrying when the message arrived.</summary>
    internal const string ArrivedAttributeName = "arrived";

    /// <summary>Names the element carrying the message's subject, absent where it carried none.</summary>
    internal const string SubjectElementName = "subject";

    /// <summary>Names the element one passage is quoted in.</summary>
    internal const string PassageElementName = "passage";

    /// <summary>Names the attribute carrying the position a passage is cited by.</summary>
    internal const string PositionAttributeName = "position";

    /// <summary>Writes the message, stopping at the passage the document's budget ran out in.</summary>
    /// <param name="document">The document the message is written into.</param>
    /// <param name="subject">The guarded subject, or <see langword="null" /> where the message carried none.</param>
    /// <param name="arrivedAt">When the message arrived, or <see langword="null" /> where that is unknown.</param>
    /// <param name="provenance">Where the message came from, written on its element, or <see langword="null" /> for a derivation whose turn does not carry it yet.</param>
    /// <param name="passages">The guarded passages, positioned from zero in the order given.</param>
    internal static void Write(
        AgentTurnDocument document,
        string? subject,
        DateTimeOffset? arrivedAt,
        EmailProvenance? provenance,
        IReadOnlyList<string> passages)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(passages);

        document.StartElement(MessageElementName);

        if (arrivedAt is { } arrived)
        {
            document.WriteAttribute(ArrivedAttributeName, arrived.ToString("O", CultureInfo.InvariantCulture));
        }

        if (provenance is not null)
        {
            document.WriteProvenance(provenance);
        }

        if (subject is not null)
        {
            document.WriteQuoted(SubjectElementName, subject);
        }

        foreach (var (passage, position) in passages.Select(static (passage, position) => (passage, position)))
        {
            if (document.IsExhausted)
            {
                break;
            }

            document.StartElement(PassageElementName);
            document.WriteAttribute(PositionAttributeName, position.ToString(CultureInfo.InvariantCulture));
            document.WriteQuotedContent(passage);
            document.EndElement();
        }

        document.EndElement();
    }
}
