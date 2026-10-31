// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Contacts.Photos;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Emails.Threads;
using MailFathom.Application.Emails.Trust;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Emails.Signatures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;
using MailFathom.Host.Api;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the message route asks the read for, and what it puts on the wire around a body it never sends.</summary>
/// <remarks>
/// The read itself is covered where it happens. What is asserted here is the transport: that a pane's read asks for a
/// description and for no representation and no capability at all, that the file list carries the positions its own
/// download route is asked with, that the sender verdict travels as the two states a screen draws it from, and that the
/// body block says what the sender wrote rather than what a request would return.
/// </remarks>
public sealed class ClientMailMessageEndpointTests
{
    private static readonly IReadOnlyDictionary<EmailAddress, PersonPhoto> NoPhotos = new Dictionary<EmailAddress, PersonPhoto>();

    private static readonly Guid Message = new("22222222-2222-2222-2222-222222222222");

    private static readonly Guid Conversation = new("33333333-3333-3333-3333-333333333333");

    /// <summary>The path a client appends to the address it was configured with, pinned because the client composes it from a constant of its own.</summary>
    [Fact]
    public void MailMessageRoute_IsThePathAClientComposes() =>
        Assert.Equal("/messages/{storedEmailId:guid}", ClientMailMessageEndpoint.MailMessageRoute);

    /// <summary>
    /// The pane asks for a description of the message and for nothing produced from its body, and it mints no capability:
    /// the words and the tree are the body route's, and a link nobody asked for is a bearer credential handed out.
    /// </summary>
    [Fact]
    public void RequestFor_AnyRead_AsksForNoRepresentationAndNoAttachmentLink()
    {
        // Act
        var request = ClientMailMessageEndpoint.RequestFor(Message);

        // Assert
        Assert.False(request.IncludeMailDocument);
        Assert.False(request.IncludeSanitizedHtml);
        Assert.False(request.IncludeAttachmentDownloadLinks);
        Assert.False(request.RetainRemoteImageReferences);
        Assert.Equal([StoredEmailId.Create(Message)], request.StoredEmailIds);
    }

    /// <summary>The pane reads for the person rather than for an agent, so a message in a folder withheld from AI is still served to them.</summary>
    [Fact]
    public void RequestFor_AnyRead_ReadsForThePersonSoAFolderWithheldFromAIIsServed()
    {
        // Act
        var request = ClientMailMessageEndpoint.RequestFor(Message);

        // Assert
        Assert.Equal(MailboxReaderKind.Person, request.Reader);
    }

    /// <summary>The header block a pane draws is what the message displayed, including what a list row deliberately narrows away.</summary>
    [Fact]
    public void For_AReadMessage_CarriesTheHeadersTheMessageDisplayed()
    {
        // Act
        var response = ClientMailMessageResponse.For(MessageWith(), SenderContactStanding.Unanswered, NoPhotos);

        // Assert
        Assert.Equal(Message, response.StoredEmailId);
        Assert.Equal("primary", response.Account);
        Assert.Equal("83ecb521-bf32-7f67-8287-e8b923809bb3", response.Folder);
        Assert.Equal(Conversation, response.ThreadId);
        Assert.Equal("Quarterly invoice", response.Headers.Subject);
        Assert.Equal("abc@example.test", response.Headers.MessageId);
        Assert.Collection(
            response.Headers.Participants,
            participant =>
            {
                Assert.Equal("From", participant.Role);
                Assert.Equal("billing@example.test", participant.Address);
                Assert.Equal("Billing", participant.DisplayName);
            },
            participant => Assert.Equal("To", participant.Role));
    }

    /// <summary>
    /// A file is described and never carried, and the position it is described at is the one the download route is asked
    /// with — a list that renumbered or reordered would hand a reader the wrong file rather than fail.
    /// </summary>
    [Fact]
    public void For_AMessageCarryingFiles_DescribesEachAtThePositionItsDownloadIsAskedWith()
    {
        // Act
        var response = ClientMailMessageResponse.For(MessageWith(), SenderContactStanding.Unanswered, NoPhotos);

        // Assert
        Assert.Collection(
            response.Attachments,
            first =>
            {
                Assert.Equal(0, first.Position);
                Assert.Equal("invoice.pdf", first.FileName);
                Assert.Equal("application/pdf", first.MediaType);
                Assert.Equal(2048, first.SizeOctets);
                Assert.False(first.WasFileNameNormalized);
            },
            second =>
            {
                Assert.Equal(1, second.Position);
                Assert.Equal("photo.jpg", second.FileName);
            });
    }

