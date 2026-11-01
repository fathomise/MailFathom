// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Delivery.Receipts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Emails.Signatures;
using MailFathom.Domain.Spam;
using MailFathom.Infrastructure.Persistence.Emails;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Emails;

/// <summary>
/// Covers how the returned columns are read back into a summary where the row holds something no writer produces.
/// </summary>
/// <remarks>
/// The projection itself is the integration suite's to prove, because only a database can say which columns a query
/// returns. What the row decides after they arrive is ordinary logic, and the authorship likelihood is the one place it
/// decides something a corrupted column would otherwise decide for it.
/// </remarks>
public sealed class StoredEmailSummaryRowTests
{
    /// <summary>A likelihood outside the scale is read as none of a reading rather than as the strongest one.</summary>
    /// <remarks>
    /// Reading it as the nearest end would republish a column nobody can account for as maximum confidence, which is
    /// the one answer an informational reading must never reach by accident.
    /// </remarks>
    [Theory]
    [InlineData(5)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ToSummary_ALikelihoodOutsideTheScale_IsReadAsZero(double stored)
    {
        // Arrange
        var row = RowWith(MachineAuthorshipBand.Likely, stored);

        // Act
        var summary = Summarize(row);

        // Assert
        Assert.Equal(0, summary.MachineAuthorship.Likelihood);
        Assert.Equal(MachineAuthorshipBand.Likely, summary.MachineAuthorship.Band);
    }

    /// <summary>A likelihood the reading actually produced is read back exactly, because two of them are compared.</summary>
    [Fact]
    public void ToSummary_AStoredLikelihood_IsReadBackUnchanged()
    {
        // Arrange
        var row = RowWith(MachineAuthorshipBand.Possible, 0.42);

        // Act
        var summary = Summarize(row);

        // Assert
        Assert.Equal(0.42, summary.MachineAuthorship.Likelihood);
        Assert.Equal(MachineAuthorshipSignals.HiddenCharacters, summary.MachineAuthorship.Signals);
        Assert.Equal(MachineAuthorshipProfile.Standard.Revision, summary.MachineAuthorship.ProfileRevision);
    }

    /// <summary>A row nothing assessed carries the not-assessed state whole, whatever the other three columns hold.</summary>
    [Fact]
    public void ToSummary_ARowNothingAssessed_IsReadAsNotAssessed()
    {
        // Arrange
        var row = RowWith(MachineAuthorshipBand.NotAssessed, 0.9);

        // Act
        var summary = Summarize(row);

        // Assert
        Assert.Same(MachineAuthorshipAssessment.NotAssessed, summary.MachineAuthorship);
        Assert.False(summary.MachineAuthorship.ProfileRevision.NamesAProfile);
    }

    /// <summary>A report's row reads back the outcomes it named, leaving out a value no outcome carries rather than inventing one.</summary>
    [Fact]
    public void ToSummary_AReportRow_CarriesTheRecipientsAndTheOwnMessageItNamed()
    {
        // Arrange
        var row = RowWith(
            MachineAuthorshipBand.NotAssessed,
            likelihood: 0,
            reportKind: ReceiptKind.Read,
            reportRecipients: ["anna@example.test", "bert@example.test"],
            reportOutcomes: [(int)ReceiptOutcome.Displayed, 99],
            reportOutgoingEmailId: Guid.CreateVersion7());

        // Act
        var summary = Summarize(row);

        // Assert
        Assert.NotNull(summary.Report);
        Assert.Equal(ReceiptKind.Read, summary.Report.Kind);
        Assert.Equal([new EmailReportRecipient("anna@example.test", ReceiptOutcome.Displayed)], summary.Report.Recipients);
        Assert.True(summary.Report.NamesOwnMessage);
        Assert.Equal("Lunch", summary.Report.OwnMessageSubject);
    }

    /// <summary>Ordinary mail carries no report mark.</summary>
    [Fact]
    public void ToSummary_AnOrdinaryRow_CarriesNoReport()
    {
        // Act
        var summary = Summarize(RowWith(MachineAuthorshipBand.NotAssessed, likelihood: 0));

        // Assert
        Assert.Null(summary.Report);
    }

    /// <summary>A row is the reader's own sent copy exactly when the send recorded filing it, which nothing in the message can claim.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ToSummary_ARowWhetherOrNotFiledFromASend_SaysWhetherItIsTheFiledSentCopy(bool filedFromASend)
    {
        // Act
        var summary = Summarize(RowWith(
            MachineAuthorshipBand.NotAssessed,
            likelihood: 0,
            filedFromOutgoingEmailId: filedFromASend ? Guid.CreateVersion7() : null));

        // Assert
        Assert.Equal(filedFromASend, summary.IsFiledSentCopy);
    }

    /// <summary>A recorded classification reaches the summary whole: the verdict, both judges, and the phishing detail.</summary>
    [Fact]
    public void ToSummary_ARowWithAClassification_CarriesTheVerdictAStageReached()
    {
        // Arrange
        var recorded = new EmailSpamVerdict(
            SpamVerdict.Spam,
            SpamClassificationStage.Deterministic,
            SpamSubcategory.Phishing,
            SpamPhishingRequest.Credentials,
            SpamImpersonatedParty.Bank);
        var row = RowWith(MachineAuthorshipBand.NotAssessed, 0, recorded);

        // Act
        var summary = Summarize(row);

        // Assert
        Assert.Equal(recorded, summary.Spam);
        Assert.Equal(SpamClassificationStage.Model, summary.Spam?.SubcategoryNamedBy);
    }

    /// <summary>A message no stage has reached carries no verdict at all, which is not the same as one a stage cleared.</summary>
    [Fact]
    public void ToSummary_ARowNothingClassified_CarriesNoSpamVerdict()
    {
        // Arrange
        var row = RowWith(MachineAuthorshipBand.NotAssessed, 0);

        // Act
        var summary = Summarize(row);

        // Assert
        Assert.Null(summary.Spam);
    }

    /// <summary>A stored verification reaches the summary step by step, with the signer it named.</summary>
    [Fact]
    public void ToSummary_ARowWithAVerifiedSignature_CarriesEveryStepAndTheSigner()
    {
        // Arrange
        var signedAt = new DateTimeOffset(2026, 8, 27, 9, 13, 0, TimeSpan.Zero);
        var checkedAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var row = RowWith(MachineAuthorshipBand.NotAssessed, 0, signature: new StoredEmailSignatureRow(
            EmailSignatureFormat.SecureMimeDetached,
            SecureMimeType: null,
            UnverifiableReason: null,
            signedAt,
            SignatureCheckResult.Passed,
            SignatureCheckResult.Passed,
            SignatureTrustAnchor.ConfiguredAuthority,
            "CN=MailFathom Test Root CA",
            SignatureCheckResult.Passed,
            ValidityFailure: null,
            SignatureCheckResult.Passed,
            RevocationCheckMethod.Ocsp,
            checkedAt,
            RevokedAt: null,
            SignatureCheckResult.Failed,
            "CN=Anna Kowalska",
            "anna@signer.example.test",
            "CN=MailFathom Test Issuing CA",
            SignerValidFrom: null,
            SignerValidUntil: null,
            "1001",
            "AB12",
            SignerKeyId: null,
            "ECDSA P-256",
            "SHA-256"));

        // Act
        var signature = Summarize(row).SenderVerification.Signature;

        // Assert
        Assert.Equal(EmailSignatureOutcome.Verified, signature.Outcome);
        Assert.False(signature.SignerMatchesAuthor);
        Assert.Equal(RevocationCheckMethod.Ocsp, signature.RevocationMethod);
        Assert.Equal(checkedAt, signature.RevocationCheckedAt);
        Assert.Equal("anna@signer.example.test", signature.Signer?.Address);
        Assert.True(signature.Signer?.KeyMaterial.IsEmpty);
    }

    /// <summary>A row no verification found a signature on reads as not signed, with no signer.</summary>
    [Fact]
    public void ToSummary_ARowWithNoSignature_IsReadAsNotSigned()
    {
        // Arrange
        var row = RowWith(MachineAuthorshipBand.NotAssessed, 0);

        // Act
        var signature = Summarize(row).SenderVerification.Signature;

        // Assert
        Assert.Equal(EmailSignature.NotSigned, signature);
    }

    /// <summary>
    /// A row reads back the DKIM and SPF domains that passed beside the authenticated one, and what recognized a trusted
    /// author, which is what a details popup lists and what no other column says.
    /// </summary>
    [Fact]
    public void ToSummary_ARowAServerAuthenticatedAndATrustedSenderMatched_CarriesTheDomainsThatPassedAndWhatRecognizedTheAuthor()
    {
        // Arrange
        var row = RowWith(MachineAuthorshipBand.NotAssessed, 0) with
        {
            AuthorAuthenticationOutcome = AuthorAuthenticationOutcome.Authenticated,
            AuthenticatedSenderDomain = "RELAY.EXAMPLE",
            DkimSignerDomain = "RELAY.EXAMPLE",
            SpfMailFromDomain = "PARTNER.EXAMPLE",
            SenderAuthenticationMethod = SenderAuthenticationMethod.DomainKeysIdentifiedMail,
            DmarcOutcome = DmarcOutcome.Pass,
        };
        var verdict = SenderTrust.TrustedByContact(
            SenderTrustSource.PersonalContact,
            ContactId.Create(Guid.CreateVersion7()),
            TrustedAuthorEvidence.Authentication,
            Revision);

        // Act
        var summary = Summarize(row, verdict);

        // Assert
        Assert.Equal("RELAY.EXAMPLE", summary.SenderAuthenticationEvidence.DkimDomain?.Value);
        Assert.Equal("PARTNER.EXAMPLE", summary.SenderAuthenticationEvidence.SpfDomain?.Value);
        Assert.Equal(SenderTrustSource.PersonalContact, summary.SenderVerification.DeploymentTrustGrantedBy);
    }

    /// <summary>
    /// A row a personal contact recognized reads back the route that established its author, what tied the message to
    /// the address, and the contact that recognized it, which is what the sender details name and nothing else carries.
    /// </summary>
    [Fact]
    public void ToSummary_ARowAContactRecognized_CarriesTheRouteTheEvidenceAndTheRecognizingContact()
    {
        // Arrange
        var contactId = Guid.CreateVersion7();
        var row = RowWith(MachineAuthorshipBand.NotAssessed, 0) with
        {
            AuthorAuthenticationOutcome = AuthorAuthenticationOutcome.Authenticated,
            AuthorAuthenticationRoute = AuthorAuthenticationRoute.AlignedDkim,
        };
        var verdict = SenderTrust.TrustedByContact(
            SenderTrustSource.PersonalContact,
            ContactId.Create(contactId),
            TrustedAuthorEvidence.AuthorSignature,
            Revision);

        // Act
        var summary = Summarize(row, verdict);

        // Assert
        Assert.Equal(AuthorAuthenticationRoute.AlignedDkim, summary.SenderAuthenticationEvidence.AuthorRoute);
        Assert.Equal(TrustedAuthorEvidence.AuthorSignature, summary.SenderVerification.DeploymentTrustAuthorEvidence);
        Assert.Equal(contactId, summary.SenderVerification.RecognizingContactId?.Value);
    }

    /// <summary>A row nothing authenticated reads back no DKIM or SPF domain and no trust source.</summary>
    [Fact]
    public void ToSummary_ARowNothingAuthenticated_CarriesNoDomainAndNoTrustSource()
    {
        // Arrange
        var row = RowWith(MachineAuthorshipBand.NotAssessed, 0);

        // Act
        var summary = Summarize(row);

        // Assert
        Assert.Null(summary.SenderAuthenticationEvidence.DkimDomain);
        Assert.Null(summary.SenderAuthenticationEvidence.SpfDomain);
        Assert.Equal(SenderTrustSource.None, summary.SenderVerification.DeploymentTrustGrantedBy);
        Assert.Equal(AuthorAuthenticationRoute.None, summary.SenderAuthenticationEvidence.AuthorRoute);
        Assert.Null(summary.SenderVerification.RecognizingContactId);
    }

    /// <summary>
    /// A row an organization's signature recognized reads back that organization's current name, so a rename since the
    /// message was stored reaches it.
    /// </summary>
    [Fact]
    public void ToSummary_ARowAnOrganizationRecognized_CarriesTheOrganizationsCurrentName()
    {
        // Arrange
        var organizationId = Guid.CreateVersion7();
        var row = RowWith(MachineAuthorshipBand.NotAssessed, 0);
        var verdict = SenderTrust.TrustedAsOrganizationMember(organizationId, Revision);

        // Act
        var summary = Summarize(row, verdict, new Dictionary<Guid, string> { [organizationId] = "Nordwind" });

        // Assert
        Assert.Equal(SenderTrustSource.OrganizationMember, summary.SenderVerification.DeploymentTrustGrantedBy);
        Assert.Equal("Nordwind", summary.SenderVerification.RecognizingOrganizationName);
    }

    /// <summary>
    /// A row whose organization was deleted since keeps the reason and carries no name, whether the column was cleared
    /// or still names an organization the names no longer hold.
    /// </summary>
    /// <param name="namesTheOrganization">Whether the row's column still names the deleted organization.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ToSummary_ARowWhoseOrganizationWasDeleted_KeepsTheReasonWithoutAName(bool namesTheOrganization)
    {
        // Arrange
        var row = RowWith(MachineAuthorshipBand.NotAssessed, 0);
        var verdict = SenderTrust.FromStored(
            SenderTrustLevel.Trusted,
            SenderTrustSource.OrganizationMember,
            TrustedAuthorEvidence.None,
            Revision,
            namesTheOrganization ? Guid.CreateVersion7() : null,
            isStrict: false);

        // Act
        var summary = Summarize(row, verdict);

        // Assert
        Assert.Equal(SenderTrustSource.OrganizationMember, summary.SenderVerification.DeploymentTrustGrantedBy);
        Assert.Null(summary.SenderVerification.RecognizingOrganizationName);
    }

    private static SenderTrustPolicyRevision Revision { get; } = SenderTrustPolicyRevision.Of(["own:ME@WORK.EXAMPLE"]);

    private static EmailSummary Summarize(
        StoredEmailSummaryRow row,
        SenderTrust? senderTrust = null,
        IReadOnlyDictionary<Guid, string>? organizationNames = null) =>
        row.ToSummary(
            new Dictionary<Guid, IReadOnlyList<string>>(),
            senderTrust ?? SenderTrust.NotEvaluated,
            organizationNames ?? new Dictionary<Guid, string>());

    private static StoredEmailSignatureRow NotSignedRow() => new(
        Format: null,
        SecureMimeType: null,
        UnverifiableReason: null,
        SignedAt: null,
        SignatureCheckResult.Skipped,
        SignatureCheckResult.Skipped,
        TrustAnchor: null,
        ChainEnd: null,
        SignatureCheckResult.Skipped,
        ValidityFailure: null,
        SignatureCheckResult.Skipped,
        RevocationMethod: null,
        RevocationCheckedAt: null,
        RevokedAt: null,
        SignatureCheckResult.Skipped,
        SignerName: null,
        SignerAddress: null,
        SignerIssuer: null,
        SignerValidFrom: null,
        SignerValidUntil: null,
        SignerSerialNumber: null,
        SignerFingerprint: null,
        SignerKeyId: null,
        SignerPublicKeyAlgorithm: null,
        SignerDigestAlgorithm: null);

    private static StoredEmailSummaryRow RowWith(
        MachineAuthorshipBand band,
        double likelihood,
        EmailSpamVerdict? spam = null,
        StoredEmailSignatureRow? signature = null,
        ReceiptKind? reportKind = null,
        string[]? reportRecipients = null,
        int[]? reportOutcomes = null,
        Guid? reportOutgoingEmailId = null,
        Guid? filedFromOutgoingEmailId = null) => new(
        Guid.CreateVersion7(),
        "primary",
        Guid.CreateVersion7(),
        "INBOX",
        ThreadId: null,
        InternetMessageId: null,
        Subject: "Quarterly invoice",
        SentAt: null,
        ReceivedAt: null,
        SizeOctets: 4096,
        SenderDisplayName: null,
        SenderAddress: "billing@partner.example",
        ToAddresses: [],
        AttachmentCount: 0,
        AttachmentTotalSizeOctets: 0,
        InlineResourceCount: 0,
        IsEncrypted: false,
        CarriesUnverifiedSignature: false,
        ContainsUnexpandedTnefPart: false,
        StoredEmailContentAvailability.Available,
        RemoteFlagsObservedAt: null,
        IsRemotelySeen: false,
        IsRemotelyAnswered: false,
        IsRemotelyFlagged: false,
        IsRemotelyDraft: false,
        IsRemotelyDeleted: false,
        RemoteKeywords: [],
        AuthorAuthenticationOutcome.NotEstablished,
        AuthorAuthenticationRoute.None,
        AuthenticatedSenderDomain: null,
        DisplayedAuthorDomain: null,
        SenderAuthenticationMethod.None,
        DmarcOutcome.NotReported,
        SenderAuthenticationSource.ReceivingServer,
        DkimSignerDomain: null,
        SpfMailFromDomain: null,
        new StoredSenderTrustRow(
            MailboxAccountId: "primary",
            Address: "BILLING@PARTNER.EXAMPLE",
            SenderTrustLevel.Unknown,
            SenderTrustSource.None,
            TrustedAuthorEvidence.None,
            PolicyRevision: null,
            OrganizationId: null,
            IsStrict: false,
            AuthorAuthenticationOutcome.NotEstablished,
            AuthorAuthenticationRoute.None,
            AuthenticatedAuthorDomain: null,
            DmarcPolicyReading.NotEstablished,
            SignatureCheckResult.Skipped,
            SignatureUnverifiableReason: null,
            SignatureCheckResult.Skipped,
            SignatureCheckResult.Skipped,
            SignatureCheckResult.Skipped,
            SignatureCheckResult.Skipped),
        band,
        likelihood,
        MachineAuthorshipSignals.HiddenCharacters,
        MachineAuthorshipProfile.Standard.Revision.Value,
        signature ?? NotSignedRow(),
        spam?.Verdict,
        spam?.DecidedBy,
        spam?.Subcategory,
        spam?.PhishingRequest,
        spam?.ImpersonatedParty,
        reportKind,
        reportRecipients ?? [],
        reportOutcomes ?? [],
        reportOutgoingEmailId,
        ReportOwnMessageSubject: reportOutgoingEmailId is null ? null : "Lunch",
        ReadReceiptRequestedTo: null,
        ReadReceiptRequestNamesAnotherAddress: false,
        ReadReceiptSentAt: null,
        filedFromOutgoingEmailId);
}
