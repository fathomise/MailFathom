// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Security.Cryptography;
using MailFathom.AI.Chat;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.AI.Retrieval;
using MailFathom.Application.Accounts;
using MailFathom.Application.Agent.Answering;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Answers;
using MailFathom.Application.Chat;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Blocks;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.AgentConversations;

/// <summary>Composes the Agent's answer to one question as an Agent Framework agent over the person's mail, calendar, and tasks.</summary>
/// <remarks>
/// <para>
/// The composition is <see cref="AgentComposition" />'s, like every other AI operation here; what is this agent's own is
/// its instruction and its tools, which <see cref="AgentConversationTools" /> states. Every call goes through the run's
/// own ceiling and the provider's bulkhead, in that order, exactly as the answering and drafting agents send theirs.
/// </para>
/// <para>
/// <strong>What it composes is written as it is composed.</strong> A status line before each tool, the state of a
/// conversation and every proposal the moment a tool produces it, and the answer itself once the model has said it. None
/// of it waits for the run to end, which is what lets a client that was never connected read the whole of it later.
/// </para>
/// <para>
/// The answer cites every message the run read rather than the ones the model says it used. A citation the model named
/// would be a claim about its own reading; the ones minted here are what it was actually shown.
/// </para>
/// </remarks>
internal sealed class AgentConversationAgent : IAgentAnswerComposer
{
    private readonly ChatGenerationPlan plan;
    private readonly AgentRunBounds runBounds;
    private readonly IMailAnsweringSpendLedger spendLedger;
    private readonly IProviderEndpointCredentialSource credentialSource;
    private readonly OpenAiCompatibleClientFactory clientFactory;
    private readonly IHttpClientFactory transportFactory;
    private readonly IOutboundOperationRunner operationRunner;
    private readonly IAiProviderHealthRecorder healthRecorder;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly IMailAccountLanguages accountLanguages;
    private readonly IAgentInstructionEnvelope instructionEnvelope;
    private readonly AgentConversationReaders readers;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<AgentConversationAgent> logger;

