// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using MailFathom.Application.Preferences;
using Xunit;

namespace MailFathom.Application.UnitTests.Preferences;

/// <summary>
/// Covers the message view as the published identity it is: the name a client sends and a response answers with. What
/// these hold is that the set is exactly the two renderings the design offers, that a name outside it — a withdrawn
/// rendering included — is refused rather than reconstructed, and that the unusable struct default is never written.
/// </summary>
public sealed class ClientMessageViewTests
{
    [Fact]
    public void All_ThePublishedSet_IsTheReducedTreeAndTheSendersOwnMarkup()
    {
        // Act
        var names = ClientMessageView.All.Select(view => view.Name);

        // Assert
        Assert.Equal(["reduced", "embeddedHtml"], names);
    }

    [Theory]
    [InlineData("reduced")]
    [InlineData("embeddedHtml")]
    public void TryParse_ANameThisBuildPublishes_ReportsTheViewItNames(string name)
    {
        // Act
        var parsed = ClientMessageView.TryParse(name, out var view);

        // Assert
        Assert.True(parsed);
        Assert.Equal(name, view.Name);
    }

    [Theory]
    [InlineData("cleaned")]
    [InlineData("Reduced")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_ANameNothingPublishes_ReportsTheUnspecifiedDefault(string? name)
    {
        // Act
        var parsed = ClientMessageView.TryParse(name, out var view);

        // Assert
        Assert.False(parsed);
        Assert.False(view.IsSpecified);
    }

    [Fact]
    public void Read_ASerializedView_ReadsBackTheViewThatWasWritten()
    {
        // Act
        var json = JsonSerializer.Serialize(ClientMessageView.EmbeddedHtml);
        var read = JsonSerializer.Deserialize<ClientMessageView>(json);

        // Assert
        Assert.Equal(("\"embeddedHtml\"", ClientMessageView.EmbeddedHtml), (json, read));
    }

    [Fact]
    public void ReadAsPropertyName_ADictionaryKeyedByView_ReadsBackTheKeysThatWereWritten()
    {
        // Arrange
        var keyed = new Dictionary<ClientMessageView, int> { [ClientMessageView.Reduced] = 1 };

        // Act
        var json = JsonSerializer.Serialize(keyed);
        var read = JsonSerializer.Deserialize<Dictionary<ClientMessageView, int>>(json);

        // Assert
        Assert.Equal("{\"reduced\":1}", json);
        Assert.Equal([ClientMessageView.Reduced], read?.Keys);
    }

    [Theory]
    [InlineData("\"cleaned\"")]
    [InlineData("7")]
    public void Read_AValueThatNamesNoPublishedView_RefusesRatherThanReconstructingOne(string json)
    {
        // Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ClientMessageView>(json));
    }

    [Fact]
    public void Write_TheStructDefault_RefusesRatherThanWritingAnEmptyValue()
    {
        // Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(default(ClientMessageView)));
    }
}
