// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Contacts;

/// <summary>Which surface wrote an address into a contact, which decides whether the address can make its sender trusted.</summary>
/// <remarks>
/// <para>
/// A person and an administrator stating an address are deciding who they know, so their addresses count. An agent's
/// never does, under any grant: a model reads mail an attacker wrote, and a message telling it to add an address must
/// not make that address's next message trusted. No permission could tell a legitimate agent write from an injected one.
/// </para>
/// <para>
/// <see cref="Unrecorded" /> is what an address carries where nobody recorded its writer: every address stored before
/// the writer was, some of which an agent wrote, and every address collection recorded. It counts no more than an
/// agent's, until a person or an administrator states it again.
/// </para>
/// </remarks>
public enum ContactAddressWriterKind
{
    /// <summary>Nobody recorded who wrote the address, so it grants nothing.</summary>
    Unrecorded = 0,

    /// <summary>The person themselves, through the client endpoint.</summary>
    Person = 1,

    /// <summary>An administrator, through the administrative endpoint or <c>mfctl</c>.</summary>
    Administrator = 2,

    /// <summary>An agent acting for the person over MCP, whose address never grants trust.</summary>
    Agent = 3,
}
