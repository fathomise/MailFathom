// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MailFathom.Infrastructure.Persistence.Embeddings;

/// <summary>Composes the one statement the vectors of one provider call are written by.</summary>
/// <remarks>
/// <para>
/// One statement rather than a read and an insert, because two writers reach one passage in the ordinary course: the
/// live path embeds a message the moment its passages are cut, and the sweep reaches the same message whenever it walks
/// past it before the live path has committed. A read inside each writer's own transaction sees none of the other's
/// rows, so a read-then-insert collides on the key at commit, and EF Core reports every such collision at
/// <c>Error</c> twice before any caller can classify it. The conflict clause makes the losing writer's row an update of
/// the winner's instead — the same vector of the same passage under the same profile — so nothing collides and nothing
/// is logged, while a genuine failure of the write, a width the check constraint refuses or a profile that does not
/// exist, still fails as it always did.
/// </para>
/// <para>
/// It is composed here rather than inside the store so what it says is readable without a database. Every identifier
/// comes from the model and the conflict target is the key's own name, so a renamed column or key stops the build
/// rather than producing a statement PostgreSQL refuses; every value is a parameter.
/// </para>
/// </remarks>
internal static class EmailEmbeddingUpsertStatement
{
    /// <summary>How many parameters every row of the statement shares, ahead of the rows' own.</summary>
    private const int SharedParameterCount = 3;

    /// <summary>How many parameters one row carries: its passage and its vector.</summary>
    private const int ParametersPerRow = 2;

    /// <summary>Composes the statement for a given number of vectors.</summary>
    /// <param name="model">The model the schema is generated from.</param>
    /// <param name="rowCount">How many passage and vector pairs the statement writes.</param>
    /// <returns>
    /// The statement, carrying <c>{0}</c> for the profile, <c>{1}</c> for its dimension, and <c>{2}</c> for the instant,
    /// followed by each row's passage and vector in that order: <c>{3}</c> and <c>{4}</c> for the first, <c>{5}</c> and
    /// <c>{6}</c> for the second, and so on.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="model" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="rowCount" /> is not positive.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the model does not map the vector row.</exception>
    public static string Compose(IModel model, int rowCount)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rowCount);

        var embeddings = PersistedSchemaNames.EntityTypeOf<EmailEmbeddingEntity>(model);

        var chunkColumn = PersistedSchemaNames.QuotedColumn(embeddings, nameof(EmailEmbeddingEntity.EmailChunkId));
        var profileColumn = PersistedSchemaNames.QuotedColumn(embeddings, nameof(EmailEmbeddingEntity.EmbeddingProfileId));
        var dimensionColumn = PersistedSchemaNames.QuotedColumn(embeddings, nameof(EmailEmbeddingEntity.Dimension));
        var embeddingColumn = PersistedSchemaNames.QuotedColumn(embeddings, nameof(EmailEmbeddingEntity.Embedding));
        var generatedColumn = PersistedSchemaNames.QuotedColumn(embeddings, nameof(EmailEmbeddingEntity.GeneratedAt));

        var rows = string.Join(
            ",\n    ",
            Enumerable.Range(0, rowCount).Select(DescribeRow));

        return $$"""
            INSERT INTO {{PersistedSchemaNames.QuotedTable(embeddings)}}
                ({{chunkColumn}}, {{profileColumn}}, {{dimensionColumn}}, {{embeddingColumn}}, {{generatedColumn}})
            VALUES
                {{rows}}
            ON CONFLICT ON CONSTRAINT "{{PersistenceConstraintNames.EmailEmbeddingPrimaryKeyConstraintName}}" DO UPDATE SET
                {{dimensionColumn}} = EXCLUDED.{{dimensionColumn}},
                {{embeddingColumn}} = EXCLUDED.{{embeddingColumn}},
                {{generatedColumn}} = EXCLUDED.{{generatedColumn}}
            """;
    }

    private static string DescribeRow(int row)
    {
        var chunkParameter = SharedParameterCount + (row * ParametersPerRow);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"({{{chunkParameter}}}, {{0}}, {{1}}, {{{chunkParameter + 1}}}, {{2}})");
    }
}
