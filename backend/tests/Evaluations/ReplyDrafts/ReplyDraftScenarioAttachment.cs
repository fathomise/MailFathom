// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Evaluations.ReplyDrafts;

/// <summary>An attachment a drafting case writes onto the message the person chose, as the words extraction would have read out of it.</summary>
/// <param name="FileName">The file name the attachment carries.</param>
/// <param name="Text">The words it yielded, which name nobody real.</param>
internal sealed record ReplyDraftScenarioAttachment(string FileName, string Text);
