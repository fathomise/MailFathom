// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Agent.Reports;
using MailFathom.Infrastructure.Documents.Reports;

namespace MailFathom.Evaluations.AgentConversations;

/// <summary>Sets every report the run writes with the deployment's own typesetter, and keeps what it was handed so a case can read the report the model wrote.</summary>
internal sealed class RecordingReportTypesetter : IAgentReportTypesetter
{
    private readonly MigraDocReportTypesetter typesetter = new();
    private readonly List<AgentReport> set = [];

    /// <summary>Gets every report set, in the order the run wrote them, each with the sources the tool listed and the citations it numbered.</summary>
    public IReadOnlyList<AgentReport> Set => this.set;

    /// <inheritdoc />
    public AgentReportDocument Typeset(AgentReport report)
    {
        var document = this.typesetter.Typeset(report);
        this.set.Add(report);

        return document;
    }
}
