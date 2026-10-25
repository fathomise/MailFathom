// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using MailFathom.AI.DecisionModels;
using MailFathom.AI.Decisions;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.TestSupport;
using Microsoft.Extensions.AI;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests.ProviderAdapters;

/// <summary>Covers the one adapter that speaks OpenRouter's Decisions API: what a request carries, and how strictly an answer is read.</summary>
public sealed class OpenRouterDecisionClientTests
{
    private const string Alias = "decider";
    private const string Model = "vendor/decider-2026-10-01";

    private static readonly ChatMessage[] Input = [new(ChatRole.User, "Your parcel is waiting, pay the fee here.")];

    private static readonly DecisionQuestion[] Questions =
    [
        DecisionQuestion.YesNo("spam", "Is this unsolicited bulk or deceptive mail?", "It is spam.", "It is wanted mail."),
        DecisionQuestion.Choice("kind", "What kind of mail is it?", [new("phishing", "Asks for credentials or payment."), new("ordinary", string.Empty)]),
        DecisionQuestion.Scale("urgency", "How urgent does it claim to be?", [new("low", string.Empty), new("medium", string.Empty), new("high", string.Empty)]),
    ];

    private const string WellFormedAnswer = """
        {
          "id": "dec-1",
          "model": "vendor/decider-2026-10-01",
          "provider": "SomeProvider",
          "answers": {
            "spam": { "type": "noul", "noul": 0.92 },
            "kind": { "type": "choice", "choice": "phishing", "confidence": 0.8, "probabilities": { "phishing": 0.8, "ordinary": 0.2 } },
            "urgency": { "type": "score", "score": 1.7, "probabilities": { "0": 0.1, "1": 0.1, "2": 0.8 }, "legend": {} }
          },
          "usage": { "input_tokens": 412, "output_tokens": 0, "cost": 0.0000123 }
        }
        """;

