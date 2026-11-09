// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Net;
using System.Text;
using System.Text.Json;
using MailFathom.AI.Chat;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailFathom.AI.UnitTests.ProviderAdapters;

/// <summary>Covers the one place a declaration becomes the parameters a request carries.</summary>
/// <remarks>
/// Both the single-request adapter and the answering run send through this, which is what stops a parameter reaching one
/// path and not the other. What each test states is whether a member appears on the options at all, because an absent
/// member is what keeps the parameter off a request that a model would reject for carrying it.
/// <para>
/// The reasoning effort is asserted here only on the request an answering run composes with tools over the responses
/// API. It is stated through the client library's own request options, so what it produces is a property of the request
/// rather than of this object, and <see cref="ProviderChatModelClientTests" /> asserts it on the wire over both APIs and
/// over a level no build knows.
/// </para>
/// </remarks>
public sealed class ChatGenerationParameterMappingTests
{
    /// <summary>The responses payload a provider answers with when the model finished without calling a tool.</summary>
    private const string OneResponse =
        "{\"id\":\"resp-1\",\"object\":\"response\",\"created_at\":1,\"model\":\"a-chat-model\",\"status\":\"completed\","
        + "\"output\":[{\"type\":\"message\",\"id\":\"msg-1\",\"status\":\"completed\",\"role\":\"assistant\","
        + "\"content\":[{\"type\":\"output_text\",\"text\":\"an answer\",\"annotations\":[]}]}],"
        + "\"usage\":{\"input_tokens\":11,\"output_tokens\":3,\"total_tokens\":14}}";

    [Fact]
    public void ToChatOptions_ADeclarationWithEveryParameter_CarriesAllOfThem()
    {
        // Arrange
        var plan = ChatDeclarations.Plan(
            maximumOutputTokens: 512,
            temperature: 0.2f,
            topP: 0.9f,
            reasoningEffort: "medium");

        // Act
        var options = ChatGenerationParameterMapping.ToChatOptions(plan);

        // Assert
        Assert.Equal(512, options.MaxOutputTokens);
        Assert.Equal(0.2f, options.Temperature);
        Assert.Equal(0.9f, options.TopP);
        Assert.NotNull(options.RawRepresentationFactory);
    }

    /// <summary>
    /// A model that does not reason rejects the parameter, so an unwritten effort has to leave the hook off entirely —
    /// on the chat completions API, where the effort is the only thing the hook was ever carrying.
    /// </summary>
    [Fact]
    public void ToChatOptions_AChatCompletionsDeclarationWithoutAReasoningEffort_CarriesNoRequestHook()
    {
        // Act
        var options = ChatGenerationParameterMapping.ToChatOptions(ChatDeclarations.Plan());

        // Assert
        Assert.Null(options.RawRepresentationFactory);
        Assert.Null(options.Temperature);
        Assert.Null(options.TopP);
    }

    /// <summary>An additional property is carried through the client library's own request options, so on chat completions it needs the hook an effort would.</summary>
    [Fact]
    public void ToChatOptions_AChatCompletionsDeclarationWithOnlyAdditionalProperties_CarriesTheRequestHook()
    {
        // Arrange
        var plan = ChatDeclarations.Plan(additionalProperties: new Dictionary<string, JsonElement>
        {
            ["top_k"] = JsonSerializer.SerializeToElement(40),
        });

        // Act
        var options = ChatGenerationParameterMapping.ToChatOptions(plan);

        // Assert
        Assert.NotNull(options.RawRepresentationFactory);
    }

    /// <summary>
    /// The responses API carries a decision of its own on every request — what the provider may keep of what it was
    /// sent — so the hook belongs to the API rather than to a parameter somebody happened to declare. What it puts on
    /// the request is asserted on the wire in <see cref="ProviderChatModelClientTests" />.
    /// </summary>
    [Fact]
    public void ToChatOptions_AResponsesDeclarationWithoutAReasoningEffort_StillCarriesTheRequestHook()
    {
        // Arrange
        var plan = ChatDeclarations.Plan(ChatDeclarations.Endpoint(api: ChatProviderApi.Responses));

        // Act
        var options = ChatGenerationParameterMapping.ToChatOptions(plan);

        // Assert
        Assert.NotNull(options.RawRepresentationFactory);
    }

    /// <summary>
    /// A current reasoning model takes function tools beside a stated effort only over the responses API, so the request
    /// an answering run sends there has to carry all of it at once: the tools the operation offers, the declared effort,
    /// the storage refusal, and the encrypted reasoning a stateless tool loop hands back on its next turn. Composed the
    /// way every operation is, so what is proved is the request the run actually sends.
    /// </summary>
    [Fact]
    public async Task ToChatOptions_AResponsesDeclarationComposedWithToolsAndAReasoningEffort_SendsThemInOneRequest()
    {
        // Arrange
        string? sentBody = null;
        using var handler = new FakeHttpMessageHandler(async (request, cancellationToken) =>
        {
            sentBody = await request.Content!.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(OneResponse, Encoding.UTF8, "application/json"),
            };
        });
        using var transport = new HttpClient(handler, disposeHandler: false);
        using var credential = ProviderEndpointCredential.FromApiKey("a-resolved-key", resolvedMaterial: null);
        var plan = ChatDeclarations.Plan(ChatDeclarations.Endpoint(api: ChatProviderApi.Responses), reasoningEffort: "medium");

        using var chatClient = new OpenAiCompatibleClientFactory().OpenChatClient(plan.Endpoint, credential, transport);
        var agent = AgentComposition.Compose(
            chatClient,
            plan,
            new AgentOperation(
                "an-operation",
                "Answer the question you were asked.",
                [AIFunctionFactory.Create(static (string queryText) => queryText, "search_mail")]),
            new EmptyAgentInstructionEnvelope(),
            NullLoggerFactory.Instance);

        // Act
        await agent.RunAsync("a question", session: null, options: null, TestContext.Current.CancellationToken);

        // Assert
        using var sent = JsonDocument.Parse(sentBody!);
        var tool = Assert.Single(sent.RootElement.GetProperty("tools").EnumerateArray());

        Assert.Equal("function", tool.GetProperty("type").GetString());
        Assert.Equal("search_mail", tool.GetProperty("name").GetString());
        Assert.Equal("medium", sent.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.False(sent.RootElement.GetProperty("store").GetBoolean());
        Assert.Equal("reasoning.encrypted_content", Assert.Single(sent.RootElement.GetProperty("include").EnumerateArray()).GetString());
    }

    [Fact]
    public void ToChatOptions_WithoutAPlan_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => ChatGenerationParameterMapping.ToChatOptions(null!));
    }
}
