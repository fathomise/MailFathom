// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.EmailContent.Rendering.Document.Blocks;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Provenance;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Spam.Model;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Spam.Model;

public sealed class SpamModelEvidenceReaderTests
{
    private static readonly StoredEmailId Email =
        StoredEmailId.Create(Guid.Parse("0199a0c0-0000-7000-8000-00000000e1de"));

    [Fact]
    public async Task ReadAsync_RenderedMessage_CarriesTheSenderSubjectBodyAndTheRecordedAuthorAuthentication()
    {
        // Arrange
        var failed = StubEmailProvenanceReader.Answering(StubEmailProvenanceReader.KnownCorrespondent with
        {
            Sender = new SenderVerification
            {
                AuthorAuthentication = AuthorAuthenticationOutcome.Failed,
                DeploymentTrust = SenderTrustLevel.Unknown,
            },
        });
        var reader = Reader(Rendering(Participants(From("Parcel Desk", "desk@example.com"))), failed);

        // Act
        var evidence = await reader.ReadAsync(Email, SomeContent(), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(evidence);
        Assert.Equal("desk@example.com", evidence.SenderAddress);
        Assert.Equal("Parcel Desk", evidence.SenderDisplayName);
        Assert.Equal("Your parcel is waiting", evidence.Subject);
        Assert.Equal("Pay the customs fee today.", evidence.Body);
        Assert.False(evidence.IsBodyCut);
        Assert.Equal(AuthorAuthenticationOutcome.Failed, evidence.AuthorAuthentication);
        Assert.False(evidence.IsSenderTrusted);
    }

    [Fact]
    public async Task ReadAsync_MessageWithNoRecordedProvenance_SaysNothingEstablishedTheAuthorAndNothingTrustsThem()
    {
        // Arrange
        var unrecorded = Substitute.For<IEmailProvenanceReader>();
        unrecorded
            .ReadAsync(Arg.Any<IReadOnlyCollection<StoredEmailId>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<StoredEmailId, EmailProvenance>());
        var reader = Reader(Rendering(Participants(From("Parcel Desk", "desk@example.com"))), unrecorded);

        // Act
        var evidence = await reader.ReadAsync(Email, SomeContent(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AuthorAuthenticationOutcome.NotEstablished, evidence?.AuthorAuthentication);
        Assert.False(evidence?.IsSenderTrusted);
    }

    [Fact]
    public async Task ReadAsync_MoreDistinctLinkHostsThanTheBound_CarriesOnlyTheFirstOnes()
    {
        // Arrange
        var runs = Enumerable.Range(0, SpamModelEvidence.MaximumLinks + 8)
            .Select(static position => new MailInlineRun(
                $"Offer {position}",
                MailTextEmphasis.None,
                Foreground: null,
                MailLinkReader.Read($"https://offer{position}.example.net/", $"Offer {position}")))
            .ToArray();
        var document = MailDocument.Reduced(
            [new MailParagraphBlock(runs, MailBlockAlignment.Inherited)],
            removedRemoteReferenceCount: 0,
            retainedRemoteImageCount: 0,
            inlineImageCount: 0,
            undrawnInlineImageCount: 0,
            truncated: false);
        var reader = Reader(Rendering(Participants(From("Deals", "deals@example.com")), document: document));

        // Act
        var evidence = await reader.ReadAsync(Email, SomeContent(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [.. Enumerable.Range(0, SpamModelEvidence.MaximumLinks).Select(static position => $"offer{position}.example.net")],
            evidence?.Links.Select(static link => link.TargetHost));
    }

    [Fact]
    public async Task ReadAsync_MoreAttachmentsThanTheBound_DescribesOnlyTheFirstOnes()
    {
        // Arrange
        var attachments = Enumerable.Range(0, SpamModelEvidence.MaximumAttachments + 4)
            .Select(static position => new ExtractedEmailAttachment(FileNamed($"scan-{position}.pdf"), "application/pdf", 1024))
            .ToArray();
        var reader = Reader(Rendering(Participants(From("Scanner", "scanner@example.com")), attachments: attachments));

        // Act
        var evidence = await reader.ReadAsync(Email, SomeContent(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [.. Enumerable.Range(0, SpamModelEvidence.MaximumAttachments).Select(static position => $"scan-{position}.pdf")],
            evidence?.Attachments.Select(static attachment => attachment.FileName));
    }

    [Fact]
    public async Task ReadAsync_AttachmentNameLongerThanTheBound_CutsTheName()
    {
        // Arrange
        var longName = string.Concat(new string('n', SpamModelEvidence.MaximumFileNameLength * 2), ".pdf");
        var reader = Reader(Rendering(
            Participants(From("Scanner", "scanner@example.com")),
            attachments: [new ExtractedEmailAttachment(FileNamed(longName), "application/pdf", 1024)]));

        // Act
        var evidence = await reader.ReadAsync(Email, SomeContent(), TestContext.Current.CancellationToken);

        // Assert
        var attachment = Assert.Single(evidence?.Attachments ?? []);
        Assert.Equal(SpamModelEvidence.MaximumFileNameLength, attachment.FileName?.Length);
    }

    [Fact]
    public async Task ReadAsync_ReplyToNamingAnotherAddress_CarriesItAndAReplyToNamingTheSenderDoesNot()
    {
        // Arrange
        var differing = Reader(Rendering(Participants(From("Billing", "billing@example.com"), ReplyTo("collect@example.net"))));
        var same = Reader(Rendering(Participants(From("Billing", "billing@example.com"), ReplyTo("Billing@Example.com"))));

        // Act
        var withDiffering = await differing.ReadAsync(Email, SomeContent(), TestContext.Current.CancellationToken);
        var withSame = await same.ReadAsync(Email, SomeContent(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("collect@example.net", withDiffering?.DifferingReplyToAddress);
        Assert.Null(withSame?.DifferingReplyToAddress);
    }

    [Fact]
    public async Task ReadAsync_LinkWhoseWordsNameAnotherHost_CarriesBothHostsAndSaysTheyDiffer()
    {
        // Arrange
        var link = MailLinkReader.Read("https://collect.example.net/pay", "www.bank.example.com")!;
        var honest = MailLinkReader.Read("https://help.example.org/", "Help centre")!;
        var document = MailDocument.Reduced(
            [
                new MailParagraphBlock(
                    [
                        new MailInlineRun("www.bank.example.com", MailTextEmphasis.None, Foreground: null, link),
                        new MailInlineRun("Help centre", MailTextEmphasis.None, Foreground: null, honest),
                        new MailInlineRun("www.bank.example.com", MailTextEmphasis.None, Foreground: null, link),
                    ],
                    MailBlockAlignment.Inherited),
            ],
            removedRemoteReferenceCount: 0,
            retainedRemoteImageCount: 0,
            inlineImageCount: 0,
            undrawnInlineImageCount: 0,
            truncated: false);
        var reader = Reader(Rendering(Participants(From("Bank", "notice@example.com")), document: document));

        // Act
        var evidence = await reader.ReadAsync(Email, SomeContent(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [
                new SpamModelLink("collect.example.net", "www.bank.example.com", DisplayedHostDiffers: true),
                new SpamModelLink("help.example.org", null, DisplayedHostDiffers: false),
            ],
            evidence?.Links);
    }

    [Fact]
    public async Task ReadAsync_AttachmentsAndATrustedSender_CarriesTheirTypesNamesAndTheTrust()
    {
        // Arrange
        var attachments = new[]
        {
            new ExtractedEmailAttachment(FileNamed("invoice.pdf.exe"), "application/octet-stream", 2048),
            new ExtractedEmailAttachment(FileName: null, "image/png", 512),
        };
        var trusted = StubEmailProvenanceReader.Answering(StubEmailProvenanceReader.KnownCorrespondent with
        {
            Sender = new SenderVerification
            {
                AuthorAuthentication = AuthorAuthenticationOutcome.Authenticated,
                DeploymentTrust = SenderTrustLevel.Trusted,
            },
        });
        var reader = Reader(Rendering(Participants(From("Supplier", "orders@example.com")), attachments: attachments), trusted);

        // Act
        var evidence = await reader.ReadAsync(Email, SomeContent(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [new SpamModelAttachment("application/octet-stream", "invoice.pdf.exe"), new SpamModelAttachment("image/png", null)],
            evidence?.Attachments);
        Assert.True(evidence?.IsSenderTrusted);
    }

    [Fact]
    public async Task ReadAsync_BodyLongerThanTheBound_CutsItAndSaysSo()
    {
        // Arrange
        var renderer = Substitute.For<IEmailContentRenderer>();
        renderer
            .RenderAsync(Arg.Any<StoredEmailContent>(), Arg.Any<EmailContentRenderingBounds>(), Arg.Any<CancellationToken>())
            .Returns(call => Rendering(
                Participants(From("Sender", "sender@example.com")),
                body: EmailBodyRepresentation.Bounded(
                    new string('a', 600),
                    EmailBodyCharacterAllowance.Of(
                        call.Arg<EmailContentRenderingBounds>().MaxCharactersPerRepresentation,
                        call.Arg<EmailContentRenderingBounds>().RemainingCharactersForRead))));
        var reader = new SpamModelEvidenceReader(
            renderer,
            StubEmailProvenanceReader.AllKnownCorrespondents,
            new SpamModelStageBounds(1, 500));

        // Act
        var evidence = await reader.ReadAsync(Email, SomeContent(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(500, evidence?.Body.Length);
        Assert.True(evidence?.IsBodyCut);
    }

    [Fact]
    public async Task ReadAsync_ContentNoRendererCanRead_ReadsNothing()
    {
        // Arrange
        var renderer = Substitute.For<IEmailContentRenderer>();
        renderer
            .RenderAsync(Arg.Any<StoredEmailContent>(), Arg.Any<EmailContentRenderingBounds>(), Arg.Any<CancellationToken>())
            .Returns(EmailContentRenderingResult.Unreadable());
        var reader = new SpamModelEvidenceReader(renderer, StubEmailProvenanceReader.AllKnownCorrespondents, SpamModelStageBounds.Default);

        // Act
        var evidence = await reader.ReadAsync(Email, SomeContent(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(evidence);
    }

    private static SpamModelEvidenceReader Reader(
        EmailContentRenderingResult rendering,
        IEmailProvenanceReader? provenance = null)
    {
        var renderer = Substitute.For<IEmailContentRenderer>();
        renderer
            .RenderAsync(Arg.Any<StoredEmailContent>(), Arg.Any<EmailContentRenderingBounds>(), Arg.Any<CancellationToken>())
            .Returns(rendering);

        return new SpamModelEvidenceReader(
            renderer,
            provenance ?? StubEmailProvenanceReader.AllKnownCorrespondents,
            SpamModelStageBounds.Default);
    }

    private static EmailContentRenderingResult Rendering(
        IReadOnlyList<EmailParticipant> participants,
        EmailBodyRepresentation? body = null,
        MailDocument? document = null,
        IReadOnlyList<ExtractedEmailAttachment>? attachments = null)
    {
        var described = attachments ?? [];
        var plainText = body ?? EmailBodyRepresentation.Bounded(
            "Pay the customs fee today.",
            EmailBodyCharacterAllowance.Of(8_000, 8_000));

        return EmailContentRenderingResult.Rendered(
            new EmailContentRendering(
                new EmailContentHeaders(
                    "Your parcel is waiting",
                    SentAt: null,
                    ReceivedAt: null,
                    participants,
                    EmailThreadReferences.None),
                plainText,
                SanitizedHtmlBody: null,
                new EmailBodyForms(PlainText: true, Html: false),
                BodyIsEncrypted: false,
                EmailAttachmentSummary.Create(
                    described,
                    inlineResourceCount: 0,
                    isEncrypted: false,
                    carriesUnverifiedSignature: false,
                    containsUnexpandedTnefPart: false),
                described)
            {
                Document = document,
            });
    }

    private static EmailParticipant[] Participants(params EmailParticipant[] participants) => participants;

    private static EmailParticipant From(string displayName, string address) =>
        new(EmailAddressRole.From, Address(displayName, address));

    private static EmailParticipant ReplyTo(string address) =>
        new(EmailAddressRole.ReplyTo, Address(null, address));

    private static EmailAddress Address(string? displayName, string address) =>
        EmailAddress.TryCreate(displayName, address, out var parsed)
            ? parsed
            : throw new InvalidOperationException("The synthetic address does not parse.");

    private static AttachmentFileName FileNamed(string name) =>
        AttachmentFileName.TryNormalize(name, out var fileName)
            ? fileName
            : throw new InvalidOperationException("The synthetic file name does not normalize.");

    private static StoredEmailContent SomeContent()
    {
        var rawMime = Encoding.ASCII.GetBytes("Subject: synthetic\r\n\r\nA synthetic body.\r\n");

        return new StoredEmailContent(rawMime, rawMime.Length, SHA256.HashData(rawMime));
    }
}
