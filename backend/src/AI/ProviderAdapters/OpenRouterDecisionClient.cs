// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using MailFathom.AI.DecisionModels;
using MailFathom.AI.Decisions;
using MailFathom.AI.Providers;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.ProviderAdapters;

/// <summary>Asks a decision model closed questions through OpenRouter's Decisions API.</summary>
/// <remarks>
/// <para>
/// <b>This adapter is the bounded exception to ADR 0011</b> that
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0041-asking-a-model-a-closed-question-through-openrouters-decisions-api.md">ADR 0041</see>
/// records: a second wire protocol, for decisions only, that no OpenAI-compatible client construction reaches. It ends
/// when OpenAI's standard Decisions contract is served by OpenRouter, and issue #2675 is that exit — it replaces this
/// adapter and touches nothing above it. Everything OpenRouter calls its own therefore stops in this file: the route,
/// the question types <c>noul</c>, <c>choice</c>, and <c>score</c>, the <c>state</c> and <c>criteria</c> members, and
/// the routing block. Nothing of it crosses <see cref="IDecisionClient" />.
/// </para>
/// <para>
/// The contract is alpha and carries no stability commitment, which is why the reading is strict rather than generous:
/// an answer of an unexpected type, a distribution that does not name exactly the options offered, and a probability
/// outside [0, 1] each refuse the whole response. Reading any of them with a default would turn a contract change at
/// the provider into a verdict nobody gave.
/// </para>
/// <para>
/// Every request carries <c>"provider": { "zdr": true, "data_collection": "deny" }</c>, written here and reachable by no
/// configuration, so routing is restricted to endpoints that keep nothing and train on nothing. A model with no such
/// endpoint fails the request instead of falling through to one that keeps data. Nothing that links one request to
/// another — a session, a user, a trace — is ever sent.
/// </para>
/// <para>
/// It composes nothing and classifies nothing: a refused status reaches its caller as an
/// <see cref="HttpRequestException" /> carrying the status and nothing from the body, which an error body can quote the
/// request into, and <see cref="ResilientDecisionClient" /> is what turns it into a decision failure.
/// </para>
/// </remarks>
internal sealed class OpenRouterDecisionClient : IDecisionClient
{
    /// <summary>Names the registered transport a decision request is sent over.</summary>
    /// <remarks>Declared by the consumer for the reason <see cref="ProviderChatModelClient.TransportName" /> gives.</remarks>
    internal const string TransportName = "mailfathom.decision-provider";

    /// <summary>The most questions one request may ask.</summary>
    internal const int MaximumQuestionsPerRequest = 16;

    /// <summary>The most options one question may offer.</summary>
    internal const int MaximumOptionsPerQuestion = 32;

    /// <summary>The name telemetry reports this provider under.</summary>
    internal const string ProviderName = "openrouter";

    private const string InputTextMember = "text";

    private static readonly Uri DefaultAddress = new("https://openrouter.ai", UriKind.Absolute);

    private readonly DecisionModelPlan plan;
    private readonly IProviderEndpointCredentialSource credentialSource;
    private readonly IHttpClientFactory transportFactory;
    private readonly Uri route;

