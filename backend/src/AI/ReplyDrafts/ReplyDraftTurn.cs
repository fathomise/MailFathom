// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.ReplyDrafts;

namespace MailFathom.AI.ReplyDrafts;

/// <summary>One drafting's turn as it is sent, beside the links it actually numbered.</summary>
/// <param name="Text">The turn.</param>
/// <param name="Links">
/// The leading part of the brief's links the turn wrote. A bounded turn may have written fewer or none, and a number the
/// model was never shown is not one a draft may cite.
/// </param>
internal sealed record ReplyDraftTurn(string Text, IReadOnlyList<ReplyDraftLink> Links);
