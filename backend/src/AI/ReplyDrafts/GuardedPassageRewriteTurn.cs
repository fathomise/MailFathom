// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.AI.ReplyDrafts;

/// <summary>One passage rewriting with everything the deployment withholds already taken out of every text of it.</summary>
/// <param name="Stretches">
/// The guarded text around the passages: before the first, between each two, and after the last, so there is always one
/// more stretch than there are passages, and a stretch may be empty.
/// </param>
/// <param name="Passages">The guarded passages, in the order they stand in the draft.</param>
/// <param name="Instruction">The guarded ask.</param>
/// <remarks>
/// The draft is held cut at its passages rather than as one text with offsets, because the guard may replace what it finds
/// with text of another length: each piece guarded on its own keeps every passage where it stood, which a guarded whole
/// read by its original offsets would not.
/// </remarks>
internal sealed record GuardedPassageRewriteTurn(
    IReadOnlyList<string> Stretches,
    IReadOnlyList<string> Passages,
    string Instruction);
