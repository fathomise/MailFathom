// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using MailFathom.Application.Preferences;
using Xunit;

namespace MailFathom.Application.UnitTests.Preferences;

/// <summary>
/// Covers the list mode as the published identity it is: the name a client sends, a response answers with, and the
/// stored row holds. What these hold is that the set is exactly the two the design offers, that a name outside it is
/// refused rather than reconstructed, and that the unusable struct default reaches neither a response nor a document.
/// </summary>
public sealed class ClientMailListModeTests
{
    [Fact]
    public void All_ThePublishedSet_IsReadingByMessageAndByConversation()
    {
        // Act
        var names = ClientMailListMode.All.Select(mode => mode.Name);

        // Assert
        Assert.Equal(["messages", "threads"], names);
    }

    [Fact]
    public void Unset_TheListModeAPersonWhoChoseNothingReads_IsOneMessagePerRow()
    {
        // Assert
        Assert.Equal(ClientMailListMode.Messages, ClientPreferences.Unset.ListMode);
    }

    [Theory]
    [InlineData("messages")]
    [InlineData("threads")]
    public void TryParse_ANameThisBuildPublishes_ReportsTheModeItNames(string name)
    {
        // Act
        var parsed = ClientMailListMode.TryParse(name, out var mode);

        // Assert
        Assert.True(parsed);
        Assert.Equal(name, mode.Name);
    }

    [Theory]
    [InlineData("Threads")]
    [InlineData("conversations")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_ANameNothingPublishes_ReportsTheUnspecifiedDefault(string? name)
    {
        // Act
        var parsed = ClientMailListMode.TryParse(name, out var mode);

        // Assert
        Assert.False(parsed);
        Assert.False(mode.IsSpecified);
    }

    [Fact]
    public void Name_TheStructDefault_RefusesToAnswerForOne()
    {
        // Assert
        Assert.Throws<InvalidOperationException>(() => default(ClientMailListMode).Name);
    }

    [Fact]
    public void ToString_EitherValue_ReadsAsItsNameOrAsUnspecified()
    {
        // Assert
        Assert.Equal(("threads", "(unspecified)"), (ClientMailListMode.Threads.ToString(), default(ClientMailListMode).ToString()));
    }

    [Fact]
    public void Read_ASerializedMode_ReadsBackTheModeThatWasWritten()
    {
        // Act
        var json = JsonSerializer.Serialize(ClientMailListMode.Threads);
        var read = JsonSerializer.Deserialize<ClientMailListMode>(json);

        // Assert
        Assert.Equal(("\"threads\"", ClientMailListMode.Threads), (json, read));
    }

    [Theory]
    [InlineData("\"conversations\"")]
    [InlineData("7")]
    public void Read_AValueThatNamesNoPublishedMode_RefusesRatherThanReconstructingOne(string json)
    {
        // Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ClientMailListMode>(json));
    }

    [Fact]
    public void Write_TheStructDefault_RefusesRatherThanWritingAnEmptyValue()
    {
        // Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(default(ClientMailListMode)));
    }
}
