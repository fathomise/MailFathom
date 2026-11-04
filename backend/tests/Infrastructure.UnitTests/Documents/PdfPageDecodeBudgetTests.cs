// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.Infrastructure.Documents;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Documents;

/// <summary>Covers how a PDF page's decode allowance counts what is charged against it.</summary>
public sealed class PdfPageDecodeBudgetTests
{
    /// <summary>A charge read off a declaration can be as large as a long holds, which must not wrap the count back under the allowance.</summary>
    [Fact]
    public void Consume_AChargeThatWouldOverflowTheCount_RefusesThePage()
    {
        // Arrange
        var budget = new PdfPageDecodeBudget(64 * 1024);

        budget.Consume(1);

        // Act
        var refusal = Assert.Throws<AttachmentTextExtractionStoppedException>(() => budget.Consume(long.MaxValue));

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.ContainerBoundExceeded, refusal.Outcome);
    }
}
