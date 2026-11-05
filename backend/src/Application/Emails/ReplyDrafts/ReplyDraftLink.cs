// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.EmailContent.Rendering.Document;

namespace MailFathom.Application.Emails.ReplyDrafts;

/// <summary>One address a draft may link to, numbered so the drafting cites it by position rather than by address.</summary>
/// <param name="Position">The zero-based place the link holds in the list the turn publishes, which is how a run of the draft names it.</param>
/// <param name="Link">The target this deployment read, which a cited number resolves to and which never reaches a provider.</param>
/// <param name="Words">The words the correspondence linked from it, or <see langword="null" /> for an address the person typed.</param>
/// <remarks>
/// Model output is text a message being answered can steer, so a link whose address a model wrote could point any words
/// anywhere. Only these targets — each one the person typed or the correspondence carried — can become a link in a
/// draft, and a provider is shown a target's host and words, never the address itself, because an address is often a
/// credential of its own: a reset link, a signed download, an unsubscribe token.
/// </remarks>
public sealed record ReplyDraftLink(int Position, MailDocumentLink Link, string? Words);
