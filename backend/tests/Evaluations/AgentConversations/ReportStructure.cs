// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Evaluations.AgentConversations;

/// <summary>A structure of Markdown a case asks a report to be set in.</summary>
internal enum ReportStructure
{
    /// <summary>A pipe table.</summary>
    Table = 0,

    /// <summary>A list with a second level under one of its items.</summary>
    NestedList = 1,

    /// <summary>A quotation.</summary>
    Quotation = 2,
}
