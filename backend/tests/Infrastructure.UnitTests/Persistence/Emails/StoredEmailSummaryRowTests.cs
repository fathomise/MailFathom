// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Summaries;
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
        var summary = row.ToSummary(new Dictionary<Guid, IReadOnlyList<string>>());

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
        var summary = row.ToSummary(new Dictionary<Guid, IReadOnlyList<string>>());

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
        var summary = row.ToSummary(new Dictionary<Guid, IReadOnlyList<string>>());

        // Assert
        Assert.Same(MachineAuthorshipAssessment.NotAssessed, summary.MachineAuthorship);
        Assert.False(summary.MachineAuthorship.ProfileRevision.NamesAProfile);
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
        var summary = row.ToSummary(new Dictionary<Guid, IReadOnlyList<string>>());

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
        var summary = row.ToSummary(new Dictionary<Guid, IReadOnlyList<string>>());

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
        var signature = row.ToSummary(new Dictionary<Guid, IReadOnlyList<string>>()).SenderVerification.Signature;

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
        var signature = row.ToSummary(new Dictionary<Guid, IReadOnlyList<string>>()).SenderVerification.Signature;

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
            SenderTrustLevel = SenderTrustLevel.Trusted,
            AuthenticatedSenderDomain = "RELAY.EXAMPLE",
            DkimSignerDomain = "RELAY.EXAMPLE",
            SpfMailFromDomain = "PARTNER.EXAMPLE",
            SenderAuthenticationMethod = SenderAuthenticationMethod.DomainKeysIdentifiedMail,
            DmarcOutcome = DmarcOutcome.Pass,
            SenderTrustGrantedBy = SenderTrustSource.StoredTrustedSender,
        };

        // Act
        var summary = row.ToSummary(new Dictionary<Guid, IReadOnlyList<string>>());

        // Assert
        Assert.Equal("RELAY.EXAMPLE", summary.SenderAuthenticationEvidence.DkimDomain?.Value);
        Assert.Equal("PARTNER.EXAMPLE", summary.SenderAuthenticationEvidence.SpfDomain?.Value);
        Assert.Equal(SenderTrustSource.StoredTrustedSender, summary.SenderVerification.DeploymentTrustGrantedBy);
    }

    /// <summary>A row nothing authenticated reads back no DKIM or SPF domain and no trust source.</summary>
    [Fact]
    public void ToSummary_ARowNothingAuthenticated_CarriesNoDomainAndNoTrustSource()
    {
        // Arrange
        var row = RowWith(MachineAuthorshipBand.NotAssessed, 0);

        // Act
        var summary = row.ToSummary(new Dictionary<Guid, IReadOnlyList<string>>());

        // Assert
        Assert.Null(summary.SenderAuthenticationEvidence.DkimDomain);
        Assert.Null(summary.SenderAuthenticationEvidence.SpfDomain);
        Assert.Equal(SenderTrustSource.None, summary.SenderVerification.DeploymentTrustGrantedBy);
    }

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
        StoredEmailSignatureRow? signature = null) => new(
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
        SenderTrustLevel.Unknown,
        AuthenticatedSenderDomain: null,
        DisplayedAuthorDomain: null,
        SenderAuthenticationMethod.None,
        DmarcOutcome.NotReported,
        SenderAuthenticationSource.ReceivingServer,
        DkimSignerDomain: null,
        SpfMailFromDomain: null,
        SenderTrustSource.None,
        band,
        likelihood,
        MachineAuthorshipSignals.HiddenCharacters,
        MachineAuthorshipProfile.Standard.Revision.Value,
        signature ?? NotSignedRow(),
        spam?.Verdict,
        spam?.DecidedBy,
        spam?.Subcategory,
        spam?.PhishingRequest,
        spam?.ImpersonatedParty);
}
