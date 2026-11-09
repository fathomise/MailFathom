// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel;
using System.Text.Json;
using MailFathom.AI.Orchestration;
using Microsoft.Extensions.AI;
using Xunit;

namespace MailFathom.AI.UnitTests.Orchestration;

/// <summary>Covers how a tool an agent is offered answers a call its schema does not describe.</summary>
/// <remarks>
/// Each call carries its values as JSON elements, which is how the tool loop hands over what a model wrote, so the
/// binding the tool meets here is the one it meets in a run.
/// </remarks>
public sealed class SchemaEnforcingFunctionTests
{
    private const string ToolName = "find_letters";

    private readonly List<string> calls = [];

    /// <summary>
    /// A key the tool does not publish is refused rather than dropped, so the model learns the filter it meant was never
    /// applied. The second key is one a model really wrote, its own channel tokens leaked into the arguments.
    /// </summary>
    [Theory]
    [InlineData("receivedOnOrBefore")]
    [InlineData("}აუდios? ♀♀♀♀♀♀} to=functions.search_mail⟨commentary 代json⟩{")]
    public async Task InvokeAsync_AnArgumentOutsideTheSchema_IsRefusedNamingItAndTheValidOnesWithoutRunningTheTool(string unknownName)
    {
        // Arrange
        var tool = this.EnforcedTool();

        // Act
        var answer = await InvokeAsync(tool, ("queryText", "\"invoice\""), (unknownName, "\"2026-09-14T12:00\""));

        // Assert
        Assert.Contains($"\"{JsonEncodedText.Encode(unknownName)}\"", answer, StringComparison.Ordinal);
        Assert.Contains("Its arguments are queryText, senderAddress, isMarkedSeen.", answer, StringComparison.Ordinal);
        Assert.Empty(this.calls);
    }

    /// <summary>A name longer than any argument is quoted only as far as the bound, because it is the model's own unbounded text.</summary>
    [Fact]
    public async Task InvokeAsync_AnUnknownNamePastTheBound_IsQuotedCutToIt()
    {
        // Arrange
        var tool = this.EnforcedTool();
        var unknownName = new string('x', SchemaEnforcingFunction.MaximumQuotedNameLength * 2);

        // Act
        var answer = await InvokeAsync(tool, ("queryText", "\"invoice\""), (unknownName, "1"));

        // Assert
        Assert.Contains($"\"{new string('x', SchemaEnforcingFunction.MaximumQuotedNameLength)}\"", answer, StringComparison.Ordinal);
        Assert.DoesNotContain(unknownName, answer, StringComparison.Ordinal);
    }

    /// <summary>A required argument left out is refused naming it, rather than failing the call opaquely.</summary>
    [Fact]
    public async Task InvokeAsync_ARequiredArgumentLeftOut_IsRefusedNamingItWithoutRunningTheTool()
    {
        // Arrange
        var tool = this.EnforcedTool();

        // Act
        var answer = await InvokeAsync(tool, ("senderAddress", "\"anna@example.test\""));

        // Assert
        Assert.StartsWith($"{ToolName} needs the argument \"queryText\".", answer, StringComparison.Ordinal);
        Assert.Empty(this.calls);
    }

    /// <summary>A value the binding cannot read as its parameter's type comes back as a refusal the model can correct, not as a failed call.</summary>
    [Fact]
    public async Task InvokeAsync_AValueTheBindingCannotRead_IsRefusedListingTheArguments()
    {
        // Arrange
        var tool = this.EnforcedTool();

        // Act
        var answer = await InvokeAsync(tool, ("queryText", "\"invoice\""), ("isMarkedSeen", "\"maybe\""));

        // Assert
        Assert.StartsWith($"An argument to {ToolName} could not be read as the type its schema gives it.", answer, StringComparison.Ordinal);
        Assert.Empty(this.calls);
    }

    /// <summary>A call the schema describes runs the tool exactly as it would have run unwrapped.</summary>
    [Fact]
    public async Task InvokeAsync_ArgumentsTheSchemaDescribes_RunsTheTool()
    {
        // Arrange
        var tool = this.EnforcedTool();

        // Act
        var answer = await InvokeAsync(tool, ("queryText", "\"invoice\""), ("isMarkedSeen", "true"));

        // Assert
        Assert.Equal("found invoice", answer);
        Assert.Equal(["invoice"], this.calls);
    }

    /// <summary>What a model is offered is the tool's own: the wrapper changes how a call is answered, never what is published.</summary>
    [Fact]
    public void Enforcing_AFunction_PublishesItsOwnNameDescriptionAndSchema()
    {
        // Arrange
        var inner = this.Tool();

        // Act
        var enforced = Assert.IsType<AIFunction>(SchemaEnforcingFunction.Enforcing(inner), exactMatch: false);

        // Assert
        Assert.Equal(inner.Name, enforced.Name);
        Assert.Equal(inner.Description, enforced.Description);
        Assert.Equal(inner.JsonSchema.GetRawText(), enforced.JsonSchema.GetRawText());
    }

    private static async Task<string> InvokeAsync(AIFunction tool, params (string Name, string Json)[] arguments)
    {
        var result = await tool.InvokeAsync(
            new AIFunctionArguments(arguments.ToDictionary(
                static argument => argument.Name,
                static argument => (object?)JsonSerializer.Deserialize<JsonElement>(argument.Json))),
            TestContext.Current.CancellationToken);

        return result switch
        {
            JsonElement element => element.GetString() ?? element.ToString(),
            string text => text,
            _ => result?.ToString() ?? string.Empty,
        };
    }

    private AIFunction EnforcedTool() =>
        Assert.IsType<AIFunction>(SchemaEnforcingFunction.Enforcing(this.Tool()), exactMatch: false);

    private AIFunction Tool() =>
        AIFunctionFactory.Create(this.FindLetters, ToolName, "Finds letters.");

    [Description("Finds letters.")]
    private string FindLetters(
        [Description("The words to look for.")] string queryText,
        [Description("Only letters from this address.")] string? senderAddress = null,
        [Description("Only read or unread letters.")] bool? isMarkedSeen = null)
    {
        _ = senderAddress;
        _ = isMarkedSeen;

        this.calls.Add(queryText);

        return $"found {queryText}";
    }
}
