// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Synchronization;

/// <summary>A message MailFathom filed itself, and the folder its binding placed it in.</summary>
/// <param name="Email">The stored message.</param>
/// <param name="Folder">The folder the stored message is in, which is what a client is told changed.</param>
public sealed record StoredFiledEmail(StoredEmailId Email, MailFolderId Folder);
