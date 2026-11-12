// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using MailFathom.Application.Emails.ReplyDrafts;
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Egress;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.ReplyDrafts;

/// <summary>Writes a reply by putting one conversation, one manner, and one ask to the composed drafting agent, and rewrites passages of a draft through the rewriting agent.</summary>
/// <remarks>
/// <para>
/// One call, no tools, and one conversation. What leaves this deployment is the subject, each message's author name,
/// when it was written, what it added and the words its attachments yielded under their file names, whether the person
/// chose it, the names of the people in it, a few openings of the person's own recent sent mail, and whatever they
/// typed — every text of them guarded first — and whether the account it is sent from appends the person's footer, which
/// is a yes or a no and never the footer, and the host and words of each link a draft may carry. <b>No address leaves
/// at all</b>: the people and the links are numbered, a proposal and a link name a number, and the address it resolves
/// to is looked up here afterwards.
/// </para>
/// <para>
/// A rewriting sends less: the draft's own text with the passages marked in it, and what the person asked, every text of
/// them guarded first. Nothing is read from a mailbox for it, and nothing is numbered but the passages themselves.
/// </para>
/// <para>
/// It opens no chat call of its own. The agent is composed by <see cref="AgentComposition" /> like every other
/// operation in this product, so the instruction is carried where no turn can reach it, the tool set is the capability,
/// and the registered instruction envelope is wrapped around the instruction rather than written into it.
/// </para>
/// <para>
/// Every drafting is admitted against the same period ceilings a question is and counted by the same two ledgers, so
/// this is bounded rather than a second, unmetered way of spending a deployment's allowance — and it competes with
/// questions, with enrichment, and with a conversation's state for that allowance, which is the trade an operator makes
/// when they turn it on. A refused admission travels rather than falling back, because a person pressing a button the
/// operator has already spent the allowance on has to be told so.
/// </para>
/// <para>
/// A provider that fails, times out, or answers unreadably is not a failure a person is shown. What they have for that
/// case is the composer they were already looking at, which is the same composer a deployment that drafts nothing
/// serves.
/// </para>
/// <para>
/// Each drafting opens its own credential, transport, chat client, and agent, and releases all four with it. That is
/// the lifetime every other agent here uses and for the same reasons: a rotated key is picked up by the next drafting
/// rather than at the next restart, and one correspondence's text cannot outlive the call that sent it.
/// </para>
/// </remarks>
internal sealed class ReplyDraftAgent : IReplyDraftWriter
{
    private readonly ChatGenerationPlan plan;
    private readonly MailAnsweringRunLedger runLedger;
    private readonly IMailAnsweringSpendLedger spendLedger;
    private readonly IProviderEndpointCredentialSource credentialSource;
    private readonly OpenAiCompatibleClientFactory clientFactory;
    private readonly IHttpClientFactory transportFactory;
    private readonly IOutboundOperationRunner operationRunner;
    private readonly IAiProviderHealthRecorder healthRecorder;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly IAgentInstructionEnvelope instructionEnvelope;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<ReplyDraftAgent> logger;

