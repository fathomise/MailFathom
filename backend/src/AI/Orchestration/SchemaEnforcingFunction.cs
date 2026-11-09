// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using MailFathom.Domain.Emails;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.Orchestration;

/// <summary>A tool that refuses a call whose arguments its own schema does not describe, and says which arguments it takes.</summary>
/// <remarks>
/// <para>
/// The framework's binding reads the arguments a method names and ignores every other key, so a model that wrote a
/// filter under a name the tool does not publish — <c>receivedOnOrBefore</c> for <c>receivedBefore</c>, or a fragment of
/// its own channel tokens — is answered as though it had asked without that filter, and the wrong answer arrives looking
/// like the right one. A value the binding cannot convert, or a required argument left out, fails the call instead, and
/// the loop reports that to the model as an opaque failure it cannot correct. Both are refused here with the argument
/// and the tool's own argument list, because the caller is a tool loop and a model that wrote an unusable call can write
/// a usable one.
/// </para>
/// <para>
/// It is applied by <see cref="AgentComposition" /> to every tool an agent is offered, so no tool has to repeat it and a
/// tool added later is held to its schema without anybody remembering to. The name, the description, and the schema are
/// the inner tool's own, so what a model is offered is unchanged.
/// </para>
/// <para>
/// A name the model wrote is quoted bounded and escaped rather than as written, because it is the model's own text and
/// a refusal is a document composed for a model to read.
/// </para>
/// </remarks>
internal sealed class SchemaEnforcingFunction : DelegatingAIFunction
{
    /// <summary>The most characters of an argument name the model wrote that a refusal quotes back.</summary>
    internal const int MaximumQuotedNameLength = 64;

    private readonly IReadOnlyList<string> argumentNames;
    private readonly IReadOnlyList<string> requiredNames;

    private SchemaEnforcingFunction(
        AIFunction innerFunction,
        IReadOnlyList<string> argumentNames,
        IReadOnlyList<string> requiredNames)
        : base(innerFunction)
    {
        this.argumentNames = argumentNames;
        this.requiredNames = requiredNames;
    }

    /// <summary>Holds a tool to the arguments its schema describes, where it describes any.</summary>
    /// <param name="tool">The tool an agent is about to be offered.</param>
    /// <returns>
    /// The tool held to its schema, or the tool itself where it is not a function or publishes no object of properties
    /// — there is then no list of arguments to hold a call to.
    /// </returns>
    internal static AITool Enforcing(AITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        if (tool is not AIFunction function
            || function.JsonSchema.ValueKind is not JsonValueKind.Object
            || !function.JsonSchema.TryGetProperty("properties", out var properties)
            || properties.ValueKind is not JsonValueKind.Object)
        {
            return tool;
        }

        return new SchemaEnforcingFunction(
            function,
            [.. properties.EnumerateObject().Select(static property => property.Name)],
            RequiredNamesOf(function.JsonSchema));
    }

    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        if (this.RefusalOf(arguments) is { } refusal)
        {
            return refusal;
        }

        try
        {
            return await base.InvokeCoreAsync(arguments, cancellationToken);
        }
        catch (JsonException)
        {
            return $"An argument to {this.Name} could not be read as the type its schema gives it. {this.ArgumentList()} Call it again with each value written as its schema states.";
        }
    }

    private static IReadOnlyList<string> RequiredNamesOf(JsonElement schema) =>
        schema.TryGetProperty("required", out var required) && required.ValueKind is JsonValueKind.Array
            ? [.. required.EnumerateArray().Where(static name => name.ValueKind is JsonValueKind.String).Select(static name => name.GetString()!)]
            : [];

    private static string Quoted(string name) =>
        $"\"{JsonEncodedText.Encode(MailTextBounds.TruncateAtTextElementBoundary(name, MaximumQuotedNameLength))}\"";

    private string? RefusalOf(AIFunctionArguments arguments)
    {
        if (arguments.Keys.FirstOrDefault(name => !this.argumentNames.Contains(name, StringComparer.Ordinal)) is { } unknown)
        {
            return $"{this.Name} takes no argument named {Quoted(unknown)}. {this.ArgumentList()} Call it again without that one.";
        }

        if (this.requiredNames.FirstOrDefault(name => !arguments.ContainsKey(name)) is { } missing)
        {
            return $"{this.Name} needs the argument {Quoted(missing)}. {this.ArgumentList()} Call it again with it.";
        }

        return null;
    }

    private string ArgumentList() =>
        this.argumentNames.Count is 0
            ? "It takes no arguments."
            : $"Its arguments are {string.Join(", ", this.argumentNames)}.";
}
