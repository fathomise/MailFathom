// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Accounts.Custody;
using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Folders;
using MailFathom.Application.Folders.Local;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Mail.Mutations.Authoring;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Application.Persistence;
using MailFathom.Application.Preferences;
using MailFathom.Application.Rules.Actions;
using MailFathom.Application.Spam;
using MailFathom.Application.Spam.Actions;
using MailFathom.Application.Spam.Gating;
using MailFathom.Application.Spam.Marking;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Spam;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the mutation routes accept, what they refuse before a use case is reached, and what they put on the wire.</summary>
/// <remarks>
/// The decisions behind the routes — whose mail may be changed, which folder a name means, what may still be withdrawn
/// — are covered where they are taken. What is asserted here is the boundary's own half: the bounds a batch is held
/// to, the request identity a repeat is recognized by, and the translation of every answer onto a published name.
/// </remarks>
public sealed class ClientMailMutationsEndpointTests
{
    private static readonly MailFolderId ArchiveId = MailFolderId.Create(Guid.Parse("0199c1a2-0000-7000-8000-000000000006"));
    private static readonly Guid Message = Guid.Parse("0199a0c0-0000-7000-8000-0000000090a0");

    private static readonly MailAccountId ServedAccount = MailAccountId.Create("work");

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private static readonly MailFolderAlias Archive = MailFolderAlias.Create("Archive");

    private static readonly MailboxMutationRequester Requester = MailboxMutationRequester.Command("call-1");

    private static readonly DateTimeOffset RecordedAt = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly IMailboxMutationRecordStore records = Substitute.For<IMailboxMutationRecordStore>();

    private readonly IEmailSpamClassificationStore spamClassifications = Substitute.For<IEmailSpamClassificationStore>();

    /// <summary>The paths a client appends to the address it was configured with, pinned because the client composes them from constants of its own.</summary>
    [Fact]
    public void Routes_AreThePathsAClientComposes()
    {
        Assert.Equal("/mutations", ClientMailMutationsEndpoint.MutationsRoute);
        Assert.Equal("/mutations/deletes", ClientMailMutationsEndpoint.DeleteMutationsRoute);
        Assert.Equal("/mutations/deletes/withdrawals", ClientMailMutationsEndpoint.DeleteWithdrawalsRoute);
        Assert.Equal("/mutations/deletes/releases", ClientMailMutationsEndpoint.DeleteReleasesRoute);
        Assert.Equal("/mutations/flags", ClientMailMutationsEndpoint.FlagMutationsRoute);
        Assert.Equal("/mutations/flags/withdrawals", ClientMailMutationsEndpoint.FlagWithdrawalsRoute);
        Assert.Equal("/mutations/moves", ClientMailMutationsEndpoint.MoveMutationsRoute);
        Assert.Equal("/mutations/moves/withdrawals", ClientMailMutationsEndpoint.MoveWithdrawalsRoute);
        Assert.Equal("/mutations/spam", ClientMailSpamMarksEndpoint.SpamMarksRoute);
    }

