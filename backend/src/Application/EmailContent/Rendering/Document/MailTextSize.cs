// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.EmailContent.Rendering.Document;

/// <summary>Which step of the closed size scale a run of text is drawn at.</summary>
/// <remarks>
/// <para>
/// A step rather than a length, which is what keeps a sender's size inside the pane: a pane draws each step as a type
/// token of its own, relative to its body text, so nothing a message wrote is drawn below <see cref="Small" /> — text a
/// sender shrank to hide is still read at a size somebody can read — and nothing above <see cref="Largest" />.
/// </para>
/// <para>
/// The scale and how mail's own spellings of a size map onto it are
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0047-what-the-composer-may-write-beyond-its-closed-set-and-how-it-reads-a-formatted-paste.md">ADR 0047</see>'s,
/// which states it once for the reduction and the composer alike.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<MailTextSize>))]
public enum MailTextSize
{
    /// <summary>The message said nothing, or asked for the size body text is read at, so the pane's own body size decides.</summary>
    Normal = 0,

    /// <summary>Small print.</summary>
    Small = 1,

    /// <summary>One step above body text.</summary>
    Large = 2,

    /// <summary>Two steps above body text.</summary>
    Larger = 3,

    /// <summary>A headline.</summary>
    Largest = 4,
}
