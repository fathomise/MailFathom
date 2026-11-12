// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Agent.Reports;

/// <summary>What a report's source is.</summary>
public enum AgentReportSourceKind
{
    /// <summary>A message the run read.</summary>
    Message = 0,

    /// <summary>A file the person attached to the question the report answers.</summary>
    AttachedFile = 1,
}
