// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Evaluations.AgentConversations;
using Xunit;

namespace MailFathom.Evaluations.UnitTests.AgentConversations;

public sealed class ReportFindingsTests
{
    /// <summary>A table is told by its pipe-delimited separator, so dashes alone — a thematic break or a heading's underline — are not one.</summary>
    [Theory]
    [InlineData("| Supplier | Total |\n|---|---:|\n| Brackenford | 18,940.00 EUR |", true)]
    [InlineData("Supplier | Total\n--- | ---\nBrackenford | 18,940.00 EUR", true)]
    [InlineData("Totals\n\n---\n\nBrackenford invoiced 18,940.00 EUR.", false)]
    [InlineData("Totals\n------\n\nBrackenford invoiced 18,940.00 EUR.", false)]
    public void Holds_ATable_IsReadOffAPipeDelimitedSeparatorAlone(string body, bool holds)
    {
        // Act
        var held = ReportFindings.Holds(body, ReportStructure.Table);

        // Assert
        Assert.Equal(holds, held);
    }
}
