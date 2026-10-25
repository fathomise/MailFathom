// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Embeddings;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Embeddings;

/// <summary>
/// The vectors of one provider call are written by one statement, and what happens to the writer that loses a race over
/// a passage is in its text: its row becomes an update of the winner's rather than a violation of the key, which is
/// what keeps the race out of the error log. None of that is visible from the port and all of it is decidable without a
/// server, so it is established here.
/// </summary>
public sealed class EmailEmbeddingUpsertStatementTests
{
    /// <summary>The live path and the sweep reaching one passage at once is the race the key used to fail on, so the loser's row replaces the winner's instead.</summary>
    [Fact]
    public void Compose_ALosingWriter_ReplacesTheWinnersVectorOnTheKeyRatherThanViolatingIt()
    {
        // Act
        var statement = Composed(rowCount: 1);

        // Assert
        Assert.Contains(
            "ON CONFLICT ON CONSTRAINT \"pk_email_embeddings\" DO UPDATE SET",
            statement,
            StringComparison.Ordinal);
        Assert.Contains("\"Embedding\" = EXCLUDED.\"Embedding\"", statement, StringComparison.Ordinal);
        Assert.Contains("\"Dimension\" = EXCLUDED.\"Dimension\"", statement, StringComparison.Ordinal);
        Assert.Contains("\"GeneratedAt\" = EXCLUDED.\"GeneratedAt\"", statement, StringComparison.Ordinal);
    }

    /// <summary>The conflict clause names the key alone, so a width the check constraint refuses or a profile that does not exist still fails the write.</summary>
    [Fact]
    public void Compose_TheWrite_AbsorbsNoConflictButTheKeys()
    {
        // Act
        var statement = Composed(rowCount: 1);

        // Assert
        Assert.DoesNotContain("DO NOTHING", statement, StringComparison.Ordinal);
        Assert.Single(statement.Split("ON CONFLICT", StringSplitOptions.None).Skip(1));
    }

    /// <summary>Every row shares the profile, its width, and the instant, and carries its own passage and vector after them.</summary>
    [Fact]
    public void Compose_SeveralVectors_WritesOneRowPerVectorOverTheSharedParameters()
    {
        // Act
        var statement = Composed(rowCount: 3);

        // Assert
        Assert.Contains(
            "VALUES\n    ({3}, {0}, {1}, {4}, {2}),\n    ({5}, {0}, {1}, {6}, {2}),\n    ({7}, {0}, {1}, {8}, {2})\n",
            statement.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
    }

    /// <summary>Every identifier comes from the model, so the table this writes is the one the mapping declares.</summary>
    [Fact]
    public void Compose_TheWrite_NamesTheTableAndColumnsTheModelStates()
    {
        // Act
        var statement = Composed(rowCount: 1);

        // Assert
        Assert.Contains(
            "INSERT INTO \"email_embeddings\"\n    (\"EmailChunkId\", \"EmbeddingProfileId\", \"Dimension\", \"Embedding\", \"GeneratedAt\")",
            statement.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_NoRowsAtAll_IsRefused()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => EmailEmbeddingUpsertStatement.Compose(context.Model, 0));
    }

    [Fact]
    public void Compose_NoModelAtAll_IsRefused()
    {
        // Assert
        Assert.Throws<ArgumentNullException>(() => EmailEmbeddingUpsertStatement.Compose(null!, 1));
    }

    private static string Composed(int rowCount)
    {
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        return EmailEmbeddingUpsertStatement.Compose(context.Model, rowCount);
    }
}