    [Fact]
    public async Task DecideAsync_AWellFormedAnswer_ReadsEveryQuestionsAnswerAndTheTokensButNoCost()
    {
        // Arrange
        using var handler = FakeHttpMessageHandler.AlwaysResponding(() => Json(HttpStatusCode.OK, WellFormedAnswer));
        using var client = ClientOver(handler);

        // Act
        var response = await client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DecisionQuestion.Yes, response.AnswerTo("spam").Value);
        Assert.Equal(0.92, response.AnswerTo("spam").Probabilities[DecisionQuestion.Yes], precision: 10);
        Assert.Equal(0.08, response.AnswerTo("spam").Probabilities[DecisionQuestion.No], precision: 10);
        Assert.Equal("phishing", response.AnswerTo("kind").Value);
        Assert.Equal("high", response.AnswerTo("urgency").Value);
        Assert.Equal(0.8, response.AnswerTo("urgency").Probabilities["high"], precision: 10);
        Assert.Equal(Model, response.ModelId);
        Assert.Equal("dec-1", response.ResponseId);
        Assert.Equal(412, response.Usage?.InputTokenCount);
        Assert.Equal(0, response.Usage?.OutputTokenCount);
        Assert.Null(response.Usage?.AdditionalCounts);
    }

    [Fact]
    public async Task DecideAsync_AnyRequest_IsSentToTheDecisionsRouteWithTheKeyTheRoutingBlockAndNothingLinkingIt()
    {
        // Arrange
        using var handler = FakeHttpMessageHandler.AlwaysResponding(() => Json(HttpStatusCode.OK, WellFormedAnswer));
        using var client = ClientOver(handler, address: new Uri("https://eu.openrouter.ai/some/path"));

        // Act
        await client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var request = Assert.Single(handler.RecordedRequests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("https://eu.openrouter.ai/api/alpha/decisions"), request.RequestUri);
        Assert.Equal(["Bearer a-configured-key"], request.Headers["Authorization"]);

        var body = JsonNode.Parse(request.ContentAsUtf8String())!.AsObject();
        Assert.Equal(Model, body["model"]!.GetValue<string>());
        Assert.Equal(Input[0].Text, body["state"]!["text"]!.GetValue<string>());
        Assert.True(body["provider"]!["zdr"]!.GetValue<bool>());
        Assert.Equal("deny", body["provider"]!["data_collection"]!.GetValue<string>());
        Assert.Equal(["model", "state", "questions", "provider"], body.Select(member => member.Key));

        var questions = body["questions"]!.AsObject();
        Assert.Equal("noul", questions["spam"]!["type"]!.GetValue<string>());
        Assert.Equal("It is spam.", questions["spam"]!["criteria"]!["true"]!.GetValue<string>());
        Assert.Equal("choice", questions["kind"]!["type"]!.GetValue<string>());
        Assert.Equal("ordinary", questions["kind"]!["criteria"]!["ordinary"]!.GetValue<string>());
        Assert.Equal("score", questions["urgency"]!["type"]!.GetValue<string>());
        Assert.Equal(3, questions["urgency"]!["criteria"]!.AsArray().Count);
    }

    [Fact]
    public async Task DecideAsync_AnUnauthenticatedModel_SendsNoAuthorization()
    {
        // Arrange
        using var handler = FakeHttpMessageHandler.AlwaysResponding(() => Json(HttpStatusCode.OK, WellFormedAnswer));
        using var client = ClientOver(handler, credential: ProviderEndpointCredential.Unauthenticated());

        // Act
        await client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.False(Assert.Single(handler.RecordedRequests).Headers.ContainsKey("Authorization"));
    }

    /// <summary>A missing key is the contract's way of not answering, and is reported as exactly that rather than as a verdict.</summary>
    [Fact]
    public async Task DecideAsync_AQuestionTheAnswerOmits_IsReadAsUnanswered()
    {
        // Arrange
        using var handler = FakeHttpMessageHandler.AlwaysResponding(() => Json(HttpStatusCode.OK, """
            { "model": "vendor/decider-2026-10-01", "answers": { "spam": { "type": "noul", "noul": 0.1 } }, "usage": { "input_tokens": 9, "output_tokens": 0 } }
            """));
        using var client = ClientOver(handler);

        // Act
        var response = await client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DecisionQuestion.No, response.AnswerTo("spam").Value);
        Assert.False(response.AnswerTo("kind").IsAnswered);
        Assert.Empty(response.AnswerTo("kind").Probabilities);
        Assert.False(response.AnswerTo("urgency").IsAnswered);
    }

    [Theory]
    [InlineData("""{ "type": "choice", "choice": "spam", "probabilities": { "spam": 1 } }""", "an answer of another type")]
    [InlineData("""{ "type": "noul" }""", "a missing probability")]
    [InlineData("""{ "type": "noul", "noul": 1.5 }""", "a probability above one")]
    [InlineData("""{ "type": "noul", "noul": "high" }""", "a probability that is not a number")]
    public async Task DecideAsync_AYesNoAnswerOutsideTheContract_RefusesTheWholeResponse(string answer, string because)
    {
        // Arrange
        using var handler = FakeHttpMessageHandler.AlwaysResponding(() => Json(HttpStatusCode.OK, AnswerOf("spam", answer)));
        using var client = ClientOver(handler);

        // Act
        var failure = await Assert.ThrowsAsync<DecisionFailedException>(
            () => client.DecideAsync(Input, [Questions[0]], cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.True(failure.Failure is DecisionFailure.AnswerUnreadable, because);
        Assert.Equal(Alias, failure.ModelAlias);
    }

    [Theory]
    [InlineData("""{ "type": "choice", "choice": "invoice", "probabilities": { "phishing": 0.5, "ordinary": 0.5 } }""", "a choice nobody offered")]
    [InlineData("""{ "type": "choice", "choice": "phishing", "probabilities": { "phishing": 0.5, "invoice": 0.5 } }""", "a distribution naming an option nobody offered")]
    [InlineData("""{ "type": "choice", "choice": "phishing", "probabilities": { "phishing": 1 } }""", "a distribution missing an offered option")]
    [InlineData("""{ "type": "choice", "choice": "phishing" }""", "no distribution at all")]
    public async Task DecideAsync_AChoiceAnswerOutsideTheContract_RefusesTheWholeResponse(string answer, string because)
    {
        // Arrange
        using var handler = FakeHttpMessageHandler.AlwaysResponding(() => Json(HttpStatusCode.OK, AnswerOf("kind", answer)));
        using var client = ClientOver(handler);

        // Act
        var failure = await Assert.ThrowsAsync<DecisionFailedException>(
            () => client.DecideAsync(Input, [Questions[1]], cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.True(failure.Failure is DecisionFailure.AnswerUnreadable, because);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{ "model": "m", "usage": { "input_tokens": 1, "output_tokens": 0 } }""")]
    [InlineData("""{ "model": "m", "answers": [], "usage": { "input_tokens": 1, "output_tokens": 0 } }""")]
    public async Task DecideAsync_ABodyThatIsNotTheContractsShape_IsUnreadable(string body)
    {
        // Arrange
        using var handler = FakeHttpMessageHandler.AlwaysResponding(() => Json(HttpStatusCode.OK, body));
        using var client = ClientOver(handler);

        // Act
        var failure = await Assert.ThrowsAsync<DecisionFailedException>(
            () => client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DecisionFailure.AnswerUnreadable, failure.Failure);
    }

    /// <summary>The body of a refusal can quote the request, and the request carries somebody's mail.</summary>
    [Fact]
    public async Task DecideAsync_ARefusedRequest_RaisesTheStatusAndNothingFromTheBody()
    {
        // Arrange
        using var handler = FakeHttpMessageHandler.AlwaysResponding(() => Json(HttpStatusCode.PaymentRequired, """{ "error": { "message": "Your parcel is waiting" } }"""));
        using var client = ClientOver(handler);

        // Act
        var failure = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(HttpStatusCode.PaymentRequired, failure.StatusCode);
        Assert.DoesNotContain("parcel", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DecideAsync_ARequestLongerThanTheModelAccepts_IsRefusedBeforeAnythingIsSent()
    {
        // Arrange
        using var handler = FakeHttpMessageHandler.AlwaysResponding(() => Json(HttpStatusCode.OK, WellFormedAnswer));
        using var client = ClientOver(handler, maximumRequestCharacters: 200);

        // Act
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Empty(handler.RecordedRequests);
    }

    [Fact]
    public async Task DecideAsync_AnInputCarryingAnImage_IsRefusedRatherThanSentWithoutIt()
    {
        // Arrange
        using var handler = FakeHttpMessageHandler.AlwaysResponding(() => Json(HttpStatusCode.OK, WellFormedAnswer));
        using var client = ClientOver(handler);
        ChatMessage[] withImage = [new(ChatRole.User, [new TextContent("see attached"), new DataContent(new byte[] { 1, 2, 3 }, "image/png")])];

        // Act
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.DecideAsync(withImage, Questions, cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Empty(handler.RecordedRequests);
    }

    [Fact]
    public async Task DecideAsync_JsonDataBesideText_IsSentAsNamedFieldsOfOneState()
    {
        // Arrange
        using var handler = FakeHttpMessageHandler.AlwaysResponding(() => Json(HttpStatusCode.OK, WellFormedAnswer));
        using var client = ClientOver(handler);
        ChatMessage[] evidence =
        [
            new(ChatRole.User, [new DataContent(Encoding.UTF8.GetBytes("""{ "subject": "Parcel", "sender": "a@example.com" }"""), "application/json"), new TextContent("Pay the fee.")]),
        ];

        // Act
        await client.DecideAsync(evidence, Questions, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var state = JsonNode.Parse(Assert.Single(handler.RecordedRequests).ContentAsUtf8String())!["state"]!.AsObject();
        Assert.Equal(["subject", "sender", "text"], state.Select(member => member.Key));
    }

    [Fact]
    public async Task DecideAsync_ACredentialThatCannotBeResolved_IsARejectedCredential()
    {
        // Arrange
        using var handler = FakeHttpMessageHandler.AlwaysResponding(() => Json(HttpStatusCode.OK, WellFormedAnswer));
        var credentialSource = Substitute.For<IProviderEndpointCredentialSource>();
        credentialSource
            .ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<ProviderEndpointCredential>>(_ => throw new InvalidOperationException("No such endpoint."));
        using var client = new OpenRouterDecisionClient(Plan(), credentialSource, TransportOver(handler));

        // Act
        var failure = await Assert.ThrowsAsync<DecisionFailedException>(
            () => client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DecisionFailure.CredentialRejected, failure.Failure);
        Assert.Empty(handler.RecordedRequests);
    }

    [Fact]
    public void GetService_Metadata_NamesTheProviderTheRouteAndTheModel()
    {
        // Arrange
        using var handler = FakeHttpMessageHandler.AlwaysResponding(() => Json(HttpStatusCode.OK, WellFormedAnswer));
        using var client = ClientOver(handler);

        // Act
        var metadata = client.GetService<DecisionClientMetadata>();

        // Assert
        Assert.Equal("openrouter", metadata?.ProviderName);
        Assert.Equal(new Uri("https://openrouter.ai/api/alpha/decisions"), metadata?.ProviderUri);
        Assert.Equal(Model, metadata?.DefaultModelId);
    }

    internal static DecisionModelPlan Plan(Uri? address = null, int maximumRequestCharacters = 32_000) =>
        new(Alias, address, Model, TimeSpan.FromSeconds(30), maximumRequestCharacters);

    internal static IHttpClientFactory TransportOver(FakeHttpMessageHandler handler)
    {
        var transportFactory = Substitute.For<IHttpClientFactory>();
        transportFactory
            .CreateClient(OpenRouterDecisionClient.TransportName)
            .Returns(_ => new HttpClient(handler, disposeHandler: false));

        return transportFactory;
    }

    internal static OpenRouterDecisionClient ClientOver(
        FakeHttpMessageHandler handler,
        Uri? address = null,
        int maximumRequestCharacters = 32_000,
        ProviderEndpointCredential? credential = null)
    {
        var credentialSource = Substitute.For<IProviderEndpointCredentialSource>();
        credentialSource
            .ResolveAsync(Alias, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(credential ?? ProviderEndpointCredential.FromApiKey("a-configured-key", resolvedMaterial: null)));

        return new OpenRouterDecisionClient(Plan(address, maximumRequestCharacters), credentialSource, TransportOver(handler));
    }

    internal static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static string AnswerOf(string questionId, string answer) =>
        $$"""{ "model": "vendor/decider-2026-10-01", "answers": { "{{questionId}}": {{answer}} }, "usage": { "input_tokens": 9, "output_tokens": 0 } }""";
}
