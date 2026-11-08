// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MailFathom.AI.Chat;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using OpenAI.Responses;

namespace MailFathom.AI.ProviderAdapters;

/// <summary>Turns the declared generation parameters into the options one request carries.</summary>
/// <remarks>
/// <para>
/// One mapping rather than one per caller, because the single-request adapter and the answering run send the same
/// parameters to the same endpoint and differ only in what each adds on top. Two copies would let a parameter reach one
/// path and not the other, and the deployment would see a setting that worked for a question and was ignored for a
/// judgement.
/// </para>
/// <para>
/// It is also what keeps every parameter optional in the way the declaration means it: an unwritten value produces no
/// member on the options object, so the request carries nothing rather than a default this side invented. Several
/// current models reject a sampling parameter or a reasoning effort outright, so sending one nobody asked for turns
/// every call a deployment makes into a rejected request.
/// </para>
/// <para>
/// One thing on a request is decided here rather than declared: what the provider may keep of what it was sent. That
/// follows the API a call is conducted through rather than anything an operator wrote, which is why it lives beside the
/// parameters instead of in the configuration they come from.
/// </para>
/// </remarks>
internal static class ChatGenerationParameterMapping
{
    /// <summary>The key the client library reads off the options to send a response schema as a strict one.</summary>
    internal const string StrictSchemaKey = "strict";

    /// <summary>The request member OpenRouter reads its routing preferences from.</summary>
    private const string ProviderMember = "provider";

    /// <summary>The routing preference that keeps OpenRouter from choosing a provider that would ignore a parameter the request carries.</summary>
    private const string RequireParametersMember = "require_parameters";

    /// <summary>Builds the options one request carries from what the deployment declared.</summary>
    /// <param name="plan">The validated declaration.</param>
    /// <param name="responseSchema">
    /// The JSON schema an operation answering in a document of a known shape names for its answer, or
    /// <see langword="null" /> for an answer of free text.
    /// </param>
    /// <returns>The options, carrying only the parameters the declaration wrote.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="plan" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The schema is sent only where the model declares that its server honours one, and then always as a strict
    /// schema: a schema the provider is free to depart from constrains nothing a reading has to rely on. Where the
    /// model declares nothing, the schema is dropped here and the request is exactly the one an operation naming none
    /// sends, which is why an operation decides what its instruction says from the same declaration.
    /// </remarks>
    public static ChatOptions ToChatOptions(ChatGenerationPlan plan, ChatResponseFormatJson? responseSchema = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var options = new ChatOptions
        {
            MaxOutputTokens = plan.MaximumOutputTokens,
            Temperature = plan.Temperature,
            TopP = plan.TopP,
        };

        var schema = plan.StructuredOutputs ? responseSchema : null;

        if (schema is not null)
        {
            options.ResponseFormat = schema;
            options.AdditionalProperties = new AdditionalPropertiesDictionary { [StrictSchemaKey] = true };
        }

        options.RawRepresentationFactory = RequestOptionsFactoryFor(
            plan.Endpoint.Api,
            plan.ReasoningEffort,
            plan.AdditionalProperties,
            requiresEveryParameter: schema is not null && plan.Endpoint.IsReachedThroughOpenRouter);

        return options;
    }

    /// <summary>Builds the per-request hook its API needs, or none where the API needs nothing.</summary>
    /// <remarks>
    /// <para>
    /// The hook is where this deployment states what the client library's own request options carry, and the two APIs
    /// need it for different reasons. The responses API always needs one, because storage at the provider is decided
    /// per request and its default is to store; chat completions needs one only when an effort or an additional property
    /// was declared, because its default is already what this deployment wants and a member nobody asked for is what a
    /// model rejects a whole request over.
    /// </para>
    /// <para>
    /// Everything else on the options is left alone — the abstraction fills the members it owns over whatever this
    /// returns, so the bounds, the sampling parameters, the instruction, and the tools all still reach the request.
    /// </para>
    /// <para>
    /// Reachable on its own for a caller whose options are composed by somebody else — the evaluation suite's judge,
    /// whose requests an evaluator builds — so the effort it declares reaches the request through this mapping rather
    /// than a second one.
    /// </para>
    /// </remarks>
    /// <param name="api">The API the request is conducted through.</param>
    /// <param name="effort">The declared reasoning effort, or <see langword="null" /> to send none.</param>
    /// <param name="additionalProperties">The request members every call carries beside the ones this deployment writes, or <see langword="null" /> for none.</param>
    /// <param name="requiresEveryParameter">
    /// Whether the request asks OpenRouter to route it only to a provider honouring every parameter it carries, which a
    /// strict response schema needs: a provider that drops the schema answers in whatever shape it likes.
    /// </param>
    /// <returns>The hook, or <see langword="null" /> where the request needs none.</returns>
    public static Func<IChatClient, object?>? RequestOptionsFactoryFor(
        ChatProviderApi api,
        string? effort,
        IReadOnlyDictionary<string, JsonElement>? additionalProperties = null,
        bool requiresEveryParameter = false)
    {
        additionalProperties ??= new Dictionary<string, JsonElement>();

        if (requiresEveryParameter)
        {
            additionalProperties = WithEveryParameterRequired(additionalProperties);
        }

        if (api is ChatProviderApi.Responses)
        {
            return _ => StatelessResponseOptions(effort, additionalProperties);
        }

        return effort is null && additionalProperties.Count == 0
            ? null
            : _ => ChatCompletionOptionsFor(effort, additionalProperties);
    }

