// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.EmailContent.Rendering.Document;

namespace MailFathom.Application.Answers;

/// <summary>One link an AI answer carries, judged as a link in mail is judged, and whether the mail the answer cites carries it too.</summary>
/// <param name="Link">Where the link goes and what its own words claimed about that, judged by <see cref="MailLinkReader" />.</param>
/// <param name="AppearsInCitedSources">Whether the text of any source the answer cites carries the same target.</param>
/// <remarks>
/// A link that appears in none of the cited sources was written by the model — or by a message speaking through it, which
/// is the case worth telling a reader about. The reverse claims less than it reads as: a link the mail carries is one the
/// mail carried, and mail is written by anybody, so it is still judged and still warned about where its judgement says so.
/// </remarks>
public sealed record AnswerLink(MailDocumentLink Link, bool AppearsInCitedSources);
