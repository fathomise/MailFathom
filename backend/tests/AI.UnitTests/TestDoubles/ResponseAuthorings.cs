// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Contacts;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Authoring;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using NSubstitute;

namespace MailFathom.AI.UnitTests.TestDoubles;

/// <summary>Builds the authoring an Agent's answer goes through, over one stored message a test states.</summary>
/// <remarks>
/// The authoring is a use case rather than a port, so a test reaching it composes the real one over its own
/// collaborators: the message's summary, its stored copy, the rendering read from it, and the address the account sends
/// from. Every one of them answers for that message alone.
/// </remarks>
internal static class ResponseAuthorings
{
    private const string FolderAlias = "INBOX";

    private const string AnsweredText = "Could you confirm the four bays by Friday?";

    /// <summary>Builds the authoring answering one message, received into an account's inbox from one author.</summary>
    /// <param name="answered">The message's identity.</param>
    /// <param name="account">The account the message was received into, and which an answer goes out from.</param>
    /// <param name="author">The address the message came from.</param>
    /// <param name="authorization">The grant the authoring acts under.</param>
    /// <returns>The authoring.</returns>
    public static StoredEmailResponseAuthoring Answering(
        StoredEmailId answered,
        MailAccountId account,
        string author,
        AccessAuthorization authorization)
    {
        var summaries = Substitute.For<IStoredEmailSummaryReader>();
        summaries.FindAsync(answered, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmailSummary?>(SummaryOf(answered, account)));

        var storedMime = Encoding.UTF8.GetBytes($"From: {author}\r\n\r\n{AnsweredText}");
        var contentStore = Substitute.For<IEmailContentStore>();
        contentStore
            .FindStoredContentAsync(answered, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StoredEmailContent?>(new StoredEmailContent(storedMime, storedMime.Length, SHA256.HashData(storedMime))));

        var renderer = Substitute.For<IEmailContentRenderer>();
        renderer
            .RenderAsync(Arg.Any<StoredEmailContent>(), Arg.Any<EmailContentRenderingBounds>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailContentRenderingResult.Rendered(RenderingFrom(author))));

        var senderIdentities = Substitute.For<IOutgoingSenderIdentityReader>();
        senderIdentities
            .FindSenderIdentity(account)
            .Returns(OutgoingSenderIdentity.Create(account, AddressOf("reader@example.test")));

        var catalog = Substitute.For<ICallerMailAccountCatalog>();
        catalog.ReadAssignedAccountsAsync(Arg.Any<CancellationToken>()).Returns([SyntheticServedAccount.Of(account)]);

        var participation = StubMailFolderParticipation.Mapping(new MailFolderIdentity(account, MailFolderAlias.Create(FolderAlias)));

        return new StoredEmailResponseAuthoring(
            summaries,
            contentStore,
            renderer,
            Substitute.For<IEmailAttachmentContentReader>(),
            Substitute.For<IEmailContentRepairRequestStore>(),
            new MailboxScopeResolver(
                catalog,
                participation,
                StubDeploymentMailFolders.Of(participation),
                StubMailFolderMappings.ResolvingNothing),
            senderIdentities,
            new NamedRecipientResolver(Substitute.For<IContactDirectory>(), ContactBookOwnerships.For(authorization)),
            new OutgoingEmailBounds
            {
                MaxRecipientCount = 8,
                MaxBodyCharacters = 4096,
                MaxAttachmentCount = 3,
                MaxAttachmentBytes = 128,
                MaxMessageBytes = 4096,
            },
            authorization);
    }

    private static EmailSummary SummaryOf(StoredEmailId answered, MailAccountId account) => new()
    {
        StoredEmailId = answered,
        Account = account,
        Folder = new StoredEmailFolder(
            MailFolderId.Create(Guid.Parse("0199c0de-0000-7000-8000-0000000000f1")),
            FolderAlias,
            MailFolderAlias.Create(FolderAlias)),
        ThreadId = null,
        InternetMessageId = null,
        Subject = "Four bays",
        SentAt = null,
        ReceivedAt = null,
        SizeOctets = 2048,
        SenderDisplayName = null,
        SenderAddress = null,
        ToAddresses = [],
        Attachments = new StoredEmailAttachmentSummary(
            0,
            TotalSizeOctets: 0,
            0,
            IsEncrypted: false,
            CarriesUnverifiedSignature: false,
            ContainsUnexpandedTnefPart: false),
        ContentAvailability = StoredEmailContentAvailability.Available,
        RemoteFlags = new RemoteEmailFlagSnapshot(
            null,
            false,
            IsAnswered: false,
            IsFlagged: false,
            IsDraft: false,
            IsDeleted: false,
            Keywords: RemoteEmailKeywords.None),
        SenderVerification = SenderVerification.NotEstablished,
        MachineAuthorship = MachineAuthorshipAssessment.NotAssessed,
        SenderAuthenticationEvidence = SenderAuthenticationEvidence.None,
    };

    private static EmailContentRendering RenderingFrom(string author) => new(
        new EmailContentHeaders(
            "Four bays",
            null,
            null,
            [new EmailParticipant(EmailAddressRole.From, AddressOf(author))],
            EmailThreadReferences.Create("bays@example.test", inReplyTo: null, references: null)),
        new EmailBodyRepresentation(AnsweredText, AnsweredText.Length, EmailBodyTruncation.None),
        null,
        new EmailBodyForms(PlainText: true, Html: false),
        false,
        EmailAttachmentSummary.Create(
            [],
            inlineResourceCount: 0,
            false,
            carriesUnverifiedSignature: false,
            containsUnexpandedTnefPart: false),
        []);

    private static EmailAddress AddressOf(string address) =>
        EmailAddress.TryCreate(displayName: null, address, out var parsed)
            ? parsed
            : throw new ArgumentException("The address is not one a message could carry.", nameof(address));
}
