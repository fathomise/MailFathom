// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Orchestration;
using Xunit;

namespace MailFathom.AI.UnitTests.Orchestration;

/// <summary>Covers the builders every agent's strict schema is written with, where a provider's own limits decide the shape.</summary>
public sealed class StrictJsonSchemaTests
{
    [Fact]
    public void ObjectOf_AnyProperties_RequiresEveryOneAndAdmitsNoOther()
    {
        // Act
        var schema = StrictJsonSchema.ObjectOf(new() { ["text"] = StrictJsonSchema.StringType(), ["done"] = StrictJsonSchema.BooleanType() });

        // Assert
        Assert.Equal(["text", "done"], schema["required"]!.AsArray().Select(static name => name!.GetValue<string>()));
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
    }

    [Fact]
    public void NumberOf_TheNumbersATurnPublished_EnumeratesExactlyThose()
    {
        // Act
        var schema = StrictJsonSchema.NumberOf([0, 2, 5]);

        // Assert
        Assert.Equal([0, 2, 5], schema["enum"]!.AsArray().Select(static number => number!.GetValue<int>()));
    }

    /// <summary>No validator accepts an empty enum and a provider caps a long one, so both ask for a plain integer the reading bounds.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(StrictJsonSchema.MaximumEnumeratedNumbers + 1)]
    public void PositionOf_NoPositionsOrMoreThanOneSchemaEnumerates_FallsBackToAPlainInteger(int count)
    {
        // Act
        var schema = StrictJsonSchema.PositionOf(count);

        // Assert
        Assert.Equal("integer", schema["type"]!.GetValue<string>());
        Assert.False(schema.ContainsKey("enum"));
    }

    [Fact]
    public void PositionOf_AsManyPositionsAsOneSchemaEnumerates_StillEnumeratesThem()
    {
        // Act
        var schema = StrictJsonSchema.PositionOf(StrictJsonSchema.MaximumEnumeratedNumbers);

        // Assert
        Assert.Equal(StrictJsonSchema.MaximumEnumeratedNumbers, schema["enum"]!.AsArray().Count);
    }
}
