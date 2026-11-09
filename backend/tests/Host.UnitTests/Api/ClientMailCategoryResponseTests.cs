// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Host.Api;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers how a category a message shows is described for the wire.</summary>
public sealed class ClientMailCategoryResponseTests
{
    /// <summary>A default travels by its identity alone, for a client to name, and an own category carries the name it was given, in the order given.</summary>
    [Fact]
    public void For_BothKinds_NamesOnlyTheOwnCategory()
    {
        // Act
        var response = ClientMailCategoryResponse.For(["finance", "Legal"]);

        // Assert
        Assert.Equal([new ClientMailCategoryResponse("finance", Name: null), new ClientMailCategoryResponse("Legal", "Legal")], response);
    }

    /// <summary>Nothing shown is an empty list, which a row draws as nothing at all.</summary>
    [Fact]
    public void For_NothingShown_IsEmpty()
    {
        // Act
        var response = ClientMailCategoryResponse.For([]);

        // Assert
        Assert.Empty(response);
    }
}
