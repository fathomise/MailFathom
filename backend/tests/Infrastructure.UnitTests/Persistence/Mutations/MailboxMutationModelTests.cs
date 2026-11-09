// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Mutations;

/// <summary>Asserts how a mutation's stored values read back, through the model the real provider builds with no connection opened.</summary>
public sealed class MailboxMutationModelTests
{
    /// <summary>A record an older build wrote, or one still in flight across an upgrade, reads as what the removed value now means.</summary>
    [Theory]
    [InlineData("RetainLocalCopy", AuthoredDeleteEmailDisposition.RetainTombstone)]
    [InlineData("RetainTombstone", AuthoredDeleteEmailDisposition.RetainTombstone)]
    [InlineData("EraseLocalCopy", AuthoredDeleteEmailDisposition.EraseLocalCopy)]
    public void LocalDisposition_AStoredName_ReadsAsTheValueThisBuildTakes(string stored, AuthoredDeleteEmailDisposition expected)
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
        var converter = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(MailboxMutationEntity))!
            .FindProperty(nameof(MailboxMutationEntity.LocalDisposition))!
            .GetValueConverter()!;

        // Act
        var read = converter.ConvertFromProvider(stored);

        // Assert
        Assert.Equal(expected, Assert.IsType<AuthoredDeleteEmailDisposition>(read));
    }
}