    /// <summary>The counts say what the message carries besides its files, which is what keeps a signed message from drawing a paperclip.</summary>
    [Fact]
    public void For_AMessageWhosePartsWereRead_CarriesTheCountsBesideTheDescriptions()
    {
        // Act
        var response = ClientMailMessageResponse.For(MessageWith(), SenderContactStanding.Unanswered, NoPhotos);

        // Assert
        Assert.NotNull(response.Carried);
        Assert.Equal(2, response.Carried.AttachmentCount);
        Assert.Equal(3, response.Carried.InlineResourceCount);
        Assert.False(response.Carried.Encrypted);
    }

    /// <summary>
    /// A message whose content the size limit kept out of storage has had its parts read by nothing, so the counts are
    /// absent rather than zero: zero would tell a reader the message carries no files, which nothing here established.
    /// </summary>
    [Fact]
    public void For_AMessageNothingHasParsed_CarriesNoCountsAtAll()
    {
        // Arrange
        var message = MessageWith() with
        {
            AttachmentSummary = null,
            Attachments = [],
            Body = EmailContentBody.NotStoredExceededSizeLimit,
        };

        // Act
        var response = ClientMailMessageResponse.For(message, SenderContactStanding.Unanswered, NoPhotos);

        // Assert
        Assert.Null(response.Carried);
        Assert.Empty(response.Attachments);
        Assert.Equal("NotStoredExceededSizeLimit", response.Body.Availability);
        Assert.False(response.Body.PlainText);
        Assert.False(response.Body.Html);
    }

    /// <summary>
    /// The body block says which forms the sender wrote rather than which the surface would return, because the words
    /// come back for every readable message and therefore say nothing about what arrived.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void For_AReadableBody_SaysWhichFormsTheSenderWrote(bool plainText, bool html)
    {
        // Arrange
        var message = MessageWith() with
        {
            Body = EmailContentBody.Readable(
                new EmailBodyRepresentation("Just words.", 11, EmailBodyTruncation.None),
                sanitizedHtml: null,
                document: null,
                selfContainedHtml: null,
                new EmailBodyForms(plainText, html)),
        };

        // Act
        var response = ClientMailMessageResponse.For(message, SenderContactStanding.Unanswered, NoPhotos);

        // Assert
        Assert.Equal("Readable", response.Body.Availability);
        Assert.Equal(plainText, response.Body.PlainText);
        Assert.Equal(html, response.Body.Html);
    }

    /// <summary>
    /// The verdict travels as the two states it was stored as. A screen draws a badge from both, because an authenticated
    /// author nobody has named carries the same trust value as one whose authentication failed.
    /// </summary>
    [Fact]
    public void For_AReadMessage_CarriesTheSenderVerdictAsTwoStates()
    {
        // Act
        var response = ClientMailMessageResponse.For(MessageWith(), SenderContactStanding.Unanswered, NoPhotos);

        // Assert
        Assert.Equal("Authenticated", response.Sender.AuthorAuthentication);
        Assert.Equal("Trusted", response.Sender.DeploymentTrust);
    }

    /// <summary>
    /// The domain that authenticated travels beside the verdict, so a reading pane can name who actually sent a message
    /// rather than repeating the <c>From</c> value it displays — which is the one thing an impersonation gets wrong.
    /// </summary>
    [Fact]
    public void For_AMessageWhoseAuthorAuthenticated_NamesTheDomainThatDidSo()
    {
        // Arrange
        var evidence = new SenderAuthenticationEvidence
        {
            AuthenticatedDomain = SenderDomain.TryCreate("mail.example.test", out var domain) ? domain : default,
            AuthenticatedBy = SenderAuthenticationMethod.DomainKeysIdentifiedMail,
            Dmarc = DmarcOutcome.Pass,
            Source = SenderAuthenticationSource.ReceivingServer,
        };

        // Act
        var response = ClientMailMessageResponse.For(MessageWith(evidence), SenderContactStanding.Unanswered, NoPhotos);

        // Assert
        Assert.Equal("mail.example.test", response.Sender.AuthenticatedDomain);
    }

