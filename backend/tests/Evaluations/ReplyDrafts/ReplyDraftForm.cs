// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Evaluations.ReplyDrafts;

/// <summary>What shape a case holds a draft to, read off the blocks the deployment kept.</summary>
internal enum ReplyDraftForm
{
    /// <summary>Paragraphs of plain words: no list, no table, no emphasis, no colour, and no alignment.</summary>
    Plain = 0,

    /// <summary>A table and a list, because the person asked for both.</summary>
    TableAndList = 1,

    /// <summary>Some structure — a list, a table, or a bold lead-in — on a long reply nobody asked to be formatted.</summary>
    Structured = 2,
}
