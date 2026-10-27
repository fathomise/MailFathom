// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Entities;

/// <summary>
/// Asserts the shape of the model the known authentication providers and their change record are generated from. The
/// model is built in memory by the real PostgreSQL provider and no connection is opened.
/// </summary>
public sealed class KnownAuthenticationProviderModelTests
{
    /// <summary>Two replicas adding one host at once both insert, and only this index decides that one of them writes nothing.</summary>
    [Fact]
    public void KnownAuthenticationProviderModel_TheHostIndex_HoldsAHostToOneEntry()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        // Act
        var index = Assert.Single(
            EntityType<KnownAuthenticationProviderEntity>(context).GetIndexes(),
            candidate => candidate.GetDatabaseName() == PersistenceConstraintNames.KnownAuthenticationProviderHostUniqueIndexName);

        // Assert
        Assert.True(index.IsUnique);
        Assert.Equal([nameof(KnownAuthenticationProviderEntity.ImapHost)], index.Properties.Select(property => property.Name));
    }

    /// <summary>The change record hangs on nothing, so removing an entry leaves the record of its removal behind.</summary>
    [Fact]
    public void KnownAuthenticationProviderChangeModel_TheRecord_ReferencesNoEntryAndRecordsOnlyTheTwoActs()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
        var changes = EntityType<KnownAuthenticationProviderChangeEntity>(context);

        // Act
        var act = Assert.Single(
            changes.GetCheckConstraints(),
            constraint => constraint.Name == PersistenceConstraintNames.KnownAuthenticationProviderChangeActCheckConstraintName);

        // Assert
        Assert.Empty(changes.GetForeignKeys());
        Assert.Equal("\"Act\" IN ('added', 'removed')", act.Sql);
    }

    private static IEntityType EntityType<TEntity>(MailFathomDbContext context) =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(TEntity))!;
}