    /// <summary>A message nothing authenticated names no domain, which is an ordinary outcome rather than missing data.</summary>
    [Fact]
    public void For_AMessageNothingAuthenticated_NamesNoDomain()
    {
        // Act
        var response = ClientMailMessageResponse.For(MessageWith(), SenderContactStanding.Unanswered, NoPhotos);

        // Assert
        Assert.Null(response.Sender.AuthenticatedDomain);
    }

    /// <summary>
    /// The verdict carries what a details popup lists under sender authentication: who checked the message, the DKIM and
    /// SPF domains that passed, and the DMARC outcome — each as stored, and none of them judged against the others.
    /// </summary>
    [Fact]
    public void For_AMessageTheReceivingServerChecked_PublishesWhoCheckedTheDomainsThatPassedAndDmarc()
    {
        // Arrange
        var evidence = new SenderAuthenticationEvidence
        {
            AuthenticatedDomain = SenderDomain.TryCreate("relay.example.test", out var relay) ? relay : default,
            DkimDomain = relay,
            SpfDomain = SenderDomain.TryCreate("example.test", out var author) ? author : default,
            AuthenticatedBy = SenderAuthenticationMethod.DomainKeysIdentifiedMail,
            Dmarc = DmarcOutcome.TemporaryError,
            Source = SenderAuthenticationSource.ReceivingServer,
        };

        // Act
        var sender = ClientMailMessageResponse.For(MessageWith(evidence), SenderContactStanding.Unanswered, NoPhotos).Sender;

        // Assert
        Assert.Equal("ReceivingServer", sender.AuthenticationSource);
        Assert.Equal("relay.example.test", sender.DkimDomain);
        Assert.Equal("example.test", sender.SpfDomain);
        Assert.Equal("TemporaryError", sender.Dmarc);
    }

    /// <summary>MailFathom's own DKIM check reports itself, with no SPF domain and no DMARC result, because it reads neither.</summary>
    [Fact]
    public void For_AMessageMailFathomCheckedItself_PublishesLocalVerificationWithNothingItCouldNotRead()
    {
        // Arrange
        var evidence = new SenderAuthenticationEvidence
        {
            DkimDomain = SenderDomain.TryCreate("example.test", out var domain) ? domain : default,
            AuthenticatedBy = SenderAuthenticationMethod.DomainKeysIdentifiedMail,
            Dmarc = DmarcOutcome.NotReported,
            Source = SenderAuthenticationSource.LocalVerification,
        };

        // Act
        var sender = ClientMailMessageResponse.For(MessageWith(evidence), SenderContactStanding.Unanswered, NoPhotos).Sender;

        // Assert
        Assert.Equal("LocalVerification", sender.AuthenticationSource);
        Assert.Null(sender.SpfDomain);
        Assert.Equal("NotReported", sender.Dmarc);
    }

    /// <summary>A trusted author's verdict says what recognized them, which is what the popup's trust step explains.</summary>
    /// <param name="grantedBy">What recognized the author.</param>
    /// <param name="published">The name the client reads it by.</param>
    [Theory]
    [InlineData(SenderTrustSource.OwnAccountDomain, "OwnAccountDomain")]
    [InlineData(SenderTrustSource.OrganizationContact, "OrganizationContact")]
    [InlineData(SenderTrustSource.PersonalContact, "PersonalContact")]
    [InlineData(SenderTrustSource.OrganizationMember, "OrganizationMember")]
    [InlineData(SenderTrustSource.OwnAccountAddress, "OwnAccountAddress")]
    public void For_ATrustedAuthor_PublishesWhatRecognizedThem(SenderTrustSource grantedBy, string published)
    {
        // Arrange
        var message = MessageWith() with
        {
            SenderVerification = MessageWith().SenderVerification with
            {
                DeploymentTrustGrantedBy = grantedBy,
            },
        };

        // Act
        var sender = ClientMailMessageResponse.For(message, SenderContactStanding.Unanswered, NoPhotos).Sender;

        // Assert
        Assert.Equal(published, sender.TrustSource);
    }