    /// <summary>Creates the drafting one composer reaches the model through.</summary>
    /// <param name="plan">The generation parameters this deployment configured.</param>
    /// <param name="runLedger">Counts what this request has spent and refuses the call that would take it past its ceiling.</param>
    /// <param name="spendLedger">Counts what the current period has spent across every run.</param>
    /// <param name="credentialSource">Resolves the endpoint's credential for the one call.</param>
    /// <param name="clientFactory">Opens the provider client.</param>
    /// <param name="transportFactory">Supplies the named outbound transport that client speaks over.</param>
    /// <param name="operationRunner">Applies this deployment's outbound resilience to the call.</param>
    /// <param name="healthRecorder">Records what the endpoint did, so the availability gate can read it.</param>
    /// <param name="egressGuard">Withholds from the provider whatever this deployment withholds.</param>
    /// <param name="instructionEnvelope">The preamble and postamble every agent here carries.</param>
    /// <param name="loggerFactory">The factory the composed agent and the resilience decorator log through.</param>
    /// <param name="logger">Records the outcome without recording the reply or the correspondence.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public ReplyDraftAgent(
        [FromKeyedServices(ChatCapability.ReplyDrafting)] ChatGenerationPlan plan,
        MailAnsweringRunLedger runLedger,
        IMailAnsweringSpendLedger spendLedger,
        IProviderEndpointCredentialSource credentialSource,
        OpenAiCompatibleClientFactory clientFactory,
        IHttpClientFactory transportFactory,
        IOutboundOperationRunner operationRunner,
        IAiProviderHealthRecorder healthRecorder,
        SensitiveContentEgressGuard egressGuard,
        IAgentInstructionEnvelope instructionEnvelope,
        ILoggerFactory loggerFactory,
        ILogger<ReplyDraftAgent> logger)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(runLedger);
        ArgumentNullException.ThrowIfNull(spendLedger);
        ArgumentNullException.ThrowIfNull(credentialSource);
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(transportFactory);
        ArgumentNullException.ThrowIfNull(operationRunner);
        ArgumentNullException.ThrowIfNull(healthRecorder);
        ArgumentNullException.ThrowIfNull(egressGuard);
        ArgumentNullException.ThrowIfNull(instructionEnvelope);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(logger);

        this.plan = plan;
        this.runLedger = runLedger;
        this.spendLedger = spendLedger;
        this.credentialSource = credentialSource;
        this.clientFactory = clientFactory;
        this.transportFactory = transportFactory;
        this.operationRunner = operationRunner;
        this.healthRecorder = healthRecorder;
        this.egressGuard = egressGuard;
        this.instructionEnvelope = instructionEnvelope;
        this.loggerFactory = loggerFactory;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<ReplyDraft> WriteAsync(ReplyDraftBrief brief, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(brief);

        var sources = brief.Sources;

        // Admitted against the period before anything is composed or scanned, so a deployment that has already spent
        // its allowance costs nothing to refuse — and so a drafting takes its place in the same count a question does
        // rather than being a second, unmetered way of reaching the provider. The refusal travels because a person
        // pressed a button: falling back here would leave them typing into a composer and waiting for a draft that
        // this deployment had already decided not to pay for.
        if (!await this.spendLedger.TryAdmitRunAsync(cancellationToken))
        {
            throw MailAnsweringBudgetExhaustedException.PeriodSpent();
        }

        var turn = await ComposeTurnAsync(brief, this.egressGuard, this.plan, cancellationToken);

        ChatRequestBounds.Require(
            [new ChatMessage(ChatRole.User, turn.Text)],
            this.plan.MaximumMessagesPerRequest,
            this.plan.MaximumRequestCharacters,
            this.plan.MaximumRequestImageOctets);

        var answer = await this.AskAsync(
            turn.Text,
            async (chatClient, model, attemptToken) => (await ReplyDraftAgentComposition
                .Compose(chatClient, model, brief.Language, turn, this.instructionEnvelope, this.loggerFactory)
                .RunAsync(turn.Text, session: null, options: null, attemptToken)).Text,
            cancellationToken);
        var draft = ReplyDraftReading.Read(answer?.Text, sources.Messages, sources.Participants, turn.Links);

        // The model that answered where one did, and the model the brief was put to where none could.
        var answeringAlias = answer?.Alias ?? this.plan.Endpoint.Alias;

        if (draft.WasWritten)
        {
            var unsupportedClaimCount = draft.Claims.Count(static claim => !claim.IsSupported);

            ReplyDraftEvents.LogDrafted(
                this.logger,
                answeringAlias,
                sources.Messages.Count,
                draft.Claims.Count,
                unsupportedClaimCount,
                draft.ProposedRecipients.Count);
        }
        else
        {
            ReplyDraftEvents.LogAnswerUnreadable(this.logger, answeringAlias);
        }

        return draft;
    }

    /// <inheritdoc />
    public async Task<PassageRewrite> RewritePassagesAsync(PassageRewriteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Admitted first and refused aloud, for the reasons a drafting is: a rewriting is one more call charged to the
        // same allowance, and the person pressed a button.
        if (!await this.spendLedger.TryAdmitRunAsync(cancellationToken))
        {
            throw MailAnsweringBudgetExhaustedException.PeriodSpent();
        }

        // Not held to the main model's request bound here as a drafting's turn is, because nothing in it could be cut
        // without losing a passage: a model too narrow for it refuses the attempt, which answers nothing rather than
        // failing the request.
        var turn = await ComposeRewritingTurnAsync(request, this.egressGuard, cancellationToken);
        var passageCount = request.Passages.Count;

        var answer = await this.AskAsync(
            turn,
            async (chatClient, model, attemptToken) => (await ReplyDraftAgentComposition
                .ComposeRewriting(chatClient, model, this.instructionEnvelope, this.loggerFactory)
                .RunAsync(turn, session: null, options: null, attemptToken)).Text,
            cancellationToken);
        var rewrite = PassageRewriteReading.Read(answer?.Text, passageCount);
        var answeringAlias = answer?.Alias ?? this.plan.Endpoint.Alias;

        if (rewrite.WasWritten)
        {
            ReplyDraftEvents.LogPassagesRewritten(this.logger, answeringAlias, passageCount);
        }
        else
        {
            ReplyDraftEvents.LogRewriteUnreadable(this.logger, answeringAlias);
        }

        return rewrite;
    }

    /// <summary>Composes the turn one rewriting sends: the draft with its passages inline, and the ask, every text of them guarded.</summary>
    /// <param name="request">The draft, the passages of it to rewrite, and how they should change.</param>
    /// <param name="egressGuard">Scans every text somebody wrote before it is composed.</param>
    /// <param name="cancellationToken">Withdraws the scan.</param>
    /// <returns>The turn text.</returns>
    /// <remarks>
    /// The draft is cut at its passages before anything is scanned, and each stretch between them and each passage is
    /// guarded on its own: a redaction may be longer or shorter than what it replaced, so a guarded whole read again at
    /// the request's offsets would put a passage's edge in the wrong place. An empty stretch — a passage at the very
    /// start or end of the draft, or two passages meeting — carries nothing to scan and is not offered.
    /// </remarks>
    internal static async Task<string> ComposeRewritingTurnAsync(
        PassageRewriteRequest request,
        SensitiveContentEgressGuard egressGuard,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var text = request.Text;
        var stretches = new List<string>(request.Passages.Count + 1);
        var passages = new List<string>(request.Passages.Count);
        var stretchStart = 0;

        foreach (var passage in request.Passages)
        {
            stretches.Add(await GuardedStretchAsync(egressGuard, text[stretchStart..passage.Start], cancellationToken));
            passages.Add(await GuardedAsync(egressGuard, text.Substring(passage.Start, passage.Length), cancellationToken));
            stretchStart = passage.Start + passage.Length;
        }

        stretches.Add(await GuardedStretchAsync(egressGuard, text[stretchStart..], cancellationToken));

        return PassageRewriteInstructions.ComposeRewritingTurn(new GuardedPassageRewriteTurn(
            stretches,
            passages,
            await GuardedAsync(egressGuard, request.Instruction, cancellationToken)));
    }

    private static Task<string> GuardedStretchAsync(
        SensitiveContentEgressGuard egressGuard,
        string stretch,
        CancellationToken cancellationToken) =>
        stretch.Length is 0 ? Task.FromResult(stretch) : GuardedAsync(egressGuard, stretch, cancellationToken);

    /// <summary>Composes the turn one drafting sends: the brief, guarded and cut to what one call may carry.</summary>
    /// <param name="brief">The correspondence, what the person selected and typed, and the language the draft is for.</param>
    /// <param name="egressGuard">Scans every text somebody wrote before it is composed.</param>
    /// <param name="plan">The model the turn is sent to, whose request bound the turn is cut to.</param>
    /// <param name="cancellationToken">Withdraws the scan.</param>
    /// <returns>The turn, and the links, messages, and people it numbered, which are the only ones a draft may cite.</returns>
    /// <remarks>
    /// A bound rather than a refusal, applied by the document as it is written, so a bounded turn is still one whose
    /// every element is closed.
    /// </remarks>
    internal static async Task<ReplyDraftTurn> ComposeTurnAsync(
        ReplyDraftBrief brief,
        SensitiveContentEgressGuard egressGuard,
        ChatGenerationPlan plan,
        CancellationToken cancellationToken)
    {
        var text = ReplyDraftInstructions.ComposeDraftingTurn(
            await GuardedTurnAsync(brief, egressGuard, cancellationToken),
            plan.MaximumRequestCharacters,
            out var written);

        return new ReplyDraftTurn(
            text,
            [.. brief.Links.Take(written.Links)],
            [.. brief.Sources.Messages.Take(written.Messages).Select(static message => message.Position)],
            [.. brief.Sources.Participants.Take(written.People).Select(static participant => participant.Position)]);
    }

    /// <summary>Puts every text of the drafting through the egress guard before any of it is composed into a turn.</summary>
    /// <remarks>
    /// The correspondence, the names, the person's own sent mail, what they typed, and each link's host and words are
    /// all somebody's data and are scanned like every other text this deployment sends. The positions and the instants
    /// are this deployment's own and carry nothing anybody wrote, and an address — a person's or a link's — is not
    /// offered because none is sent.
    /// </remarks>
    private static async Task<GuardedDraftingTurn> GuardedTurnAsync(
        ReplyDraftBrief brief,
        SensitiveContentEgressGuard egressGuard,
        CancellationToken cancellationToken)
    {
        var sources = brief.Sources;
        var messages = new List<GuardedDraftingMessage>(sources.Messages.Count);

        foreach (var message in sources.Messages)
        {
            var attachments = new List<GuardedDraftingAttachment>(message.Attachments.Count);

            foreach (var attachment in message.Attachments)
            {
                attachments.Add(new GuardedDraftingAttachment(
                    await GuardedOptionalAsync(egressGuard, attachment.FileName, cancellationToken),
                    attachment.Kind,
                    await GuardedAsync(egressGuard, attachment.Text, cancellationToken)));
            }

            messages.Add(new GuardedDraftingMessage(
                message.Position,
                await GuardedOptionalAsync(egressGuard, message.AuthorDisplayName, cancellationToken),
                message.SentAt,
                await GuardedAsync(egressGuard, message.Text, cancellationToken),
                message.IsChosen,
                attachments));
        }

        var people = new List<GuardedDraftingPerson>(sources.Participants.Count);

        foreach (var participant in sources.Participants)
        {
            people.Add(new GuardedDraftingPerson(
                participant.Position,
                await GuardedOptionalAsync(egressGuard, participant.Address.DisplayName, cancellationToken)));
        }

        var styleSamples = new List<string>(sources.StyleSamples.Count);

        foreach (var sample in sources.StyleSamples)
        {
            styleSamples.Add(await GuardedAsync(egressGuard, sample, cancellationToken));
        }

        var links = new List<GuardedDraftingLink>(brief.Links.Count);

        foreach (var link in brief.Links)
        {
            links.Add(new GuardedDraftingLink(
                link.Position,
                await GuardedOptionalAsync(egressGuard, link.Link.Host, cancellationToken),
                await GuardedOptionalAsync(egressGuard, link.Words, cancellationToken)));
        }

        return new GuardedDraftingTurn(
            await GuardedOptionalAsync(egressGuard, sources.Subject, cancellationToken),
            people,
            messages,
            styleSamples,
            await GuardedOptionalAsync(egressGuard, brief.Selection, cancellationToken),
            await GuardedOptionalAsync(egressGuard, brief.Instruction, cancellationToken),
            brief.ClosesWithFooter)
        {
            Links = links,
        };
    }

    private static Task<string> GuardedAsync(
        SensitiveContentEgressGuard egressGuard,
        string text,
        CancellationToken cancellationToken) =>
        egressGuard.GuardAsync(SensitiveContentEgressPoint.ChatPrompt, text, cancellationToken);

    private static Task<string?> GuardedOptionalAsync(
        SensitiveContentEgressGuard egressGuard,
        string? text,
        CancellationToken cancellationToken) =>
        egressGuard.GuardOptionalAsync(SensitiveContentEgressPoint.ChatPrompt, text, cancellationToken);

    /// <summary>Makes the one provider call, answering with nothing where it failed.</summary>
    /// <remarks>
    /// <para>
    /// A failure is swallowed here rather than raised because the caller already has an outcome for that case — the
    /// composer as it was — and the endpoint's own health record, which the resilience decorator wrote before this
    /// returned, is what a deployment's availability gate reads.
    /// </para>
    /// <para>
    /// A spend ceiling is the exception and travels, so the boundary above can say which ceiling stopped a drafting
    /// rather than leaving somebody pressing a button that quietly does nothing. A cancellation stays outside, being
    /// the person withdrawing the request rather than a provider failing to answer it.
    /// </para>
    /// </remarks>
    private async Task<ChatModelAnswer?> AskAsync(
        string turnText,
        Func<BudgetedChatClient, ChatGenerationPlan, CancellationToken, Task<string?>> runAgentAsync,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ChatModelFallThrough.RunAsync(
                this.plan,
                this.logger,
                (model, attemptToken) => this.AskModelAsync(model, turnText, runAgentAsync, attemptToken),
                cancellationToken);
        }
        catch (ChatGenerationFailedException failure)
        {
            // The alias the chain's last model failed under, which is what a line about this outage has to name: the
            // model asked first has a fallback behind it, so naming that one sends a reader to an endpoint that may be
            // working. There is no text, which is what tells the caller nothing answered.
            return new ChatModelAnswer(failure.EndpointAlias, Text: null);
        }
    }

    /// <summary>Asks one model of the chain, letting a failure out so the fallback behind it can be tried.</summary>
    /// <param name="model">The model of the chain this attempt is against.</param>
    /// <param name="turnText">The turn the agent is put, which this model's own bounds are checked against.</param>
    /// <param name="runAgentAsync">Composes the agent of the act being asked for over the client this attempt opened, and runs it.</param>
    /// <param name="cancellationToken">Withdraws the attempt.</param>
    private async Task<ChatModelAnswer> AskModelAsync(
        ChatGenerationPlan model,
        string turnText,
        Func<BudgetedChatClient, ChatGenerationPlan, CancellationToken, Task<string?>> runAgentAsync,
        CancellationToken cancellationToken)
    {
        // Against this model's own bounds rather than the main model's, because a fallback may be declared
        // narrower and a conversation too wide for it is refused here rather than sent and billed for.
        ChatRequestBounds.RequireForAttempt([new ChatMessage(ChatRole.User, turnText)], model);

        var endpoint = model.Endpoint;

        using var credential = await ChatModelCredential.ResolveAsync(this.credentialSource, endpoint, cancellationToken);
        using var transport = this.transportFactory.CreateClient(ProviderChatModelClient.TransportName);
        using var providerClient = this.clientFactory.OpenChatClient(endpoint, credential, transport);

        using var resilientClient = new ResilientChatClient(
            providerClient,
            endpoint,
            model.RequestTimeout,
            this.operationRunner,
            this.healthRecorder,
            this.loggerFactory.CreateLogger<ResilientChatClient>());

        // Outside the resilience decorator rather than inside it, so a call this deployment's own ceiling refused
        // never reaches the endpoint's circuit, its concurrency budget, or its health record. The two ledgers are
        // this request's and the period's: what an operator pays a provider does not change because the call was
        // started from a composer.
        await using var chatClient = new BudgetedChatClient(resilientClient, this.runLedger, this.spendLedger);

        return new ChatModelAnswer(endpoint.Alias, await runAgentAsync(chatClient, model, cancellationToken));
    }
}
