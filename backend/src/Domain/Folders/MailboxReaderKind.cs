// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Folders;

/// <summary>Names who a read of the mailbox is answering for, which decides the folders it may reach.</summary>
/// <remarks>
/// <para>
/// A folder withheld from AI is still the person's folder: they read it in the client like any other, while no agent
/// and no model reaches it. One folder therefore has two answers, and every read has to say which one it is asking for
/// rather than leaving a read model to guess from where it happens to be called.
/// </para>
/// <para>
/// <see cref="Agent" /> is the zero value on purpose. A request type that forgot to state its reader reads as an
/// agent's, which withholds the folder, rather than as the person's, which would hand it to a model.
/// </para>
/// </remarks>
public enum MailboxReaderKind
{
    /// <summary>An agent, an MCP caller, or anything else whose answer reaches a model: the folders visible to AI.</summary>
    Agent = 0,

    /// <summary>The person reading their own mail through the client: every folder MailFathom mirrors.</summary>
    Person = 1,
}
