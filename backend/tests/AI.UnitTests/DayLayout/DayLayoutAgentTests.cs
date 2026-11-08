// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Net;
using System.Text;
using MailFathom.AI.Chat;
using MailFathom.AI.DayLayout;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.Tasks;
using MailFathom.Domain.Tasks;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests.DayLayout;

/// <summary>Covers what one arrangement asks the provider for, over a real provider client and a scripted transport.</summary>
public sealed class DayLayoutAgentTests
{
    private const string Arrangement = """{"placements": [{"task": 0, "startAt": "2026-09-21T10:00:00Z", "minutes": 45}], "notToday": [1]}""";

    private static readonly DateTimeOffset DayStart = new(2026, 9, 21, 8, 0, 0, TimeSpan.Zero);

    private static readonly DayLayoutQuestion Question = new(
        DayStart,
        DayStart.AddHours(12),
        [
            new DayLayoutTask(PersonalTaskId.Create(Guid.NewGuid()), "Answer the supplier", new DateOnly(2026, 9, 21)),
            new DayLayoutTask(PersonalTaskId.Create(Guid.NewGuid()), "Draft the tender response", new DateOnly(2026, 9, 21)),
        ],
        []);

    /// <summary>A model whose server honours a strict schema is asked for one, built for the day's own tasks.</summary>
    [Theory]
    [InlineData(ChatProviderApi.ChatCompletions)]
    [InlineData(ChatProviderApi.Responses)]
    public async Task SuggestAsync_AModelDeclaringStructuredOutputs_AsksForTheStrictSchema(ChatProviderApi api)
    {
        // Arrange
        using var provider = new ScriptedTransport(StructuredRequests.AnswerOver(api, Arrangement));
        var agent = provider.PlannerOver(ChatDeclarations.Plan(ChatDeclarations.Endpoint(api: api), structuredOutputs: true));

        // Act
        var derivation = await agent.SuggestAsync(Question, TestContext.Current.CancellationToken);

        // Assert
        var format = StructuredRequests.SchemaFormatOf(provider.RequestBodies[0], api);
        var suggestion = derivation.Suggestion;

        Assert.NotNull(suggestion);
        Assert.Single(suggestion.Placements);
        Assert.Equal(("json_schema", DayLayoutSchema.Name, true), (format.Type, format.Name, format.Strict));
        Assert.Contains("leave one empty where nothing belongs in it", provider.RequestBodies[0], StringComparison.Ordinal);
    }

    /// <summary>Without the declaration the request is the one every arrangement sent before, the shape told in prose instead.</summary>
    [Theory]
    [InlineData(ChatProviderApi.ChatCompletions)]
    [InlineData(ChatProviderApi.Responses)]
    public async Task SuggestAsync_AModelDeclaringNoStructuredOutputs_SendsNoResponseFormat(ChatProviderApi api)
    {
        // Arrange
        using var provider = new ScriptedTransport(StructuredRequests.AnswerOver(api, Arrangement));
        var agent = provider.PlannerOver(ChatDeclarations.Plan(ChatDeclarations.Endpoint(api: api)));

        // Act
        await agent.SuggestAsync(Question, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(StructuredRequests.CarriesResponseFormat(provider.RequestBodies[0]));
        Assert.Contains("The object may carry two fields", provider.RequestBodies[0], StringComparison.Ordinal);
    }

    /// <summary>A provider that answers from a script, so the request is composed by a real client and sent nowhere.</summary>
    private sealed class ScriptedTransport : IDisposable
    {
        private readonly FakeHttpMessageHandler handler;

        public ScriptedTransport(string payload) =>
            this.handler = new FakeHttpMessageHandler(async (request, cancellationToken) =>
            {
                this.RequestBodies.Add(request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken));

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json"),
                };
            });

        /// <summary>What the provider was actually sent.</summary>
        public List<string> RequestBodies { get; } = [];

        public DayLayoutAgent PlannerOver(ChatGenerationPlan plan)
        {
            var transportFactory = Substitute.For<IHttpClientFactory>();
            transportFactory
                .CreateClient(Arg.Any<string>())
                .Returns(_ => new HttpClient(this.handler, disposeHandler: false));

            var credentialSource = Substitute.For<IProviderEndpointCredentialSource>();
            credentialSource
                .ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(
                    ProviderEndpointCredential.FromApiKey("a-configured-key", resolvedMaterial: null)));

            var operationRunner = Substitute.For<IOutboundOperationRunner>();
            operationRunner
                .RunAsync(
                    Arg.Any<OutboundDependency>(),
                    Arg.Any<string>(),
                    Arg.Any<Func<CancellationToken, Task<Microsoft.Extensions.AI.ChatResponse>>>(),
                    Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    // The substitute cannot know the argument is present, and it always is: this configuration matches
                    // the only overload the decorator calls.
                    var operation = call.Arg<Func<CancellationToken, Task<Microsoft.Extensions.AI.ChatResponse>>>()!;

                    return operation(call.Arg<CancellationToken>());
                });

            var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
            spendLedger.TryAdmitRunAsync(Arg.Any<CancellationToken>()).Returns(true);

            return new DayLayoutAgent(
                plan,
                MailAnsweringRunBounds.Default,
                spendLedger,
                credentialSource,
                new OpenAiCompatibleClientFactory(),
                transportFactory,
                operationRunner,
                Substitute.For<IAiProviderHealthRecorder>(),
                SensitiveContentEgressGuards.Inactive(),
                new EmptyAgentInstructionEnvelope(),
                NullLoggerFactory.Instance,
                NullLogger<DayLayoutAgent>.Instance);
        }

        public void Dispose() => this.handler.Dispose();
    }
}
