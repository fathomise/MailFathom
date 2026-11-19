// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Tasks;

/// <summary>Says whose move a task is waiting on.</summary>
public enum PersonalTaskDirection
{
    /// <summary>The person owes it: they said they would do it, or somebody asked them to.</summary>
    Owed = 0,

    /// <summary>The person waits for it: somebody else owes them an answer or a deed.</summary>
    Awaited = 1,
}