    /// <summary>Initializes the agent over the declared endpoint and the use cases its tools read and propose through.</summary>
    /// <param name="plan">The validated declaration: which endpoint answers and with which parameters.</param>
    /// <param name="runBounds">What one run may send, call, and consume before it is stopped, in the fast mode and the deep one.</param>
    /// <param name="spendLedger">Counts what every call of the run added to the current period.</param>
    /// <param name="credentialSource">Resolves what a request presents to the endpoint.</param>
    /// <param name="clientFactory">Opens a provider client over the endpoint.</param>
    /// <param name="transportFactory">Opens the transport a run's requests are sent over.</param>
    /// <param name="operationRunner">Applies the provider resilience budget to every call.</param>
    /// <param name="healthRecorder">Records what each call established about the provider.</param>
    /// <param name="egressGuard">Scans every text sent to the provider.</param>
    /// <param name="accountLanguages">Answers the language each account the turn names is kept in.</param>
    /// <param name="instructionEnvelope">Supplies the text every composed agent's instruction is wrapped in.</param>
    /// <param name="readers">The use cases the tools read and propose through.</param>
    /// <param name="loggerFactory">Creates the loggers the framework's own components record through.</param>
    /// <param name="logger">Records the outcome without recording any question, answer, or message.</param>
    public AgentConversationAgent(
        [FromKeyedServices(ChatCapability.Agent)] ChatGenerationPlan plan,
        AgentRunBounds runBounds,
        IMailAnsweringSpendLedger spendLedger,
        IProviderEndpointCredentialSource credentialSource,
        OpenAiCompatibleClientFactory clientFactory,
        IHttpClientFactory transportFactory,
        IOutboundOperationRunner operationRunner,
        IAiProviderHealthRecorder healthRecorder,
        SensitiveContentEgressGuard egressGuard,
        IMailAccountLanguages accountLanguages,
        IAgentInstructionEnvelope instructionEnvelope,
        AgentConversationReaders readers,
        ILoggerFactory loggerFactory,
        ILogger<AgentConversationAgent> logger)
    {
        this.plan = plan;
        this.runBounds = runBounds;
        this.spendLedger = spendLedger;
        this.credentialSource = credentialSource;
        this.clientFactory = clientFactory;
        this.transportFactory = transportFactory;
        this.operationRunner = operationRunner;
        this.healthRecorder = healthRecorder;
        this.egressGuard = egressGuard;
        this.accountLanguages = accountLanguages;
        this.instructionEnvelope = instructionEnvelope;
        this.readers = readers;
        this.loggerFactory = loggerFactory;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<AgentCompletion> ComposeAsync(AgentAnswerBrief brief, AgentAnswerJournal journal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(brief);
        ArgumentNullException.ThrowIfNull(journal);

        var scope = await this.readers.ScopeResolver.ReadableScopeAsync(MailboxReaderKind.ConversationAgent, [], [], JunkMailInclusion.Excluded, cancellationToken);
        var effort = new AgentRunEffort(brief.Question.Mode, this.runBounds, journal);
        var zone = this.readers.Clock.Zone();

        // The turn states the person's own wall clock, so a bound the model writes back is read at that zone's offset.
        var retrieval = new ScopedMailKnowledgeRetrieval(
            this.readers.KnowledgeSearch,
            scope,
            effort.Ledger,
            this.egressGuard,
            zone.Reading(brief.Question.AskedAt));
        var files = await AgentTurnFiles.ReadAsync(this.readers.Files, this.egressGuard, effort.Ledger, brief.Question, cancellationToken);
        var fileTools = new AgentConversationFileTools(journal, this.readers.Files, this.egressGuard, effort.Ledger, brief.Question, files);
        var tools = new AgentConversationTools(
            journal,
            retrieval,
            this.readers,
            this.egressGuard,
            scope.AccountIds,
            fileTools,
            new AgentConversationReporting(brief.Question, brief.Language, files));
        var accounts = await tools.NameAccountsAsync(cancellationToken);
        var languages = MailboxLanguages.Of(accounts.Select(static account => account.Id), this.accountLanguages);
        var stickySession = StickySessionOf(brief.Question.Conversation);

        var answer = await ChatModelFallThrough.RunAsync(
            this.plan,
            this.logger,
            (model, attemptToken) => this.AskAsync(model, brief, accounts, languages, zone, files, tools, journal, effort, stickySession, attemptToken),
            () => !tools.HasProposed,
            cancellationToken);

        await tools.DeclareSearchedAndListedAsync(cancellationToken);

        if (!await journal.ReportAsync(AgentActivity.ComposingAnswer, cancellationToken))
        {
            journal.Stopping.ThrowIfCancellationRequested();
        }

        var cited = tools.Cited.Take(PresentationEvidence.MaxCitations).Select(static citation => citation.Id).ToArray();
        // Cited rather than supported, because the answer is the model's own prose over what it was shown and nothing
        // held it against those sources; a reader checks it by following them.
        var evidence = cited.Length is 0
            ? PresentationEvidence.Unsupported(PresentationFreshness.Unknown)
            : new PresentationEvidence(PresentationSupport.Cited, cited, PresentationFreshness.Unknown);
        var confidence = PresentationConfidenceCeiling.Cap(
            tools.ReportedConfidence ?? PresentationConfidence.Moderate,
            evidence.Support);

        await journal.ComposeAsync(new AnswerBlock(evidence, answer, confidence, LinksIn(answer, tools)), cancellationToken);

        AgentConversationEvents.LogAnswered(this.logger, this.plan.Endpoint.Alias, cited.Length);

        return new AgentCompletion(tools.FollowUps, effort.Ceiling);
    }

    /// <summary>Turns an answer into the text a block may carry, or refuses it as no answer at all.</summary>
    /// <param name="text">The text the run's last turn carried.</param>
    /// <returns>The text a block carries, or <see langword="null" /> where none is left, which the run fails as an empty answer.</returns>
    /// <remarks>
    /// Its pictures go after its control characters, because dropping one between a mark and its bracket would rebuild a
    /// picture the neutralising had already passed over, and before it is cut, so the cut bounds the text that is published.
    /// </remarks>
    internal static PresentationText? Presentable(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cleaned = AnswerMarkup.WithoutPictures(new string([.. text.Where(static character => !char.IsControl(character) || character is '\n' or '\r' or '\t')])).Trim();
        var bounded = cleaned.Length <= PresentationText.MaxLength
            ? cleaned
            : MailTextBounds.TruncateAtTextElementBoundary(cleaned, PresentationText.MaxLength);

        return PresentationText.TryCreate(bounded, out var presentable) ? presentable : null;
    }

    /// <summary>Judges every link an answer writes against the mail the run showed the model.</summary>
    /// <param name="answer">The answer as its block carries it.</param>
    /// <param name="tools">The run's tools, which hold every message it showed.</param>
    /// <returns>The links, judged.</returns>
    internal static IReadOnlyList<AnswerLink> LinksIn(PresentationText answer, AgentConversationTools tools) =>
        AnswerMarkup.LinksIn(answer.Value, tools.ShownMail);

    /// <summary>Composes what the model is sent: every earlier turn of the conversation, then the question's own turn.</summary>
    /// <param name="brief">The question, the language it is answered in, and the conversation before it.</param>
    /// <param name="accounts">The accounts the turn names, which are the ones a tool the model is offered takes.</param>
    /// <param name="languages">The languages those accounts are kept in, each once, which the turn names.</param>
    /// <param name="zone">The zone the person's own days are read in, which the turn names.</param>
    /// <param name="files">What the turn is given of the files the conversation holds, or <see langword="null" /> where it holds none.</param>
    /// <param name="maximumImageOctets">The image octets the answering model takes in one request, which decides which pictures the question carries it is shown.</param>
    /// <returns>The conversation, oldest turn first, with one turn per picture shown just before the question's own.</returns>
    internal static IReadOnlyList<ChatMessage> ComposeMessages(
        AgentAnswerBrief brief,
        IReadOnlyList<AgentNamedAccount> accounts,
        IReadOnlyList<MailAccountLanguage> languages,
        UserTimeZone zone,
        AgentTurnFiles? files = null,
        int maximumImageOctets = 0) =>
        ComposeMessages(brief, accounts, languages, zone, files ?? AgentTurnFiles.None, maximumImageOctets, brief.History, brief.Question.Text.Value);

    /// <summary>Composes the conversation from a history and a question standing in for the ones the brief carries.</summary>
    private static IReadOnlyList<ChatMessage> ComposeMessages(
        AgentAnswerBrief brief,
        IReadOnlyList<AgentNamedAccount> accounts,
        IReadOnlyList<MailAccountLanguage> languages,
        UserTimeZone zone,
        AgentTurnFiles files,
        int maximumImageOctets,
        IReadOnlyList<AgentHistoryTurn> history,
        string question) =>
    [
        .. history.Select(AgentHistoryDocument.MessageOf),
        .. files.Pictures(maximumImageOctets),
        new ChatMessage(
            ChatRole.User,
            AgentConversationComposition.ComposeTurn(
                brief.Question.AskedAt,
                zone,
                accounts,
                languages,
                brief.Question.Scope,
                brief.NamedEmails,
                question,
                document => files.Write(document, maximumImageOctets))),
    ];

    /// <summary>Derives the sticky session every request of one conversation is routed by.</summary>
    /// <param name="conversation">The conversation the turn belongs to.</param>
    /// <returns>A key that is the same for every turn of the conversation and different for every other one.</returns>
    /// <remarks>
    /// <para>
    /// The conversation rather than the turn or the run, because it is what grows: every turn resends the history before
    /// it, and a compaction rewrites that history's opening turns. OpenRouter's own routing key hashes those opening
    /// turns, so it would move after the first turn and after every compaction, and each move sends the whole prefix to
    /// a provider whose cache has never seen it. Nothing wider is grouped either — two conversations never share a key,
    /// whoever holds them.
    /// </para>
    /// <para>
    /// A hash rather than the identifier itself, so the gateway is handed a routing key and never a name this
    /// deployment uses for the conversation in its own routes and records.
    /// </para>
    /// </remarks>
    internal static string StickySessionOf(AgentConversationId conversation) =>
        Convert.ToHexStringLower(SHA256.HashData(conversation.Value.ToByteArray()));

    /// <summary>Scans every text the person or the Agent wrote, which is what leaves this deployment on each call, and composes the conversation from what the scan let through.</summary>
    /// <remarks>
    /// Each text is scanned as the value it is, before the question's turn is written around it, so no redaction can
    /// reach into the elements the turn is composed of. The files' names and words were guarded as they were read.
    /// </remarks>
    private async Task<IReadOnlyList<ChatMessage>> GuardAsync(
        AgentAnswerBrief brief,
        IReadOnlyList<AgentNamedAccount> accounts,
        IReadOnlyList<MailAccountLanguage> languages,
        UserTimeZone zone,
        AgentTurnFiles files,
        int maximumImageOctets,
        CancellationToken cancellationToken)
    {
        var history = await AgentHistoryDocument.GuardAsync(this.egressGuard, brief.History, cancellationToken);
        var question = await this.egressGuard.GuardAsync(
            SensitiveContentEgressPoint.ChatPrompt,
            brief.Question.Text.Value,
            cancellationToken);

        return ComposeMessages(brief, accounts, languages, zone, files, maximumImageOctets, history, question);
    }

    private async Task<PresentationText> AskAsync(
        ChatGenerationPlan model,
        AgentAnswerBrief brief,
        IReadOnlyList<AgentNamedAccount> accounts,
        IReadOnlyList<MailAccountLanguage> languages,
        UserTimeZone zone,
        AgentTurnFiles files,
        AgentConversationTools tools,
        AgentAnswerJournal journal,
        AgentRunEffort effort,
        string stickySession,
        CancellationToken cancellationToken)
    {
        // Against this model's own bounds, and before the scan, so a conversation refused by a ceiling costs no scan.
        ChatRequestBounds.RequireForAttempt(ComposeMessages(brief, accounts, languages, zone, files, model.MaximumRequestImageOctets), model);
        tools.ForgetEndingReports();

        var guarded = await this.GuardAsync(brief, accounts, languages, zone, files, model.MaximumRequestImageOctets, cancellationToken);

        var endpoint = model.Endpoint;

        using var credential = await ChatModelCredential.ResolveAsync(this.credentialSource, endpoint, cancellationToken);
        using var transport = this.transportFactory.CreateClient(ProviderChatModelClient.TransportName);
        using var providerClient = this.clientFactory.OpenChatClient(endpoint, credential, transport, stickySession);
        using var resilientClient = new ResilientChatClient(
            providerClient,
            endpoint,
            model.RequestTimeout,
            this.operationRunner,
            this.healthRecorder,
            this.loggerFactory.CreateLogger<ResilientChatClient>());
        await using var budgetedClient = new BudgetedChatClient(resilientClient, effort.Ledger, this.spendLedger);
        using var recordedClient = new RecordedChatClient(budgetedClient, journal);
        using var steeredClient = new SteeredChatClient(recordedClient, journal, this.egressGuard);
        using var lastCallClient = new LastCallChatClient(steeredClient, effort);

        var agent = AgentConversationComposition.Compose(lastCallClient, model, brief.Language, effort.Offer(tools.Create()), this.instructionEnvelope, this.loggerFactory);
        var response = await agent.RunAsync(ChatConversationMapping.ToProviderConversation(guarded), session: null, options: null, cancellationToken);

        if (Presentable(response.Text) is not { } answer)
        {
            AgentConversationEvents.LogProducedNoAnswer(this.logger, endpoint.Alias);

            throw new ChatGenerationFailedException(endpoint.Alias, ChatGenerationFailure.AnswerEmpty);
        }

        return answer;
    }
}