    /// <summary>A read naming nothing is a request with no question in it, and answering it with everything would be the unbounded read this surface refuses.</summary>
    [Fact]
    public async Task ReadChangesAsync_NoRecordNamed_IsRefused()
    {
        // Act
        var result = await ClientMailMutationsEndpoint.ReadChangesAsync(
            [],
            this.ProgressReader(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, RefusalOf(result).StatusCode);
    }

    /// <summary>The caller supplies the identities, so without a ceiling the size of the answer would be the caller's to choose.</summary>
    [Fact]
    public async Task ReadChangesAsync_MoreRecordsThanTheReadBound_IsRefused()
    {
        // Arrange
        var asked = Enumerable
            .Range(0, ClientMailMutationsEndpoint.MaximumChangesPerRead + 1)
            .Select(_ => Guid.CreateVersion7())
            .ToArray();

        // Act
        var result = await ClientMailMutationsEndpoint.ReadChangesAsync(
            asked,
            this.ProgressReader(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, RefusalOf(result).StatusCode);
    }

    /// <summary>
    /// A read names its records in the request line, which Kestrel refuses over 8192 bytes before any handler sees it,
    /// so the bound the surface publishes has to be one a request carrying it actually survives.
    /// </summary>
    [Fact]
    public void MaximumChangesPerRead_ARequestNamingThatMany_FitsTheRequestLineKestrelAdmits()
    {
        // Arrange
        const int KestrelMaxRequestLineBytes = 8192;
        var line = "GET /api/client" + ClientMailMutationsEndpoint.MutationsRoute + "?"
            + string.Join(
                '&',
                Enumerable
                    .Range(0, ClientMailMutationsEndpoint.MaximumChangesPerRead)
                    .Select(_ => $"record={Guid.CreateVersion7()}"))
            + " HTTP/1.1";

        // Assert
        Assert.True(line.Length < KestrelMaxRequestLineBytes, $"The request line is {line.Length} bytes.");
    }

    /// <summary>Every fact the use case read reaches the wire, because each of them is one a person acts on rather than waits through.</summary>
    [Fact]
    public void ChangesResponse_AChangeInFlight_CarriesEveryFactAClientActsOn()
    {
        // Arrange
        var recordId = MailboxMutationRecordId.Create(Guid.CreateVersion7());
        var progress = new MailboxChangeProgress(
            recordId,
            StoredEmailId.Create(Message),
            MailboxMutation.Relocate,
            MailboxMutationLifecycle.Converging,
            IsOutcomeUnknown: true,
            AttemptCount: 2,
            MailFathomErrorCode.MailboxUnavailable,
            RecordedAt,
            RecordedAt.AddMinutes(1));

        // Act
        var response = ClientMailChangesResponse.For([progress]);

        // Assert
        var change = Assert.Single(response.Changes);

        Assert.Equal(recordId.Value, change.RecordId);
        Assert.Equal(Message, change.StoredEmailId);
        Assert.Equal(MailboxMutation.Relocate.Name, change.Mutation);
        Assert.Equal(MailboxMutationLifecycle.Converging.Name, change.State);
        Assert.True(change.OutcomeUnknown);
        Assert.Equal(2, change.AttemptCount);
        Assert.Equal(MailFathomErrorCode.MailboxUnavailable.Value, change.LastFailure);
        Assert.Equal(RecordedAt, change.RecordedAt);
        Assert.Equal(RecordedAt.AddMinutes(1), change.StateChangedAt);
    }

    /// <summary>A folder withheld from AI is still the person's, so a change they made to its mail is reported to them like any other.</summary>
    [Fact]
    public async Task ReadChangesAsync_AChangeToMailInAFolderWithheldFromAI_ReportsItToThePerson()
    {
        // Arrange
        var held = this.Holding(FlagRequest());

        // Act
        var result = await ClientMailMutationsEndpoint.ReadChangesAsync(
            [held.Id.Value],
            this.ProgressReader(InboxWithheldFromAI()),
            TestContext.Current.CancellationToken);

        // Assert
        var change = Assert.Single(Assert.IsType<Ok<ClientMailChangesResponse>>(result.Result).Value!.Changes);

        Assert.Equal(held.Id.Value, change.RecordId);
    }

    /// <summary>A batch naming no message asks for nothing, and a boundary that accepted it would answer an empty request with a success.</summary>
    [Fact]
    public async Task SubmitFlagChangesAsync_ABatchNamingNoMessage_IsRefused()
    {
        // Act
        var result = await ClientMailMutationsEndpoint.SubmitFlagChangesAsync(
            new ClientMailFlagChangesRequest(RequestId: null, []),
            this.FlagRecorder(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, RefusalOf(result).StatusCode);
    }

    /// <summary>A body that did not parse is refused rather than read as an empty batch, which is the same answer for the same reason.</summary>
    [Fact]
    public async Task SubmitFlagChangesAsync_NoBody_IsRefused()
    {
        // Act
        var result = await ClientMailMutationsEndpoint.SubmitFlagChangesAsync(
            request: null,
            this.FlagRecorder(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, RefusalOf(result).StatusCode);
    }

    /// <summary>The bound is what stops one request from becoming an unbounded number of durable writes.</summary>
    [Fact]
    public async Task SubmitFlagChangesAsync_MoreMessagesThanTheBatchBound_IsRefused()
    {
        // Arrange
        var changes = Enumerable
            .Range(0, ClientMailMutationsEndpoint.MaximumChangesPerRequest + 1)
            .Select(_ => new ClientMailFlagChangeRequest(
                Guid.CreateVersion7(),
                new ClientMailFlagStateRequest(Seen: true, Flagged: null),
                Tags: null))
            .ToArray();

        // Act
        var result = await ClientMailMutationsEndpoint.SubmitFlagChangesAsync(
            new ClientMailFlagChangesRequest(RequestId: null, changes),
            this.FlagRecorder(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, RefusalOf(result).StatusCode);
    }

    /// <summary>
    /// The request identity is what decides whether asking again is the same request, so it is held to a length and a
    /// character set here rather than reaching the record it would be stored on.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("has\0a control character")]
    public async Task SubmitFlagChangesAsync_ARequestIdentityThisSurfaceCannotStore_IsRefused(string requestId)
    {
        // Act
        var result = await ClientMailMutationsEndpoint.SubmitFlagChangesAsync(
            new ClientMailFlagChangesRequest(
                requestId,
                [new ClientMailFlagChangeRequest(Message, new ClientMailFlagStateRequest(true, null), null)]),
            this.FlagRecorder(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, RefusalOf(result).StatusCode);
    }

    /// <summary>A batch naming no move asks for nothing, exactly as one naming no flag change does.</summary>
    [Fact]
    public async Task SubmitMovesAsync_ABatchNamingNoMessage_IsRefused()
    {
        // Act
        var result = await ClientMailMutationsEndpoint.SubmitMovesAsync(
            new ClientMailMovesRequest(RequestId: null, []),
            this.RelocationRecorder(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, RefusalOf(result).StatusCode);
    }

    /// <summary>
    /// The route's own translation of a change the use case wrote down, driven through the use case rather than through
    /// the response builder: what a defect would swap here is which answer reaches the wire, and only a call that
    /// actually records something proves the recorded one does.
    /// </summary>
    [Fact]
    public async Task SubmitFlagChangesAsync_AChangeTheUseCaseRecords_PutsTheRecordOnTheWire()
    {
        // Arrange
        this.RecordEveryRequest();

        // Act
        var result = await ClientMailMutationsEndpoint.SubmitFlagChangesAsync(
            new ClientMailFlagChangesRequest(
                "call-1",
                [new ClientMailFlagChangeRequest(Message, new ClientMailFlagStateRequest(Seen: true, null), null)]),
            this.FlagRecorder(TargetInInbox()),
            TestContext.Current.CancellationToken);

        // Assert
        var change = Assert.Single(Assert.IsType<Ok<ClientMailFlagChangesResponse>>(result.Result).Value!.Results);

        Assert.Equal(Message, change.StoredEmailId);
        Assert.Equal(ClientMailChangeOutcomes.Recorded, change.Outcome);
        Assert.Null(change.Detail);
        Assert.NotEmpty(change.Changes);
    }

    /// <summary>A folder withheld from AI is still the person's, so a flag they set on its mail is recorded like any other.</summary>
    [Fact]
    public async Task SubmitFlagChangesAsync_AMessageInAFolderWithheldFromAI_IsRecordedForThePerson()
    {
        // Arrange
        this.RecordEveryRequest();

        // Act
        var result = await ClientMailMutationsEndpoint.SubmitFlagChangesAsync(
            new ClientMailFlagChangesRequest(
                "call-1",
                [new ClientMailFlagChangeRequest(Message, new ClientMailFlagStateRequest(Seen: true, null), null)]),
            this.FlagRecorder(TargetInInbox(), participation: InboxWithheldFromAI()),
            TestContext.Current.CancellationToken);

        // Assert
        var change = Assert.Single(Assert.IsType<Ok<ClientMailFlagChangesResponse>>(result.Result).Value!.Results);

        Assert.Equal(ClientMailChangeOutcomes.Recorded, change.Outcome);
        Assert.NotEmpty(change.Changes);
    }

    /// <summary>
    /// On a held account the change is made rather than recorded, so the route answers <c>applied</c> with no record: a
    /// client that read <c>recorded</c> with an empty list would wait on a record that was never opened.
    /// </summary>
    [Fact]
    public async Task SubmitFlagChangesAsync_AHeldAccount_AnswersAppliedWithNoRecord()
    {
        // Act
        var result = await ClientMailMutationsEndpoint.SubmitFlagChangesAsync(
            new ClientMailFlagChangesRequest(
                "call-1",
                [new ClientMailFlagChangeRequest(Message, new ClientMailFlagStateRequest(Seen: true, null), null)]),
            this.FlagRecorder(TargetInInbox(), held: true),
            TestContext.Current.CancellationToken);

        // Assert
        var change = Assert.Single(Assert.IsType<Ok<ClientMailFlagChangesResponse>>(result.Result).Value!.Results);

        Assert.Equal(ClientMailChangeOutcomes.Applied, change.Outcome);
        Assert.Empty(change.Changes);
        await this.records.DidNotReceiveWithAnyArgs().OpenAsync(default!, default!, default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>A move a held account applied is published as <c>applied</c>, naming where it went and no record to follow.</summary>
    [Fact]
    public void MoveResult_AMoveAHeldAccountApplied_IsPublishedAsAppliedWithNoChange()
    {
        // Arrange
        var archive = MailFolderAlias.Create("Archive");

        // Act
        var move = ClientMailMoveResultResponse.For(Message, ArchiveId, AuthoredMailRelocationResult.Applied(archive));

        // Assert
        Assert.Equal(ClientMailChangeOutcomes.Applied, move.Outcome);
        Assert.Equal(ArchiveId.ToString(), move.DestinationFolder);
        Assert.Null(move.Change);
    }

    /// <summary>
    /// A message the use case cannot find reaches the route as an exception and has to leave it as that one message's
    /// result, because a batch carries on past a message that has gone.
    /// </summary>
    [Fact]
    public async Task SubmitFlagChangesAsync_AMessageTheUseCaseCannotFind_ReportsThatMessageAlone()
    {
        // Act
        var result = await ClientMailMutationsEndpoint.SubmitFlagChangesAsync(
            new ClientMailFlagChangesRequest(
                "call-1",
                [new ClientMailFlagChangeRequest(Message, new ClientMailFlagStateRequest(Seen: true, null), null)]),
            this.FlagRecorder(),
            TestContext.Current.CancellationToken);

        // Assert
        var change = Assert.Single(Assert.IsType<Ok<ClientMailFlagChangesResponse>>(result.Result).Value!.Results);

        Assert.Equal(ClientMailChangeOutcomes.MessageNotFound, change.Outcome);
        Assert.Empty(change.Changes);
    }

    /// <summary>
    /// A change the boundary cannot express is that message's refusal rather than the batch's, and it carries the
    /// refusal's own sentence — which is written for somebody to read and names no mail content.
    /// </summary>
    [Fact]
    public async Task SubmitFlagChangesAsync_ATagChangeNamingNoDirection_ReportsThatMessageWithTheRefusalsOwnSentence()
    {
        // Act
        var result = await ClientMailMutationsEndpoint.SubmitFlagChangesAsync(
            new ClientMailFlagChangesRequest(
                "call-1",
                [
                    new ClientMailFlagChangeRequest(
                        Message,
                        null,
                        new ClientMailTagChangeRequest("sideways", ["$label1"])),
                ]),
            this.FlagRecorder(TargetInInbox()),
            TestContext.Current.CancellationToken);

        // Assert
        var change = Assert.Single(Assert.IsType<Ok<ClientMailFlagChangesResponse>>(result.Result).Value!.Results);

        Assert.Equal(ClientMailChangeOutcomes.ChangeNotUsable, change.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(change.Detail));
        Assert.Empty(change.Changes);
    }

    /// <summary>
    /// The move route's own translation, driven through the use case: what it publishes is the answer that use case
    /// returned rather than one the route decided, so a result carried through the whole call proves the wiring the
    /// response builder's own test cannot.
    /// </summary>
    [Fact]
    public async Task SubmitMovesAsync_AMessageTheUseCaseCannotFind_PublishesTheUseCasesOwnAnswer()
    {
        // Act
        var result = await ClientMailMutationsEndpoint.SubmitMovesAsync(
            new ClientMailMovesRequest("call-1", [new ClientMailMoveRequest(Message, ArchiveId.ToString())]),
            this.RelocationRecorder(),
            TestContext.Current.CancellationToken);

        // Assert
        var move = Assert.Single(Assert.IsType<Ok<ClientMailMovesResponse>>(result.Result).Value!.Results);

        Assert.Equal(Message, move.StoredEmailId);
        Assert.Equal(ClientMailChangeOutcomes.MessageNotFound, move.Outcome);
        Assert.Null(move.DestinationFolder);
        Assert.Null(move.Change);
    }

    /// <summary>A destination is named by the identity the folders route reports, so a name of any other shape is no folder at all.</summary>
    [Fact]
    public async Task SubmitMovesAsync_ADestinationNamedByAnythingButItsIdentity_IsAFolderNotFound()
    {
        // Act
        var result = await ClientMailMutationsEndpoint.SubmitMovesAsync(
            new ClientMailMovesRequest("call-1", [new ClientMailMoveRequest(Message, "Archive")]),
            this.RelocationRecorder(),
            TestContext.Current.CancellationToken);

        // Assert
        var move = Assert.Single(Assert.IsType<Ok<ClientMailMovesResponse>>(result.Result).Value!.Results);

        Assert.Equal(ClientMailChangeOutcomes.DestinationNotFound, move.Outcome);
        Assert.Null(move.Change);
    }

    /// <summary>A folder withheld from AI is still the person's, so moving its mail out is recorded like any other move.</summary>
    [Fact]
    public async Task SubmitMovesAsync_AMessageInAFolderWithheldFromAI_IsRecordedForThePerson()
    {
        // Arrange
        this.RecordEveryRequest();
        var participation = StubMailFolderParticipation
            .Mapping(new MailFolderIdentity(ServedAccount, Archive))
            .Hiding(new MailFolderIdentity(ServedAccount, Inbox));

        // Act
        var result = await ClientMailMutationsEndpoint.SubmitMovesAsync(
            new ClientMailMovesRequest("call-1", [new ClientMailMoveRequest(Message, ArchiveId.ToString())]),
            this.RelocationRecorder(TargetInInbox(), participation),
            TestContext.Current.CancellationToken);

        // Assert
        var move = Assert.Single(Assert.IsType<Ok<ClientMailMovesResponse>>(result.Result).Value!.Results);

        Assert.Equal(ClientMailChangeOutcomes.Recorded, move.Outcome);
        Assert.Equal(MailboxMutation.Relocate.Name, move.Change?.Mutation);
    }

    /// <summary>A folder withheld from AI is still one the person reads, so filing their mail into it is recorded like any other move.</summary>
    [Fact]
    public async Task SubmitMovesAsync_ADestinationWithheldFromAI_IsRecordedForThePerson()
    {
        // Arrange
        this.RecordEveryRequest();
        var participation = StubMailFolderParticipation
            .Mapping(new MailFolderIdentity(ServedAccount, Inbox))
            .Hiding(new MailFolderIdentity(ServedAccount, Archive));

        // Act
        var result = await ClientMailMutationsEndpoint.SubmitMovesAsync(
            new ClientMailMovesRequest("call-1", [new ClientMailMoveRequest(Message, ArchiveId.ToString())]),
            this.RelocationRecorder(TargetInInbox(), participation),
            TestContext.Current.CancellationToken);

        // Assert
        var move = Assert.Single(Assert.IsType<Ok<ClientMailMovesResponse>>(result.Result).Value!.Results);

        Assert.Equal(ClientMailChangeOutcomes.Recorded, move.Outcome);
        Assert.Equal(ArchiveId.ToString(), move.DestinationFolder);
    }

    /// <summary>A batch naming no delete asks for nothing, exactly as one naming no move does.</summary>
    [Fact]
    public async Task SubmitDeletesAsync_ABatchNamingNoMessage_IsRefused()
    {
        // Act
        var result = await ClientMailMutationsEndpoint.SubmitDeletesAsync(
            new ClientMailDeletesRequest(RequestId: null, []),
            this.DeletionRecorder(),
            Preferences(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, RefusalOf(result).StatusCode);
    }

    /// <summary>
    /// The delete route's own translation, driven through the use case: the record it publishes names the delete
    /// mutation, and no part of the answer says what became of the local copy.
    /// </summary>
    [Fact]
    public async Task SubmitDeletesAsync_ADeleteTheUseCaseRecords_PutsTheRecordOnTheWire()
    {
        // Arrange
        this.RecordEveryRequest();

        // Act
        var result = await ClientMailMutationsEndpoint.SubmitDeletesAsync(
            new ClientMailDeletesRequest("call-1", [new ClientMailDeleteRequest(Message)]),
            this.DeletionRecorder(TargetInInbox()),
            Preferences(),
            TestContext.Current.CancellationToken);

        // Assert
        var deleted = Assert.Single(Assert.IsType<Ok<ClientMailDeletesResponse>>(result.Result).Value!.Results);

        Assert.Equal(Message, deleted.StoredEmailId);
        Assert.Equal(ClientMailChangeOutcomes.Recorded, deleted.Outcome);
        Assert.Equal(MailboxMutation.Delete.Name, deleted.Change?.Mutation);
    }

    /// <summary>A folder withheld from AI is still the person's, so deleting its mail is recorded like any other delete.</summary>
    [Fact]
    public async Task SubmitDeletesAsync_AMessageInAFolderWithheldFromAI_IsRecordedForThePerson()
    {
        // Arrange
        this.RecordEveryRequest();

        // Act
        var result = await ClientMailMutationsEndpoint.SubmitDeletesAsync(
            new ClientMailDeletesRequest("call-1", [new ClientMailDeleteRequest(Message)]),
            this.DeletionRecorder(TargetInInbox(), participation: InboxWithheldFromAI()),
            Preferences(),
            TestContext.Current.CancellationToken);

        // Assert
        var deleted = Assert.Single(Assert.IsType<Ok<ClientMailDeletesResponse>>(result.Result).Value!.Results);

        Assert.Equal(ClientMailChangeOutcomes.Recorded, deleted.Outcome);
        Assert.Equal(MailboxMutation.Delete.Name, deleted.Change?.Mutation);
    }

    /// <summary>A delete on a held account moves the message into the local trash at once, so the route answers <c>applied</c> with no record.</summary>
    [Fact]
    public async Task SubmitDeletesAsync_AHeldAccount_AnswersAppliedWithNoRecord()
    {
        // Act
        var result = await ClientMailMutationsEndpoint.SubmitDeletesAsync(
            new ClientMailDeletesRequest("call-1", [new ClientMailDeleteRequest(Message)]),
            this.DeletionRecorder(TargetInInbox(), held: true),
            Preferences(),
            TestContext.Current.CancellationToken);

        // Assert
        var deleted = Assert.Single(Assert.IsType<Ok<ClientMailDeletesResponse>>(result.Result).Value!.Results);

        Assert.Equal(ClientMailChangeOutcomes.Applied, deleted.Outcome);
        Assert.Null(deleted.Change);
        await this.records.DidNotReceiveWithAnyArgs().OpenAsync(default!, default!, default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The wait in front of a delete, which is the whole of what makes it withdrawable: the record is opened held until
    /// the person's own notification has ended and the grace after it has passed, so nothing reaches the mail server
    /// while the way back is still on the screen.
    /// </summary>
    [Fact]
    public async Task SubmitDeletesAsync_ADeleteAPersonAuthored_HoldsTheRecordForTheirNotificationAndTheGrace()
    {
        // Arrange
        this.RecordEveryRequest();

        // Act
        await ClientMailMutationsEndpoint.SubmitDeletesAsync(
            new ClientMailDeletesRequest("call-1", [new ClientMailDeleteRequest(Message)]),
            this.DeletionRecorder(TargetInInbox()),
            Preferences(ClientPreferences.Unset with { NotificationSeconds = 8 }),
            TestContext.Current.CancellationToken);

        // Assert
        await this.records.Received(1).OpenAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<MailboxMutationRequest>(),
            RecordedAt + TimeSpan.FromSeconds(8) + ClientMailMutationsEndpoint.DeleteWithdrawalGrace,
            Arg.Any<MailboxMutationLocalChange>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A grant carrying the delete and not the read is one the route serves, since no permission implies another: the
    /// setting is what that caller may not read, so the delete is recorded and held for the unset notification time
    /// rather than refused over a preference.
    /// </summary>
    [Fact]
    public async Task SubmitDeletesAsync_ACallerThatMayDeleteButNotRead_IsRecordedAndHeldForTheUnsetNotificationTime()
    {
        // Arrange
        this.RecordEveryRequest();

        // Act
        var result = await ClientMailMutationsEndpoint.SubmitDeletesAsync(
            new ClientMailDeletesRequest("call-1", [new ClientMailDeleteRequest(Message)]),
            this.DeletionRecorder(TargetInInbox()),
            Preferences(ClientPreferences.Unset with { NotificationSeconds = 8 }, MailFathomPermission.MailDelete),
            TestContext.Current.CancellationToken);

        // Assert
        var deleted = Assert.Single(Assert.IsType<Ok<ClientMailDeletesResponse>>(result.Result).Value!.Results);

        Assert.Equal(ClientMailChangeOutcomes.Recorded, deleted.Outcome);
        await this.records.Received(1).OpenAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<MailboxMutationRequest>(),
            RecordedAt
                + TimeSpan.FromSeconds(ClientPreferences.Unset.NotificationSeconds)
                + ClientMailMutationsEndpoint.DeleteWithdrawalGrace,
            Arg.Any<MailboxMutationLocalChange>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A message the use case cannot find is that message's own result, because a batch carries on past one that has gone.</summary>
    [Fact]
    public async Task SubmitDeletesAsync_AMessageTheUseCaseCannotFind_PublishesTheUseCasesOwnAnswer()
    {
        // Act
        var result = await ClientMailMutationsEndpoint.SubmitDeletesAsync(
            new ClientMailDeletesRequest("call-1", [new ClientMailDeleteRequest(Message)]),
            this.DeletionRecorder(),
            Preferences(),
            TestContext.Current.CancellationToken);

        // Assert
        var deleted = Assert.Single(Assert.IsType<Ok<ClientMailDeletesResponse>>(result.Result).Value!.Results);

        Assert.Equal(ClientMailChangeOutcomes.MessageNotFound, deleted.Outcome);
        Assert.Null(deleted.Change);
    }

    /// <summary>An identifier naming no message is answered about that entry rather than refusing the batch it travelled in.</summary>
    [Fact]
    public async Task SubmitDeletesAsync_AnEntryNamingTheEmptyIdentifier_ReportsThatEntryAlone()
    {
        // Act
        var result = await ClientMailMutationsEndpoint.SubmitDeletesAsync(
            new ClientMailDeletesRequest("call-1", [new ClientMailDeleteRequest(Guid.Empty)]),
            this.DeletionRecorder(TargetInInbox()),
            Preferences(),
            TestContext.Current.CancellationToken);

        // Assert
        var deleted = Assert.Single(Assert.IsType<Ok<ClientMailDeletesResponse>>(result.Result).Value!.Results);

        Assert.Equal(ClientMailChangeOutcomes.MessageNotFound, deleted.Outcome);
        await this.records.DidNotReceiveWithAnyArgs()
            .OpenAsync(default!, default!, default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>A withdrawal naming nothing is a request with nothing to take back.</summary>
    [Fact]
    public async Task WithdrawFlagChangesAsync_NoRecordNamed_IsRefused()
    {
        // Act
        var result = await ClientMailMutationsEndpoint.WithdrawFlagChangesAsync(
            new ClientMailChangeWithdrawalRequest([]),
            this.Withdrawer(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, RefusalOf(result).StatusCode);
    }

    /// <summary>The moving half is bounded exactly as the flag half is, and by the same published number.</summary>
    [Fact]
    public async Task WithdrawMovesAsync_MoreRecordsThanTheBatchBound_IsRefused()
    {
        // Arrange
        var asked = Enumerable
            .Range(0, ClientMailMutationsEndpoint.MaximumChangesPerRequest + 1)
            .Select(_ => Guid.CreateVersion7())
            .ToArray();

        // Act
        var result = await ClientMailMutationsEndpoint.WithdrawMovesAsync(
            new ClientMailChangeWithdrawalRequest(asked),
            this.Withdrawer(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, RefusalOf(result).StatusCode);
    }

    /// <summary>A folder withheld from AI is still the person's, so a flag change they made to its mail is theirs to withdraw.</summary>
    [Fact]
    public async Task WithdrawFlagChangesAsync_AChangeToMailInAFolderWithheldFromAI_IsWithdrawnForThePerson()
    {
        // Arrange
        var held = this.Holding(FlagRequest());

        // Act
        var result = await ClientMailMutationsEndpoint.WithdrawFlagChangesAsync(
            new ClientMailChangeWithdrawalRequest([held.Id.Value]),
            this.Withdrawer(InboxWithheldFromAI()),
            TestContext.Current.CancellationToken);

        // Assert
        var change = Assert.Single(Assert.IsType<Ok<ClientMailChangesResponse>>(result.Result).Value!.Changes);

        Assert.Equal(held.Id.Value, change.RecordId);
    }

    /// <summary>A folder withheld from AI is still the person's, so a move they made of its mail is theirs to withdraw.</summary>
    [Fact]
    public async Task WithdrawMovesAsync_AMoveOfMailInAFolderWithheldFromAI_IsWithdrawnForThePerson()
    {
        // Arrange
        var held = this.Holding(MoveRequest());

        // Act
        var result = await ClientMailMutationsEndpoint.WithdrawMovesAsync(
            new ClientMailChangeWithdrawalRequest([held.Id.Value]),
            this.Withdrawer(InboxWithheldFromAI()),
            TestContext.Current.CancellationToken);

        // Assert
        var change = Assert.Single(Assert.IsType<Ok<ClientMailChangesResponse>>(result.Result).Value!.Changes);

        Assert.Equal(held.Id.Value, change.RecordId);
    }

    /// <summary>A folder withheld from AI is still the person's, so a delete they made of its mail is theirs to withdraw.</summary>
    [Fact]
    public async Task WithdrawDeletesAsync_ADeleteOfMailInAFolderWithheldFromAI_IsWithdrawnForThePerson()
    {
        // Arrange
        var held = this.Holding(DeleteRequest());

        // Act
        var result = await ClientMailMutationsEndpoint.WithdrawDeletesAsync(
            new ClientMailChangeWithdrawalRequest([held.Id.Value]),
            this.Withdrawer(InboxWithheldFromAI()),
            TestContext.Current.CancellationToken);

        // Assert
        var change = Assert.Single(Assert.IsType<Ok<ClientMailChangesResponse>>(result.Result).Value!.Changes);

        Assert.Equal(held.Id.Value, change.RecordId);
    }

    /// <summary>A folder withheld from AI is still the person's, so the wait in front of a delete of its mail is theirs to end.</summary>
    [Fact]
    public async Task ReleaseDeletesAsync_ADeleteOfMailInAFolderWithheldFromAI_IsReleasedForThePerson()
    {
        // Arrange
        var held = this.Holding(DeleteRequest());

        // Act
        var result = await ClientMailMutationsEndpoint.ReleaseDeletesAsync(
            new ClientMailChangeWithdrawalRequest([held.Id.Value]),
            this.Releaser(InboxWithheldFromAI()),
            TestContext.Current.CancellationToken);

        // Assert
        var change = Assert.Single(Assert.IsType<Ok<ClientMailChangesResponse>>(result.Result).Value!.Changes);

        Assert.Equal(held.Id.Value, change.RecordId);
    }

    /// <summary>Every answer the moving use case can give reaches the wire under a published name, so a client never meets an outcome it has no branch for.</summary>
    [Fact]
    public void MoveResult_EveryDeclaredOutcome_PublishesAName()
    {
        // Act
        var published = Enum
            .GetValues<MailRelocationOutcome>()
            .Select(outcome => ClientMailMoveResultResponse.NotRecorded(Message, outcome).Outcome)
            .ToArray();

        // Assert
        Assert.All(published, outcome => Assert.False(string.IsNullOrWhiteSpace(outcome)));
        Assert.Equal(published.Length, published.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>A recorded move carries the folder it was filed into and the record it is carried by, which is what a client polls on.</summary>
    [Fact]
    public void MoveResult_ARecordedMove_CarriesTheFolderAndTheRecord()
    {
        // Arrange
        var recordId = MailboxMutationRecordId.Create(Guid.CreateVersion7());
        var archive = MailFolderAlias.Create("Archive");
        var recorded = AuthoredMailRelocationResult.Recorded(archive, recordId, MailboxMutationLifecycle.Pending);

        // Act
        var response = ClientMailMoveResultResponse.For(Message, ArchiveId, recorded);

        // Assert
        Assert.Equal(ClientMailChangeOutcomes.Recorded, response.Outcome);
        Assert.Equal(ArchiveId.ToString(), response.DestinationFolder);
        Assert.Equal(MailboxMutation.Relocate.Name, response.Change?.Mutation);
        Assert.Equal(recordId.Value, response.Change?.RecordId);
        Assert.Equal(MailboxMutationLifecycle.Pending.Name, response.Change?.State);
    }

    /// <summary>The person's move into the junk folder also wrote their verdict's keywords, and those records reach the client beside the move's own.</summary>
    [Fact]
    public void MoveResult_APersonsMoveIntoJunk_CarriesTheSpamMarksRecords()
    {
        // Arrange
        var keywords = RecordOf(MailboxMutationRequest.AddKeywords(
            StoredEmailId.Create(Message),
            TargetInInbox().Occurrence,
            Requester,
            AuthoredMailKeywords.Create(["$Junk"])));
        var recorded = AuthoredMailRelocationResult.Recorded(
            MailFolderAlias.Create("Junk"),
            MailboxMutationRecordId.Create(Guid.CreateVersion7()),
            MailboxMutationLifecycle.Pending) with
        {
            SpamMarkRecords = [keywords],
        };

        // Act
        var response = ClientMailMoveResultResponse.For(Message, ArchiveId, recorded);

        // Assert
        var change = Assert.Single(response.SpamMarkChanges);
        Assert.Equal((MailboxMutation.AddKeywords.Name, keywords.Id.Value), (change.Mutation, change.RecordId));
    }

    /// <summary>A move that was not written down names no folder and no record, so nothing on the response invites a client to poll for one.</summary>
    [Fact]
    public void MoveResult_AMoveThatWasNotRecorded_NamesNeitherAFolderNorARecord()
    {
        // Act
        var response = ClientMailMoveResultResponse.For(
            Message,
            ArchiveId,
            AuthoredMailRelocationResult.NotRecorded(MailRelocationOutcome.MessageNotFound));

        // Assert
        Assert.Equal(ClientMailChangeOutcomes.MessageNotFound, response.Outcome);
        Assert.Null(response.DestinationFolder);
        Assert.Null(response.Change);
    }

    /// <summary>A batch of spam marks naming no message asks for nothing, exactly as every other batch here does.</summary>
    [Fact]
    public async Task SubmitSpamMarksAsync_ABatchNamingNoMessage_IsRefused()
    {
        // Act
        var result = await ClientMailSpamMarksEndpoint.SubmitSpamMarksAsync(
            new ClientMailSpamMarksRequest(RequestId: null, "Spam", SpamSubcategory: null, []),
            this.SpamMarkRecorder(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, RefusalOf(result).StatusCode);
    }

    /// <summary>The bound holds a batch of spam marks to the durable writes one request may cause, as it holds every other batch.</summary>
    [Fact]
    public async Task SubmitSpamMarksAsync_MoreMessagesThanTheBatchBound_IsRefused()
    {
        // Arrange
        Guid[] messages = [.. Enumerable.Range(0, ClientMailMutationsEndpoint.MaximumChangesPerRequest + 1).Select(_ => Guid.CreateVersion7())];

        // Act
        var result = await ClientMailSpamMarksEndpoint.SubmitSpamMarksAsync(
            new ClientMailSpamMarksRequest(RequestId: null, "NotSpam", SpamSubcategory: null, messages),
            this.SpamMarkRecorder(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, RefusalOf(result).StatusCode);
    }

    /// <summary>
    /// A mark is the published member names and nothing else: a subcategory beside <i>not spam</i>, a name spelled in
    /// another case, a number standing for a member, and a missing verdict are each refused rather than guessed at.
    /// </summary>
    [Theory]
    [InlineData("NotSpam", "Phishing")]
    [InlineData("Spam", "phishing")]
    [InlineData("Spam", "0")]
    [InlineData("spam", null)]
    [InlineData("Undetermined", null)]
    [InlineData(null, null)]
    public async Task SubmitSpamMarksAsync_AMarkThisSurfaceDoesNotPublish_IsRefused(string? verdict, string? subcategory)
    {
        // Act
        var result = await ClientMailSpamMarksEndpoint.SubmitSpamMarksAsync(
            new ClientMailSpamMarksRequest(RequestId: null, verdict, subcategory, [Message]),
            this.SpamMarkRecorder(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, RefusalOf(result).StatusCode);
    }

    /// <summary>A message the use case cannot find is answered on its own, so the rest of the batch is still marked.</summary>
    [Fact]
    public async Task SubmitSpamMarksAsync_AMessageTheUseCaseCannotFind_ReportsThatMessageAlone()
    {
        // Act
        var result = await ClientMailSpamMarksEndpoint.SubmitSpamMarksAsync(
            new ClientMailSpamMarksRequest("call-1", "Spam", "Phishing", [Message, Guid.Empty]),
            this.SpamMarkRecorder(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [(Message, ClientMailChangeOutcomes.MessageNotFound), (Guid.Empty, ClientMailChangeOutcomes.MessageNotFound)],
            Assert.IsType<Ok<ClientMailSpamMarksResponse>>(result.Result).Value!.Results.Select(static mark => (mark.StoredEmailId, mark.Outcome)));
    }

    /// <summary>
    /// A caller holding only the flag grant, naming mail of an account whose switch files junk, is refused the whole
    /// request before any message of it is marked — never after part of the batch committed.
    /// </summary>
    [Fact]
    public async Task SubmitSpamMarksAsync_AFlagOnlyCallerNamingMailOfAnAccountThatFilesJunk_IsRefusedBeforeAnyMessageIsMarked()
    {
        // Arrange
        var recorder = this.SpamMarkRecorder(
            TargetInInbox(),
            SpamActionSettings.Create(filesJunk: true, marksJunkRead: false));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            ClientMailSpamMarksEndpoint.SubmitSpamMarksAsync(
                new ClientMailSpamMarksRequest("call-1", "Spam", SpamSubcategory: null, [Message, Guid.CreateVersion7()]),
                recorder,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailMove, refusal.RequiredPermission);
        await this.spamClassifications.DidNotReceive().SaveAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<SpamClassification>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>What a mark wrote reaches the wire as the move and the records a client follows, in the vocabulary every change here uses.</summary>
    [Fact]
    public void SpamMarkResultResponse_AMarkThatMovedTheMessage_CarriesTheMoveAndItsRecords()
    {
        // Arrange
        var record = RecordOf(MoveRequest());

        // Act
        var response = ClientMailSpamMarkResultResponse.For(
            new SpamMarkResult(StoredEmailId.Create(Message), SpamMarkOutcome.Recorded, [record], IsMoved: true));

        // Assert
        Assert.Equal((ClientMailChangeOutcomes.Recorded, true), (response.Outcome, response.Moved));
        Assert.Equal(record.Id.Value, Assert.Single(response.Changes).RecordId);
    }

    private static ProblemHttpResult RefusalOf<TValue>(Results<Ok<TValue>, ProblemHttpResult> result) =>
        Assert.IsType<ProblemHttpResult>(result.Result);

    /// <summary>Builds the scope resolution every one of these use cases reaches its caller's mail through.</summary>
    /// <param name="participation">Which of the caller's folders are reachable, defaulting to none, because most tests here are refused before a folder is read.</param>
    private static MailboxScopeResolver ScopeResolver(StubMailFolderParticipation? participation = null)
    {
        var catalog = Substitute.For<ICallerMailAccountCatalog>();
        catalog.ReadAssignedAccountsAsync(Arg.Any<CancellationToken>()).Returns([SyntheticServedAccount.Of(ServedAccount)]);

        var reachable = participation ?? StubMailFolderParticipation.Nothing;

        return new MailboxScopeResolver(
            catalog,
            reachable,
            StubDeploymentMailFolders.Of(reachable),
            StubMailFolderMappings.ResolvingNothing);
    }

    /// <summary>Answers every open with a freshly recorded row, which is what the routes' recorded answers are read from.</summary>
    /// <remarks>
    /// The identity is not deduplicated, because nothing asserted here asks the same thing twice — recognizing a repeat
    /// is the record store's own contract and is covered against a real database.
    /// </remarks>
    private void RecordEveryRequest() => this.records
        .OpenAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<MailboxMutationRequest>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<MailboxMutationLocalChange>(),
            Arg.Any<CancellationToken>())
        .Returns(call => Task.FromResult(RecordOf(Assert.IsType<MailboxMutationRequest>(call[1]))));

    /// <summary>Holds one record the caller authored, answering its read, its withdrawal, and its release with the record as it stands.</summary>
    private MailboxMutationRecord Holding(MailboxMutationRequest request)
    {
        var record = RecordOf(request);

        this.records
            .ReadAsync(
                Arg.Any<IReadOnlyList<MailAccountId>>(),
                Arg.Any<IReadOnlyList<MailboxMutationRecordId>>(),
                Arg.Any<CancellationToken>())
            .Returns([record]);
        this.records
            .WithdrawAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<IReadOnlyList<MailAccountId>>(),
                Arg.Any<IReadOnlyList<MailboxMutationRecordId>>(),
                Arg.Any<CancellationToken>())
            .Returns([record]);
        this.records
            .ReleaseAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<IReadOnlyList<MailAccountId>>(),
                Arg.Any<IReadOnlyList<MailboxMutationRecordId>>(),
                Arg.Any<CancellationToken>())
            .Returns([record]);

        return record;
    }

    private static MailboxMutationRecord RecordOf(MailboxMutationRequest request) => new()
    {
        Id = MailboxMutationRecordId.Create(Guid.CreateVersion7()),
        Request = request,
        Stage = MailboxMutationStage.Recorded,
        IsAudited = false,
        RequiresSourceRemoval = false,
        Placement = RemoteEmailPlacement.NotReported(),
        AttemptCount = 0,
        RecordedAt = RecordedAt,
        StageChangedAt = RecordedAt,
        LastFailure = null,
        PlacementObservedAt = null,
        SourceRemovalObservedAt = null,
    };

    private static MailboxMutationRequest FlagRequest() =>
        MailboxMutationRequest.SetSeen(StoredEmailId.Create(Message), TargetInInbox().Occurrence, Requester, isSeen: true);

    private static MailboxMutationRequest MoveRequest() =>
        MailboxMutationRequest.Relocate(
            StoredEmailId.Create(Message),
            TargetInInbox().Occurrence,
            Requester,
            RemoteFolderPath.Create(Archive.Value));

    private static MailboxMutationRequest DeleteRequest() =>
        MailboxMutationRequest.Delete(
            StoredEmailId.Create(Message),
            TargetInInbox().Occurrence,
            Requester,
            AuthoredDeleteEmailDisposition.RetainTombstone,
            AuthoredDeleteServerDisposition.Expunge);

    /// <summary>The caller's inbox withheld from AI, which leaves it mirrored and the person's to read and act on.</summary>
    private static StubMailFolderParticipation InboxWithheldFromAI() =>
        StubMailFolderParticipation.Nothing.Hiding(new MailFolderIdentity(ServedAccount, Inbox));

    /// <summary>The caller's own message, in a folder the scope above reaches.</summary>
    private static AuthoredMailboxTarget TargetInInbox()
    {
        var folder = MailFolderResolution.FirstBindingOf(Inbox, RemoteFolderPath.Create(Inbox.Value));

        return new AuthoredMailboxTarget(
            EmailOccurrenceId.Create(ServedAccount, folder.Id, ImapUidValidity.Create(42), ImapUid.Create(7)),
            folder,
            MailFolderId.Create(Guid.Parse("0199c1a2-0000-7000-8000-000000000005")));
    }

    private static OptimisticConcurrencyRetryPolicy CommitPolicy()
    {
        var sessions = Substitute.For<IPersistenceSessionFactory>();
        sessions.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var session = Substitute.For<IPersistenceSession>();
            session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);

            return session;
        });

        return new OptimisticConcurrencyRetryPolicy(
            sessions,
            new PersistenceConcurrencyOptions(),
            new FakeTimeProvider(RecordedAt));
    }

    private MailboxChangeProgressReader ProgressReader(StubMailFolderParticipation? participation = null) => new(
        AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead),
        ScopeResolver(participation),
        this.records);

    private MailboxChangeWithdrawer Withdrawer(StubMailFolderParticipation? participation = null) => new(
        AccessAuthorizations.ForCallerGranted(
            MailFathomPermission.MailFlagsWrite,
            MailFathomPermission.MailMove,
            MailFathomPermission.MailDelete),
        ScopeResolver(participation),
        this.records,
        CommitPolicy());

    private MailboxChangeReleaser Releaser(StubMailFolderParticipation? participation = null) => new(
        AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailDelete),
        ScopeResolver(participation),
        this.records,
        CommitPolicy(),
        new MailAccountRunSignal());

    /// <summary>Builds the submission every use case here writes through.</summary>
    /// <param name="held">Whether the account's mailbox is one MailFathom holds, storing the message the inbox target names; otherwise its source is the truth.</param>
    private MailboxChangeSubmission Submission(bool held)
    {
        var localFolders = Substitute.For<ILocalMailFolderStore>();
        var custody = Substitute.For<IMailAccountCustodyStore>();
        var states = Substitute.For<ILocalEmailStateStore>();
        var auditSettings = Substitute.For<IMailboxMutationAuditSettingsReader>();

        if (held)
        {
            localFolders
                .ReadAsync(Arg.Any<IPersistenceSession>(), Arg.Any<MailAccountId>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<LocalMailFolderHolding?>(new LocalMailFolderHolding([], [])));
            custody
                .ReadAsync(Arg.Any<IPersistenceSession>(), Arg.Any<MailAccountId>(), Arg.Any<CancellationToken>())
                .Returns(new MailAccountCustodyState(MailAccountCustody.HoldMailbox, MailAccountCustodyPhase.Held));
            states
                .ReadAsync(
                    Arg.Any<IPersistenceSession>(),
                    Arg.Any<MailAccountId>(),
                    Arg.Any<StoredEmailId>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<LocalEmailState?>(new LocalEmailState(
                    TargetInInbox().Folder,
                    HoldsSourceOccurrence: true,
                    Folder: null,
                    IsSeen: false,
                    IsFlagged: false,
                    RemoteEmailKeywords.Create([]))));
            auditSettings.GetAuditSettings(Arg.Any<MailAccountId>()).Returns(MailboxMutationAuditSettings.Disabled);
        }

        return new MailboxCustodianComposition(new FakeTimeProvider(RecordedAt))
        {
            Custody = custody,
            LocalFolders = localFolders,
            Records = this.records,
            States = states,
            AuditSettings = auditSettings,
        }.Submission;
    }

    /// <summary>Builds the flag-change use case the routes are given.</summary>
    /// <param name="target">The message the caller names, defaulting to none, which is the absence the recorder reports as a message that has gone.</param>
    /// <param name="held">Whether the account's mailbox is one MailFathom holds.</param>
    /// <param name="participation">What the caller's folders take part in where a target is named, defaulting to the inbox taking part in everything.</param>
    private MailFlagChangeRecorder FlagRecorder(
        AuthoredMailboxTarget? target = null,
        bool held = false,
        StubMailFolderParticipation? participation = null)
    {
        var targets = Substitute.For<IAuthoredMailboxTargetReader>();
        targets.FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(target));

        return new MailFlagChangeRecorder(
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailFlagsWrite),
            ScopeResolver(
                target is null
                    ? null
                    : participation ?? StubMailFolderParticipation.Mapping(new MailFolderIdentity(ServedAccount, Inbox))),
            targets,
            this.Submission(held),
            CommitPolicy(),
            new MailAccountRunSignal());
    }

    /// <summary>Builds the moving use case the route is given, over an account that mirrors its archive and has bound it.</summary>
    /// <param name="target">The message the caller names, defaulting to none, which is the absence the recorder reports as a message that has gone.</param>
    /// <param name="participation">What the caller's folders take part in where a target is named, defaulting to the inbox and the archive taking part in everything.</param>
    private MailRelocationRecorder RelocationRecorder(
        AuthoredMailboxTarget? target = null,
        StubMailFolderParticipation? participation = null)
    {
        var targets = Substitute.For<IAuthoredMailboxTargetReader>();
        targets.FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(target));

        var mappings = StubMailFolderMappings.Nothing.With(
            ServedAccount,
            MailFolderMapping.ToRemotePath(Archive, RemoteFolderPath.Create(Archive.Value)));

        var bindings = Substitute.For<IMailFolderResolutionStore>();
        bindings
            .GetCurrentResolutionAsync(ServedAccount, Archive, Arg.Any<CancellationToken>())
            .Returns(MailFolderResolution.FirstBindingOf(Archive, RemoteFolderPath.Create(Archive.Value)));

        var folders = Substitute.For<ILocalMailFolderStore>();
        folders
            .ReadFoldersAsync(ServedAccount, Arg.Any<CancellationToken>())
            .Returns([new LocalMailFolder(ArchiveId, ParentId: null, LocalMailFolderName.Create(Archive.Value), Role: null, Archive)]);

        return new MailRelocationRecorder(
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailMove),
            ScopeResolver(
                target is null
                    ? null
                    : participation ?? StubMailFolderParticipation.Mapping(
                        new MailFolderIdentity(ServedAccount, Inbox),
                        new MailFolderIdentity(ServedAccount, Archive))),
            targets,
            mappings.Resolver,
            bindings,
            folders,
            DestinationResolver(mappings, bindings),
            Substitute.For<IAuthoredDeleteEmailDispositionReader>(),
            this.Submission(held: false),
            CommitPolicy(),
            new MailAccountRunSignal(),
            StubJunkMailFolderCatalog.None,
            this.SpamMarks());
    }

    /// <summary>Builds the use case the spam route is given, under the grant that route carries.</summary>
    /// <param name="target">The message the caller names, defaulting to none, which is the absence the recorder reports as a message that has gone.</param>
    /// <param name="switches">The account's spam switches, defaulting to ones that file nothing.</param>
    private SpamMarkRecorder SpamMarkRecorder(AuthoredMailboxTarget? target = null, SpamActionSettings? switches = null)
    {
        var targets = Substitute.For<IAuthoredMailboxTargetReader>();
        targets.FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(target));

        var settings = Substitute.For<ISpamActionSettingsReader>();
        settings.ActionsFor(Arg.Any<MailAccountId>()).Returns(switches ?? SpamActionSettings.None);

        return new SpamMarkRecorder(
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailFlagsWrite),
            ScopeResolver(
                target is null ? null : StubMailFolderParticipation.Mapping(new MailFolderIdentity(ServedAccount, Inbox))),
            targets,
            settings,
            StubJunkMailFolderCatalog.None,
            Substitute.For<ILocalMailFolderStore>(),
            DestinationResolver(StubMailFolderMappings.Nothing, Substitute.For<IMailFolderResolutionStore>()),
            Substitute.For<IAuthoredDeleteEmailDispositionReader>(),
            this.SpamMarks(),
            this.Submission(held: false),
            CommitPolicy(),
            new MailAccountRunSignal(),
            StubMailFolderMappings.Nothing.Resolver,
            Substitute.For<IMailFolderResolutionStore>());
    }

    /// <summary>Builds what writes the person's verdict and its keywords, over an account that permits keywords.</summary>
    private SpamMarkWriter SpamMarks()
    {
        var permissions = Substitute.For<IMailRuleActionPermissionReader>();
        permissions.GetRuleActionPermissions(Arg.Any<MailAccountId>()).Returns(MailRuleActionPermissions.Default);

        return new SpamMarkWriter(
            Substitute.For<ISpamActionOccurrenceReader>(),
            permissions,
            this.spamClassifications,
            Substitute.For<ISpamVerdictActStore>(),
            Substitute.For<IEmailChunkStore>(),
            Substitute.For<IStoredEmailAttachmentTextStore>(),
            Substitute.For<IDerivedWorkGateTelemetry>(),
            this.Submission(held: false),
            new FakeTimeProvider(RecordedAt));
    }

    /// <summary>Builds the deleting use case the route is given, under the grant that route carries.</summary>
    /// <param name="target">The message the caller names, defaulting to none, which is the absence the recorder reports as a message that has gone.</param>
    /// <param name="held">Whether the account's mailbox is one MailFathom holds.</param>
    /// <param name="participation">What the caller's folders take part in where a target is named, defaulting to the inbox taking part in everything.</param>
    private MailDeletionRecorder DeletionRecorder(
        AuthoredMailboxTarget? target = null,
        bool held = false,
        StubMailFolderParticipation? participation = null)
    {
        var targets = Substitute.For<IAuthoredMailboxTargetReader>();
        targets.FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(target));

        var dispositions = Substitute.For<IAuthoredDeleteEmailDispositionReader>();
        dispositions
            .GetAuthoredDeleteDisposition(Arg.Any<MailAccountId>())
            .Returns(AuthoredDeleteEmailDisposition.RetainTombstone);

        return new MailDeletionRecorder(
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailDelete),
            ScopeResolver(
                target is null
                    ? null
                    : participation ?? StubMailFolderParticipation.Mapping(new MailFolderIdentity(ServedAccount, Inbox))),
            targets,
            dispositions,
            this.Submission(held),
            CommitPolicy(),
            new MailAccountRunSignal(),
            new FakeTimeProvider(RecordedAt));
    }

    /// <summary>Builds the preferences the delete route reads the withdrawal window from.</summary>
    /// <param name="stored">What the person set, defaulting to none, which is the unset answer every deployment starts from.</param>
    /// <param name="granted">What the caller's grant carries, defaulting to the read the preferences are published under.</param>
    private static OwnClientPreferences Preferences(
        ClientPreferences? stored = null,
        MailFathomPermission? granted = null)
    {
        var preferences = Substitute.For<IClientPreferencesStore>();
        preferences.ReadAsync(Arg.Any<UserId>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(stored));

        return new OwnClientPreferences(
            AccessAuthorizations.ForCallerGranted(granted ?? MailFathomPermission.ProfileRead),
            preferences);
    }

    /// <summary>Builds the destination resolver over the account's mappings and the bindings its synchronization recorded.</summary>
    private static MailboxDestinationResolver DestinationResolver(
        StubMailFolderMappings mappings,
        IMailFolderResolutionStore bindings) =>
        new MailboxCustodianComposition(new FakeTimeProvider(RecordedAt))
        {
            Mappings = mappings,
            FolderResolutions = bindings,
        }.Destinations;
}
