// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Infrastructure.Persistence.Folders;

/// <summary>One folder as its path is composed from: its identity, its parent, and its own name.</summary>
/// <param name="Id">The folder.</param>
/// <param name="ParentId">The folder it sits beneath, or <see langword="null" /> at the top of its account.</param>
/// <param name="Name">The folder's own name.</param>
internal sealed record StoredMailFolderNode(Guid Id, Guid? ParentId, string Name);