    /// <summary>
    /// A member an organization's signature recognized publishes that organization's name beside the source, and none
    /// where the organization has since been deleted.
    /// </summary>
    /// <param name="organizationName">The organization's current name, or <see langword="null" /> where it was deleted.</param>
    [Theory]
    [InlineData("Nordwind")]
    [InlineData(null)]
    public void For_AnOrganizationMember_PublishesTheOrganizationsCurrentName(string? organizationName)
    {
        // Arrange
        var message = MessageWith() with
        {
            SenderVerification = MessageWith().SenderVerification with
            {
                DeploymentTrust = SenderTrustLevel.Trusted,
                DeploymentTrustGrantedBy = SenderTrustSource.OrganizationMember,
                RecognizingOrganizationName = organizationName,
            },
        };

        // Act
        var sender = ClientMailMessageResponse.For(message, SenderContactStanding.Unanswered, NoPhotos).Sender;

        // Assert
        Assert.Equal("OrganizationMember", sender.TrustSource);
        Assert.Equal(organizationName, sender.TrustOrganization);
    }

    /// <summary>Only the organization source names an organization, so no other trust source carries a name.</summary>
    [Fact]
    public void For_AnAuthorAContactRecognized_PublishesNoOrganization()
    {
        // Arrange
        var message = MessageWith() with
        {
            SenderVerification = MessageWith().SenderVerification with
            {
                DeploymentTrust = SenderTrustLevel.Trusted,
                DeploymentTrustGrantedBy = SenderTrustSource.PersonalContact,
                RecognizingOrganizationName = "Nordwind",
            },
        };

        // Act
        var sender = ClientMailMessageResponse.For(message, SenderContactStanding.Unanswered, NoPhotos).Sender;

        // Assert
        Assert.Null(sender.TrustOrganization);
    }

    /// <summary>An author nothing recognized publishes no trust source, which is the ordinary state rather than missing data.</summary>
    [Fact]
    public void For_AnAuthorNothingRecognized_PublishesNoTrustSource()
    {
        // Arrange
        var message = MessageWith() with { SenderVerification = SenderVerification.NotEstablished };

        // Act
        var sender = ClientMailMessageResponse.For(message, SenderContactStanding.Unanswered, NoPhotos).Sender;

        // Assert
        Assert.Null(sender.TrustSource);
    }

    /// <summary>The route that established an authenticated author is published by its own name, and none where nothing did.</summary>
    /// <param name="route">The route the evidence records.</param>
    /// <param name="published">What the wire carries for it.</param>
    [Theory]
    [InlineData(AuthorAuthenticationRoute.DmarcPass, "DmarcPass")]
    [InlineData(AuthorAuthenticationRoute.AlignedDkim, "AlignedDkim")]
    [InlineData(AuthorAuthenticationRoute.AlignedSpf, "AlignedSpf")]
    [InlineData(AuthorAuthenticationRoute.None, null)]
    public void For_AnAuthenticationRoute_PublishesItByName(AuthorAuthenticationRoute route, string? published)
    {
        // Arrange
        var message = MessageWith(SenderAuthenticationEvidence.None with { AuthorRoute = route });

        // Act
        var sender = ClientMailMessageResponse.For(message, SenderContactStanding.Unanswered, NoPhotos).Sender;

        // Assert
        Assert.Equal(published, sender.AuthorRoute);
    }

    /// <summary>What tied a trusted message to its address is published by its own name, and none where nothing was asked of it.</summary>
    /// <param name="evidence">The evidence the verdict records.</param>
    /// <param name="published">What the wire carries for it.</param>
    [Theory]
    [InlineData(TrustedAuthorEvidence.Authentication, "Authentication")]
    [InlineData(TrustedAuthorEvidence.AuthorSignature, "AuthorSignature")]
    [InlineData(TrustedAuthorEvidence.None, null)]
    public void For_TrustEvidence_PublishesItByName(TrustedAuthorEvidence evidence, string? published)
    {
        // Arrange
        var message = MessageWith() with
        {
            SenderVerification = MessageWith().SenderVerification with { DeploymentTrustAuthorEvidence = evidence },
        };

        // Act
        var sender = ClientMailMessageResponse.For(message, SenderContactStanding.Unanswered, NoPhotos).Sender;

        // Assert
        Assert.Equal(published, sender.TrustEvidence);
    }