    /// <summary>Initializes the adapter over one declared decision model.</summary>
    /// <param name="plan">Which model is asked, where, and under which bounds.</param>
    /// <param name="credentialSource">Resolves what a request presents, per request.</param>
    /// <param name="transportFactory">Opens the transport a request is sent over, one per request.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public OpenRouterDecisionClient(
        DecisionModelPlan plan,
        IProviderEndpointCredentialSource credentialSource,
        IHttpClientFactory transportFactory)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(credentialSource);
        ArgumentNullException.ThrowIfNull(transportFactory);

        this.plan = plan;
        this.credentialSource = credentialSource;
        this.transportFactory = transportFactory;

        // The route replaces whatever path the address carries, because the address names the host — the global router
        // or a regional one — and the route under it is this contract's.
        this.route = new Uri(plan.Address ?? DefaultAddress, "/api/alpha/decisions");
    }

    /// <inheritdoc />
    public async Task<DecisionResponse> DecideAsync(
        IEnumerable<ChatMessage> input,
        IReadOnlyList<DecisionQuestion> questions,
        DecisionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        RequireWithinBounds(questions);

        var model = options?.ModelId ?? this.plan.Model;
        var body = ComposeRequest(model, [.. input], questions);

        if (body.Length > this.plan.MaximumRequestCharacters)
        {
            throw new ArgumentException(
                $"The request to decision model '{this.plan.Alias}' carries {body.Length} characters, more than the {this.plan.MaximumRequestCharacters} it is declared to accept.",
                nameof(input));
        }

        var answered = await this.SendAsync(body, cancellationToken);

        return OpenRouterDecisionReading.Read(answered, questions, this.plan.Alias);
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (serviceKey is not null)
        {
            return null;
        }

        if (serviceType == typeof(DecisionClientMetadata))
        {
            return new DecisionClientMetadata(ProviderName, this.route, this.plan.Model);
        }

        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    /// <inheritdoc />
    /// <remarks>Holds nothing between requests: the transport and the credential are opened and released per request.</remarks>
    public void Dispose()
    {
    }

    private static void RequireWithinBounds(IReadOnlyList<DecisionQuestion> questions)
    {
        DecisionQuestion.RequireUsable(questions);

        if (questions.Count > MaximumQuestionsPerRequest)
        {
            throw new ArgumentException(
                $"A decision request asks at most {MaximumQuestionsPerRequest} questions.",
                nameof(questions));
        }

        if (questions.Any(question => question.Options.Count > MaximumOptionsPerQuestion))
        {
            throw new ArgumentException(
                $"A decision question offers at most {MaximumOptionsPerQuestion} options.",
                nameof(questions));
        }
    }

    /// <summary>Writes the request body in OpenRouter's shape.</summary>
    private static string ComposeRequest(string model, IReadOnlyList<ChatMessage> input, IReadOnlyList<DecisionQuestion> questions)
    {
        var asked = new JsonObject();

        foreach (var question in questions)
        {
            asked[question.Id] = ComposeQuestion(question);
        }

        var request = new JsonObject
        {
            ["model"] = model,
            ["state"] = ComposeState(input),
            ["questions"] = asked,
            ["provider"] = new JsonObject
            {
                ["zdr"] = true,
                ["data_collection"] = "deny",
            },
        };

        return request.ToJsonString();
    }

    /// <summary>Writes the input as one object with named fields, which is the shape OpenRouter states it reads best.</summary>
    /// <remarks>
    /// A JSON object the caller put in as data contributes its members as they are, and every text part is joined under
    /// one member of its own. Anything else — an image above all — is refused rather than dropped, because content a
    /// caller handed over and this adapter silently left out is an answer about less than the caller thinks it asked.
    /// </remarks>
    private static JsonObject ComposeState(IReadOnlyList<ChatMessage> input)
    {
        var state = new JsonObject();
        var texts = new List<string>();

        foreach (var content in input.SelectMany(message => message.Contents))
        {
            switch (content)
            {
                case TextContent { Text: { Length: > 0 } text }:
                    texts.Add(text);
                    break;

                case DataContent data when data.HasTopLevelMediaType("application") && data.MediaType.EndsWith("json", StringComparison.OrdinalIgnoreCase):
                    MergeMembers(state, data);
                    break;

                case TextContent:
                    break;

                default:
                    throw new ArgumentException(
                        "A decision request carries text and JSON object data only.",
                        nameof(input));
            }
        }

        if (texts.Count > 0)
        {
            if (state.ContainsKey(InputTextMember))
            {
                throw new ArgumentException(
                    $"JSON data in a decision request names a member '{InputTextMember}', which the request's text is sent under.",
                    nameof(input));
            }

            state[InputTextMember] = string.Join("\n\n", texts);
        }

        if (state.Count is 0)
        {
            throw new ArgumentException("A decision request carries some input to decide about.", nameof(input));
        }

        return state;
    }

    private static void MergeMembers(JsonObject state, DataContent data)
    {
        if (JsonNode.Parse(data.Data.Span) is not JsonObject members)
        {
            throw new ArgumentException("JSON data in a decision request is one object.", nameof(data));
        }

        foreach (var name in members.Select(member => member.Key).ToArray())
        {
            if (state.ContainsKey(name))
            {
                throw new ArgumentException($"Two parts of a decision request's input name the member '{name}'.", nameof(data));
            }

            var value = members[name];
            members.Remove(name);
            state[name] = value;
        }
    }

    private static JsonObject ComposeQuestion(DecisionQuestion question) => question.Kind switch
    {
        DecisionQuestionKind.YesNo => ComposeYesNo(question),
        DecisionQuestionKind.Choice => new JsonObject
        {
            ["type"] = "choice",
            ["instructions"] = question.Instructions,
            ["criteria"] = new JsonObject(question.Options.Select(option =>
                KeyValuePair.Create(option.Value, (JsonNode?)DescriptionOf(option)))),
        },
        DecisionQuestionKind.Scale => new JsonObject
        {
            ["type"] = "score",
            ["instructions"] = question.Instructions,
            ["criteria"] = new JsonArray([.. question.Options.Select(option => (JsonNode?)DescriptionOf(option))]),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(question), question.Kind, "No decision question of this kind exists."),
    };

    private static JsonObject ComposeYesNo(DecisionQuestion question)
    {
        var composed = new JsonObject
        {
            ["type"] = "noul",
            ["instructions"] = question.Instructions,
        };

        var yes = question.Options.Single(option => option.Value == DecisionQuestion.Yes);
        var no = question.Options.Single(option => option.Value == DecisionQuestion.No);

        // Both or neither, because the contract requires the pair once criteria are written at all.
        if (yes.Description.Length > 0 || no.Description.Length > 0)
        {
            composed["criteria"] = new JsonObject
            {
                ["true"] = DescriptionOf(yes),
                ["false"] = DescriptionOf(no),
            };
        }

        return composed;
    }

    /// <summary>Names an option to the model by its description, or by its value where it carries none.</summary>
    private static string DescriptionOf(DecisionQuestionOption option) =>
        option.Description.Length > 0 ? option.Description : option.Value;

    /// <summary>Sends one request and returns the body of a successful answer.</summary>
    /// <remarks>
    /// The credential is resolved per request and released with it, and the transport is opened per request, for the
    /// reasons the chat adapter gives. The body is buffered under the registration's response limit, so an answer
    /// larger than any decision could be arrives as a transport failure rather than as a stream read without end.
    /// </remarks>
    private async Task<string> SendAsync(string body, CancellationToken cancellationToken)
    {
        using var credential = await this.ResolveCredentialAsync(cancellationToken);
        using var transport = this.transportFactory.CreateClient(TransportName);
        using var request = new HttpRequestMessage(HttpMethod.Post, this.route)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        if (credential.Kind is ProviderEndpointCredentialKind.ApiKey)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.ApiKey);
        }
        else if (credential.Kind is not ProviderEndpointCredentialKind.Unauthenticated)
        {
            throw new DecisionFailedException(this.plan.Alias, DecisionFailure.CredentialRejected);
        }

        using var response = await transport.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // The status alone: the body of a refusal can quote the request, and the request carries somebody's mail.
            throw new HttpRequestException(
                $"Decision model '{this.plan.Alias}' answered with status {(int)response.StatusCode}.",
                inner: null,
                response.StatusCode);
        }

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task<ProviderEndpointCredential> ResolveCredentialAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await this.credentialSource.ResolveAsync(this.plan.Alias, cancellationToken);
        }
        catch (InvalidOperationException refusal)
        {
            throw new DecisionFailedException(this.plan.Alias, DecisionFailure.CredentialRejected, refusal);
        }
    }
}
