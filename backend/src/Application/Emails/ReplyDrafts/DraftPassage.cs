// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Emails.ReplyDrafts;

/// <summary>One stretch of a draft somebody asked to have rewritten, named by where it stands in the draft's text.</summary>
/// <param name="Start">The offset of its first character, counted in UTF-16 code units from the start of the draft.</param>
/// <param name="Length">How many UTF-16 code units it spans.</param>
/// <remarks>
/// Offsets in UTF-16 code units because that is how a browser's string counts them, so the client names a stretch by the
/// same indices it selected it with and nothing is converted on the way. Whether a passage lies inside its draft is a
/// question about both, which <see cref="PassageRewriteRequest" /> answers.
/// </remarks>
public readonly record struct DraftPassage(int Start, int Length);