    /// <summary>Whose contact recognized the author is published beside a personal contact source and beside no other.</summary>
    /// <param name="grantedBy">What recognized the author.</param>
    /// <param name="published">What the wire carries for the reader's own contact.</param>
    [Theory]
    [InlineData(SenderTrustSource.PersonalContact, "Reader")]
    [InlineData(SenderTrustSource.OrganizationContact, null)]
    public void For_AReadersOwnContact_IsPublishedOnlyBesideAPersonalContactSource(SenderTrustSource grantedBy, string? published)
    {
        // Arrange
        var message = MessageWith() with
        {
            SenderVerification = MessageWith().SenderVerification with { DeploymentTrustGrantedBy = grantedBy },
        };
        var standing = new SenderContactStanding { RecognizingContactHolder = RecognizingContactHolder.Reader };

        // Act
        var sender = ClientMailMessageResponse.For(message, standing, NoPhotos).Sender;

        // Assert
        Assert.Equal(published, sender.TrustContact);
    }

    /// <summary>
    /// An offer to write the sender down is published with the contact it restates, or with none for a new contact, and
    /// is absent where it is not offered.
    /// </summary>
    [Fact]
    public void For_AnOfferToAddTheSender_PublishesTheContactItRestatesOrNone()
    {
        // Arrange
        var restated = ContactId.Create(Guid.Parse("0199f000-0000-7000-8000-0000000000c1"));

        // Act
        var restating = ClientMailMessageResponse.For(
            MessageWith(),
            new SenderContactStanding { ContactAddition = new ContactAdditionOffer(restated) }, NoPhotos).Sender;
        var recording = ClientMailMessageResponse.For(
            MessageWith(),
            new SenderContactStanding { ContactAddition = new ContactAdditionOffer(RestatedContactId: null) }, NoPhotos).Sender;
        var withheld = ClientMailMessageResponse.For(MessageWith(), SenderContactStanding.Unanswered, NoPhotos).Sender;

        // Assert
        Assert.Equal(restated.Value, restating.AddToContacts?.RestatedContactId);
        Assert.NotNull(recording.AddToContacts);
        Assert.Null(recording.AddToContacts.RestatedContactId);
        Assert.Null(withheld.AddToContacts);
    }

    /// <summary>The flags a pane draws are the ones the mail server last showed, and unread is the absence of the seen flag.</summary>
    [Fact]
    public void For_AReadMessage_CarriesTheFlagsTheServerLastShowed()
    {
        // Act
        var response = ClientMailMessageResponse.For(MessageWith(), SenderContactStanding.Unanswered, NoPhotos);

        // Assert
        Assert.True(response.Unread);
        Assert.False(response.Flagged);
        Assert.False(response.Answered);
    }

    /// <summary>
    /// A signature travels beside the sender verdict with each step's own result, so a popup draws the steps a
    /// verification never reached as well as those it did, and a mismatched signer as a fact of its own.
    /// </summary>
    [Fact]
    public void For_AMessageSignedBySomebodyElse_CarriesTheVerifiedStepsAndTheMismatch()
    {
        // Arrange
        var checkedAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var message = MessageWith() with
        {
            SenderVerification = MessageWith().SenderVerification with
            {
                Signature = new EmailSignature
                {
                    Format = EmailSignatureFormat.OpenPgpMime,
                    ContentCheck = SignatureCheckResult.Passed,
                    AuthorityCheck = SignatureCheckResult.Passed,
                    TrustAnchor = SignatureTrustAnchor.ConfiguredKey,
                    ValidityCheck = SignatureCheckResult.Passed,
                    RevocationCheck = SignatureCheckResult.Passed,
                    RevocationMethod = RevocationCheckMethod.KeyRevocationSignature,
                    RevocationCheckedAt = checkedAt,
                    SignerCheck = SignatureCheckResult.Failed,
                    Signer = new EmailSigner { Address = "robert@pgp.example.test", KeyId = "0123456789ABCDEF", KeyMaterial = new byte[] { 1, 2, 3 } },
                },
            },
        };

        // Act
        var signature = ClientMailMessageResponse.For(message, SenderContactStanding.Unanswered, NoPhotos).Signature;

        // Assert
        Assert.Equal("Verified", signature.Outcome);
        Assert.Equal("OpenPgpMime", signature.Format);
        Assert.Equal(new ClientMailSignatureStepsResponse("Passed", "Passed", "Passed", "Passed", "Failed"), signature.Steps);
        Assert.Equal("ConfiguredKey", signature.TrustAnchor);
        Assert.Equal("KeyRevocationSignature", signature.RevocationMethod);
        Assert.False(signature.SignerMatchesAuthor);
        Assert.Equal("robert@pgp.example.test", signature.Signer?.Address);
        Assert.Equal("0123456789ABCDEF", signature.Signer?.KeyId);
    }