    /// <summary>Adds the routing preference that requires every parameter to be honoured to whatever routing block the model declared.</summary>
    /// <remarks>
    /// Merged rather than written beside the declaration, because OpenRouter reads one <c>provider</c> member and an
    /// operator may already have declared one as an additional property — an order of providers, a refusal of data
    /// collection — which a second member of the same name would overwrite.
    /// </remarks>
    private static Dictionary<string, JsonElement> WithEveryParameterRequired(IReadOnlyDictionary<string, JsonElement> additionalProperties)
    {
        var routing = additionalProperties.TryGetValue(ProviderMember, out var declared) && declared.ValueKind is JsonValueKind.Object
            ? JsonNode.Parse(declared.GetRawText())!.AsObject()
            : new JsonObject();

        routing[RequireParametersMember] = true;

        using var merged = JsonDocument.Parse(routing.ToJsonString());

        return new Dictionary<string, JsonElement>(additionalProperties) { [ProviderMember] = merged.RootElement.Clone() };
    }

    /// <summary>Builds the responses request that leaves the provider holding nothing once it has answered.</summary>
    /// <remarks>
    /// <para>
    /// A request here carries the question, the instruction, and the mail passages retrieval selected for it, and the
    /// responses API stores the whole of that for thirty days unless the request refuses — where it is readable in the
    /// provider's own console by anyone holding the account. So the refusal is stated on every call this API conducts
    /// rather than offered as a setting: a deployment that wanted a copy of its own correspondence kept by a third
    /// party is not a shape this system has. Chat completions defaults the other way and is left alone.
    /// </para>
    /// <para>
    /// Refusing the store is what makes the run stateless, and that has a second half. A reasoning model returns its
    /// reasoning as encrypted content the caller hands back on the next turn, and the provider emits that content only
    /// where the request asked for it. The answering run is a tool loop, so without it every turn after the first would
    /// begin without what the model worked out over the mail it had already read. Asking for it costs nothing on a
    /// model that does not reason, which returns none.
    /// </para>
    /// <para>
    /// The effort is stated here rather than through the provider-neutral reasoning member, and that is what lets a
    /// deployment name a level this build has never heard of. The neutral member carries a closed set fixed when its
    /// package was compiled, so a level a model gains later — as <c>xhigh</c> was gained — would be unsendable until a
    /// release of MailFathom caught up. The library's own type is built from a string, so the value the operator wrote
    /// is the value that goes out.
    /// </para>
    /// <para>
    /// The additional properties go in last, through the request's own JSON patch, because they are exactly the members
    /// this build has no typed property for. None of them can be a member set above: the plan refuses every name this
    /// deployment writes, so the storage refusal and the included reasoning content cannot be undone from here.
    /// </para>
    /// </remarks>
    // The whole responses request-options surface carries the evaluation-only marker in this release of the client
    // library, as does the chat completions reasoning member, and the JSON patch both carry is marked experimental by the
    // client model library, so the suppression covers the methods that build a request and nothing else in the file
    // inherits it.
#pragma warning disable OPENAI001, SCME0001
    private static CreateResponseOptions StatelessResponseOptions(
        string? effort,
        IReadOnlyDictionary<string, JsonElement> additionalProperties)
    {
        var options = new CreateResponseOptions { StoredOutputEnabled = false };

        options.IncludedProperties.Add(IncludedResponseProperty.ReasoningEncryptedContent);

        if (effort is not null)
        {
            options.ReasoningOptions = new ResponseReasoningOptions
            {
                ReasoningEffortLevel = new ResponseReasoningEffortLevel(effort),
            };
        }

        foreach (var (name, value) in additionalProperties)
        {
            options.Patch.Set(PathTo(name), BinaryData.FromString(value.GetRawText()));
        }

        return options;
    }

    /// <summary>Builds the chat completions request, which carries the declared effort and the additional properties and nothing else.</summary>
    private static ChatCompletionOptions ChatCompletionOptionsFor(
        string? effort,
        IReadOnlyDictionary<string, JsonElement> additionalProperties)
    {
        var options = new ChatCompletionOptions();

        if (effort is not null)
        {
            options.ReasoningEffortLevel = new ChatReasoningEffortLevel(effort);
        }

        foreach (var (name, value) in additionalProperties)
        {
            options.Patch.Set(PathTo(name), BinaryData.FromString(value.GetRawText()));
        }

        return options;
    }
#pragma warning restore OPENAI001, SCME0001

    /// <summary>Addresses a top-level member of the request body, which the plan's name shape keeps from reaching anywhere deeper.</summary>
    private static byte[] PathTo(string name) => Encoding.UTF8.GetBytes($"$.{name}");
}
