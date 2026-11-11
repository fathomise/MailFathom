// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.ReplyDrafts;
using Xunit;

namespace MailFathom.AI.UnitTests.ReplyDrafts;

/// <summary>Covers the schema a rewriting asks a model for: that strict mode accepts it, and that what it admits reads into a rewriting.</summary>
public sealed class PassageRewriteSchemaTests
{
    /// <summary>Strict mode refuses an object that leaves a member optional or admits another, so the one object states both.</summary>
    [Fact]
    public void ResponseFormat_Always_RequiresAnArrayOfStringsAndAdmitsNothingElse()
    {
        // Act
        var format = PassageRewriteSchema.ResponseFormat();

        // Assert
        var schema = format.Schema!.Value;
        var passages = schema.GetProperty("properties").GetProperty("passages");

        Assert.Equal(PassageRewriteSchema.Name, format.SchemaName);
        Assert.Equal(["passages"], schema.GetProperty("required").EnumerateArray().Select(static name => name.GetString()));
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("array", passages.GetProperty("type").GetString());
        Assert.Equal("string", passages.GetProperty("items").GetProperty("type").GetString());
    }

    /// <summary>An answer written to the schema is one the reading takes whole, so the two cannot drift apart unnoticed.</summary>
    [Fact]
    public void Read_AnAnswerWrittenToTheSchema_ReadsAsARewriting()
    {
        // Act
        var rewrite = PassageRewriteReading.Read("""{"passages": ["Tuesday suits us.", "Kind regards."]}""", passageCount: 2);

        // Assert
        Assert.True(rewrite.WasWritten);
    }
}