    /// <summary>A message carrying no signature still carries the object, saying so, with every step skipped and no signer.</summary>
    [Fact]
    public void For_AnUnsignedMessage_CarriesNotSignedWithEveryStepSkipped()
    {
        // Act
        var signature = ClientMailMessageResponse.For(MessageWith(), SenderContactStanding.Unanswered, NoPhotos).Signature;

        // Assert
        Assert.Equal("NotSigned", signature.Outcome);
        Assert.Null(signature.Format);
        Assert.Equal(new ClientMailSignatureStepsResponse("Skipped", "Skipped", "Skipped", "Skipped", "Skipped"), signature.Steps);
        Assert.Null(signature.Signer);
    }

    /// <summary>An opened message carries the verdict its row carries, so a warning is drawn from the message already fetched.</summary>
    [Fact]
    public void For_AMessageThePersonMarkedSpam_CarriesTheirVerdictAndTheirSubcategory()
    {
        // Arrange
        var message = MessageWith() with
        {
            Spam = new EmailSpamVerdict(
                SpamVerdict.Spam,
                SpamClassificationStage.Person,
                SpamSubcategory.Scam,
                PhishingRequest: null,
                ImpersonatedParty: null),
        };

        // Act
        var response = ClientMailMessageResponse.For(message, SenderContactStanding.Unanswered, NoPhotos);

        // Assert
        Assert.Equal(new ClientMailSpamVerdictResponse("Spam", "Person", "Scam", "Person", null, null), response.Spam);
    }

    private static ReadEmailContent MessageWith(SenderAuthenticationEvidence? evidence = null) => new()
    {
        StoredEmailId = StoredEmailId.Create(Message),
        AccountId = MailAccountId.Create("primary"),
        Folder = new StoredEmailFolder(MailFolderId.Create(Guid.Parse("83ecb521-bf32-7f67-8287-e8b923809bb3")), "INBOX", MailFolderAlias.Create("INBOX")),
        SizeOctets = 40_960,
        Headers = new EmailContentHeaders(
            "Quarterly invoice",
            SentAt: null,
            ReceivedAt: null,
            [
                Participant(EmailAddressRole.From, "Billing", "billing@example.test"),
                Participant(EmailAddressRole.To, displayName: null, "reader@example.test"),
            ],
            EmailThreadReferences.Create("abc@example.test", inReplyTo: null, references: null)),
        Body = EmailContentBody.Readable(
            new EmailBodyRepresentation("Just words.", 11, EmailBodyTruncation.None),
            sanitizedHtml: null,
            document: null,
            selfContainedHtml: null,
            new EmailBodyForms(PlainText: true, Html: true)),
        AttachmentSummary = new StoredEmailAttachmentSummary(
            AttachmentCount: 2,
            TotalSizeOctets: 2_148,
            InlineResourceCount: 3,
            IsEncrypted: false,
            CarriesUnverifiedSignature: false,
            ContainsUnexpandedTnefPart: false),
        Attachments =
        [
            Attachment("invoice.pdf", "application/pdf", 2_048),
            Attachment("photo.jpg", "image/jpeg", 100),
        ],
        RemoteFlags = RemoteEmailFlagSnapshot.NeverObserved,
        SenderVerification = new SenderVerification
        {
            AuthorAuthentication = AuthorAuthenticationOutcome.Authenticated,
            DeploymentTrust = SenderTrustLevel.Trusted,
        },
        SenderAuthenticationEvidence = evidence ?? SenderAuthenticationEvidence.None,
        MachineAuthorship = MachineAuthorshipAssessment.NotAssessed,
        Thread = new ReadEmailThread
        {
            ThreadId = EmailThreadId.Create(Conversation),
            EmailCount = 1,
            MoreEmailsNotNamed = false,
            OtherEmails = [],
        },
    };

    private static EmailParticipant Participant(EmailAddressRole role, string? displayName, string address) =>
        new(role, EmailAddress.TryCreate(displayName, address, out var parsed) ? parsed : default);

    private static ReadEmailAttachment Attachment(string fileName, string mediaType, long sizeOctets) => new(
        new ExtractedEmailAttachment(
            AttachmentFileName.TryNormalize(fileName, out var normalized) ? normalized : null,
            mediaType,
            sizeOctets),
        AttachmentDownload.NotRequested);
}
