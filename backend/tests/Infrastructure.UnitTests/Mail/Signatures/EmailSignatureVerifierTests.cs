// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using MailFathom.Domain.Emails.Signatures;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Signatures;

public sealed class EmailSignatureVerifierTests
{
    private const string TestRoot = "O=MailFathom Tests, CN=MailFathom Test Root CA";

    [Fact]
    public async Task VerifyAsync_DetachedSecureMimeWithGoodOcspAnswer_VerifiesEveryStep()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders(ocspAnswer: "ocsp-anna-good.der");
        var message = SignatureFixtures.Message("smime-detached-anna.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders)
            .VerifyAsync(message, "anna@signer.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureOutcome.Verified, signature.Outcome);
        Assert.Equal(EmailSignatureFormat.SecureMimeDetached, signature.Format);
        Assert.Null(signature.UnverifiableReason);
        Assert.Equal(SignatureFixtures.SignedAt, signature.SignedAt);
        Assert.Equal(
            [SignatureCheckResult.Passed, SignatureCheckResult.Passed, SignatureCheckResult.Passed, SignatureCheckResult.Passed, SignatureCheckResult.Passed],
            [signature.ContentCheck, signature.AuthorityCheck, signature.ValidityCheck, signature.RevocationCheck, signature.SignerCheck]);
        Assert.Equal(SignatureTrustAnchor.ConfiguredAuthority, signature.TrustAnchor);
        Assert.Equal(TestRoot, signature.ChainEnd);
        Assert.Equal(RevocationCheckMethod.Ocsp, signature.RevocationMethod);
        Assert.Equal(SignatureFixtures.Now, signature.RevocationCheckedAt);
        Assert.True(signature.SignerMatchesAuthor);
    }

    [Fact]
    public async Task VerifyAsync_VerifiedSecureMimeSignature_RecordsTheSignerAndKeepsTheCertificateAsKeyMaterial()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders(ocspAnswer: "ocsp-anna-good.der");
        var message = SignatureFixtures.Message("smime-detached-anna.eml");

        // Act
        var signer = (await SignatureFixtures.Verifier(responders)
            .VerifyAsync(message, "anna@signer.example.test", TestContext.Current.CancellationToken)).Signer;

        // Assert
        Assert.NotNull(signer);
        Assert.Equal("O=Signer Example, CN=Anna Kowalska", signer.Name);
        Assert.Equal("anna@signer.example.test", signer.Address);
        Assert.Equal("O=MailFathom Tests, CN=MailFathom Test Issuing CA", signer.Issuer);
        Assert.Equal("1001", signer.SerialNumber);
        Assert.Equal("ECDSA P-256", signer.PublicKeyAlgorithm);
        Assert.Equal("SHA-256", signer.DigestAlgorithm);
        Assert.Equal(64, signer.Fingerprint?.Length);
        Assert.False(signer.KeyMaterial.IsEmpty);
    }

    [Fact]
    public async Task VerifyAsync_OpaqueSignedData_VerifiesAndRecordsTheDeclaredType()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders(ocspAnswer: "ocsp-anna-good.der");
        var message = SignatureFixtures.Message("smime-opaque-anna.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders)
            .VerifyAsync(message, "anna@signer.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureOutcome.Verified, signature.Outcome);
        Assert.Equal(EmailSignatureFormat.SecureMimeOpaque, signature.Format);
        Assert.Equal(SecureMimeContentType.SignedData, signature.SecureMimeType);
    }

    [Fact]
    public async Task VerifyAsync_SignedContentChangedAfterSigning_FailsWithoutAnUnverifiableReason()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders(ocspAnswer: "ocsp-anna-good.der");
        var tampered = Encoding.UTF8.GetString(SignatureFixtures.Bytes("smime-detached-anna.eml"))
            .Replace("attached to the next message", "payable to a new account", StringComparison.Ordinal);

        // Act
        var signature = await SignatureFixtures.Verifier(responders)
            .VerifyAsync(SignatureFixtures.MessageFrom(tampered), "anna@signer.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureOutcome.Failed, signature.Outcome);
        Assert.Equal(SignatureCheckResult.Failed, signature.ContentCheck);
        Assert.Null(signature.UnverifiableReason);
    }

    [Fact]
    public async Task VerifyAsync_SignerIsNotTheDisplayedAuthor_StaysVerifiedAndReportsTheMismatch()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders(ocspAnswer: "ocsp-anna-good.der");
        var message = SignatureFixtures.Message("smime-detached-anna.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders)
            .VerifyAsync(message, "ceo@impersonated.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureOutcome.Verified, signature.Outcome);
        Assert.Equal(SignatureCheckResult.Failed, signature.SignerCheck);
        Assert.False(signature.SignerMatchesAuthor);
        Assert.Equal("anna@signer.example.test", signature.Signer?.Address);
    }

    [Fact]
    public async Task VerifyAsync_OcspAnswersRevoked_IsNotVerifiableBecauseRevoked()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders(ocspAnswer: "ocsp-anna-revoked.der");
        var message = SignatureFixtures.Message("smime-detached-anna.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders)
            .VerifyAsync(message, "anna@signer.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureOutcome.NotVerifiable, signature.Outcome);
        Assert.Equal(EmailSignatureUnverifiableReason.Revoked, signature.UnverifiableReason);
        Assert.Equal(SignatureCheckResult.Failed, signature.RevocationCheck);
        Assert.Equal(new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero), signature.RevokedAt);
    }

    [Fact]
    public async Task VerifyAsync_EveryResponderUnreachable_IsNotVerifiableBecauseRevocationCannotBeChecked()
    {
        // Arrange
        using var responders = SignatureFixtures.UnreachableResponders();
        var message = SignatureFixtures.Message("smime-detached-anna.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders)
            .VerifyAsync(message, "anna@signer.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureOutcome.NotVerifiable, signature.Outcome);
        Assert.Equal(EmailSignatureUnverifiableReason.RevocationNotCheckable, signature.UnverifiableReason);
        Assert.Equal(SignatureCheckResult.NotChecked, signature.RevocationCheck);
        Assert.Equal(SignatureCheckResult.Passed, signature.ContentCheck);
    }

    [Fact]
    public async Task VerifyAsync_OcspResponderAnswersNothing_FallsBackToTheRevocationList()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders(revocationList: "crl-none-revoked.der");
        var message = SignatureFixtures.Message("smime-detached-anna.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders)
            .VerifyAsync(message, "anna@signer.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureOutcome.Verified, signature.Outcome);
        Assert.Equal(RevocationCheckMethod.Crl, signature.RevocationMethod);
        Assert.Equal(
            [SignatureFixtures.OcspResponder, SignatureFixtures.RevocationListLocation],
            [.. responders.RecordedRequests.Select(request => request.RequestUri)]);
    }

    [Fact]
    public async Task VerifyAsync_CertificateNamingOnlyARevocationListThatListsIt_IsNotVerifiableBecauseRevoked()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders(revocationList: "crl-celina-revoked.der");
        var message = SignatureFixtures.Message("smime-detached-celina.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders)
            .VerifyAsync(message, "celina@signer.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureUnverifiableReason.Revoked, signature.UnverifiableReason);
        Assert.Equal(RevocationCheckMethod.Crl, signature.RevocationMethod);
    }

    [Fact]
    public async Task VerifyAsync_CertificateNamingOnlyARevocationListThatDoesNotListIt_IsVerified()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders(revocationList: "crl-none-revoked.der");
        var message = SignatureFixtures.Message("smime-detached-celina.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders)
            .VerifyAsync(message, "celina@signer.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureOutcome.Verified, signature.Outcome);
        Assert.Equal(RevocationCheckMethod.Crl, signature.RevocationMethod);
    }

    [Fact]
    public async Task VerifyAsync_CertificateNamingNoRevocationSource_IsNotVerifiableAndAsksNobody()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders(ocspAnswer: "ocsp-anna-good.der", revocationList: "crl-none-revoked.der");
        var message = SignatureFixtures.Message("smime-detached-bartek.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders)
            .VerifyAsync(message, "bartek@signer.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureUnverifiableReason.RevocationNotCheckable, signature.UnverifiableReason);
        Assert.Empty(responders.RecordedRequests);
    }

    [Fact]
    public async Task VerifyAsync_CertificateExpiredBeforeSigning_IsNotVerifiableBecauseOutsideItsValidity()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders(revocationList: "crl-none-revoked.der");
        var message = SignatureFixtures.Message("smime-detached-edward.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders)
            .VerifyAsync(message, "edward@signer.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureUnverifiableReason.OutsideValidityPeriod, signature.UnverifiableReason);
        Assert.Equal(SignatureCheckResult.Failed, signature.ValidityCheck);
        Assert.Equal(SignatureCheckResult.Passed, signature.AuthorityCheck);
    }

    [Theory]
    [InlineData("smime-detached-paula.eml", "paula@signer.example.test")]
    [InlineData("smime-detached-kamil.eml", "kamil@signer.example.test")]
    public async Task VerifyAsync_CertificateNotIssuedForSigningMail_IsNotVerifiableBecauseOfItsKeyUsage(string fixture, string author)
    {
        // Arrange
        using var responders = SignatureFixtures.Responders(revocationList: "crl-none-revoked.der");
        var message = SignatureFixtures.Message(fixture);

        // Act
        var signature = await SignatureFixtures.Verifier(responders)
            .VerifyAsync(message, author, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureUnverifiableReason.WrongKeyUsage, signature.UnverifiableReason);
        Assert.Equal(EmailSignatureUnverifiableReason.WrongKeyUsage, signature.ValidityFailure);
    }

    [Fact]
    public async Task VerifyAsync_CertificateChainingToAnUntrustedRoot_IsNotVerifiableAndChecksNoRevocation()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders(ocspAnswer: "ocsp-anna-good.der", revocationList: "crl-none-revoked.der");
        var message = SignatureFixtures.Message("smime-detached-stefan.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders)
            .VerifyAsync(message, "stefan@elsewhere.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureUnverifiableReason.UntrustedRoot, signature.UnverifiableReason);
        Assert.Equal(SignatureCheckResult.Failed, signature.AuthorityCheck);
        Assert.Null(signature.TrustAnchor);
        Assert.Equal(SignatureCheckResult.Skipped, signature.RevocationCheck);
        Assert.Empty(responders.RecordedRequests);
    }

    [Fact]
    public async Task VerifyAsync_TestRootNotConfigured_IsNotVerifiableBecauseTheRootIsUntrusted()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders(ocspAnswer: "ocsp-anna-good.der");
        var message = SignatureFixtures.Message("smime-detached-anna.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders, trustTestAuthority: false)
            .VerifyAsync(message, "anna@signer.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureUnverifiableReason.UntrustedRoot, signature.UnverifiableReason);
        Assert.Equal(SignatureCheckResult.Passed, signature.ContentCheck);
    }

    [Fact]
    public async Task VerifyAsync_SignatureCarryingNoCertificate_IsNotVerifiableAndLeavesTheContentUnchecked()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders();
        var message = SignatureFixtures.Message("smime-detached-anna-without-certificates.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders)
            .VerifyAsync(message, "anna@signer.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureUnverifiableReason.NoCertificate, signature.UnverifiableReason);
        Assert.Equal(SignatureCheckResult.NotChecked, signature.ContentCheck);
        Assert.Equal(SignatureCheckResult.Skipped, signature.ValidityCheck);
        Assert.Null(signature.Signer);
    }

    [Fact]
    public async Task VerifyAsync_SameCertificateVerifiedTwice_AsksTheResponderOnce()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders(ocspAnswer: "ocsp-anna-good.der");
        var verifier = SignatureFixtures.Verifier(responders);

        // Act
        await verifier.VerifyAsync(SignatureFixtures.Message("smime-detached-anna.eml"), "anna@signer.example.test", TestContext.Current.CancellationToken);
        var second = await verifier.VerifyAsync(SignatureFixtures.Message("smime-opaque-anna.eml"), "anna@signer.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureOutcome.Verified, second.Outcome);
        Assert.Single(responders.RecordedRequests);
    }

    [Fact]
    public async Task VerifyAsync_OpenPgpSignatureByAConfiguredKey_VerifiesEveryStep()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders();
        var message = SignatureFixtures.Message("pgp-robert.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders, trustedKeys: "pgp-robert.asc")
            .VerifyAsync(message, "robert@pgp.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureOutcome.Verified, signature.Outcome);
        Assert.Equal(EmailSignatureFormat.OpenPgpMime, signature.Format);
        Assert.Equal(SignatureTrustAnchor.ConfiguredKey, signature.TrustAnchor);
        Assert.Equal(RevocationCheckMethod.KeyRevocationSignature, signature.RevocationMethod);
        Assert.True(signature.SignerMatchesAuthor);
        Assert.Equal(16, signature.Signer?.KeyId?.Length);
        Assert.False(signature.Signer?.KeyMaterial.IsEmpty);
        Assert.Empty(responders.RecordedRequests);
    }

    [Fact]
    public async Task VerifyAsync_OpenPgpSignatureByAKeyNobodyConfigured_IsNotVerifiableAndLeavesTheContentUnchecked()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders();
        var message = SignatureFixtures.Message("pgp-uma.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders, trustedKeys: "pgp-robert.asc")
            .VerifyAsync(message, "uma@pgp.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureUnverifiableReason.UnknownKey, signature.UnverifiableReason);
        Assert.Equal(SignatureCheckResult.NotChecked, signature.ContentCheck);
        Assert.Equal(SignatureCheckResult.Skipped, signature.SignerCheck);
        Assert.NotNull(signature.Signer?.KeyId);
    }

    [Theory]
    [InlineData("pgp-erik", "erik@pgp.example.test", EmailSignatureUnverifiableReason.ExpiredKey)]
    [InlineData("pgp-regina", "regina@pgp.example.test", EmailSignatureUnverifiableReason.RevokedKey)]
    public async Task VerifyAsync_OpenPgpKeyExpiredOrRevoked_IsNotVerifiableForThatReason(
        string fixture,
        string author,
        EmailSignatureUnverifiableReason expected)
    {
        // Arrange
        using var responders = SignatureFixtures.Responders();
        var message = SignatureFixtures.Message($"{fixture}.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders, trustedKeys: $"{fixture}.asc")
            .VerifyAsync(message, author, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expected, signature.UnverifiableReason);
        Assert.Equal(SignatureCheckResult.Passed, signature.ContentCheck);
    }

    [Fact]
    public async Task VerifyAsync_UnsignedMessage_IsNotSigned()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders();
        var message = SignatureFixtures.MessageFrom(
            "From: anna@signer.example.test\r\nSubject: Plain\r\nContent-Type: text/plain\r\n\r\nNothing signed here.\r\n");

        // Act
        var signature = await SignatureFixtures.Verifier(responders)
            .VerifyAsync(message, "anna@signer.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignature.NotSigned, signature);
        Assert.Equal(EmailSignatureOutcome.NotSigned, signature.Outcome);
    }

    [Theory]
    [InlineData("enveloped-data", SecureMimeContentType.EnvelopedData)]
    [InlineData("authEnveloped-data", SecureMimeContentType.AuthEnvelopedData)]
    [InlineData("compressed-data", SecureMimeContentType.CompressedData)]
    public async Task VerifyAsync_OpaqueBodyThatIsNotSignedData_IsNotVerifiableBecauseItWasNotOpened(
        string declaredType,
        SecureMimeContentType expected)
    {
        // Arrange
        using var responders = SignatureFixtures.Responders();
        var message = SignatureFixtures.MessageFrom(OpaqueMessage(declaredType));

        // Act
        var signature = await SignatureFixtures.Verifier(responders)
            .VerifyAsync(message, "anna@signer.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureOutcome.NotVerifiable, signature.Outcome);
        Assert.Equal(EmailSignatureUnverifiableReason.ContentNotOpened, signature.UnverifiableReason);
        Assert.Equal(expected, signature.SecureMimeType);
    }

    [Fact]
    public async Task VerifyAsync_CertificatesOnlyBody_IsNotSignedAndRecordsItsType()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders();
        var message = SignatureFixtures.MessageFrom(OpaqueMessage("certs-only"));

        // Act
        var signature = await SignatureFixtures.Verifier(responders)
            .VerifyAsync(message, "anna@signer.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureOutcome.NotSigned, signature.Outcome);
        Assert.Equal(SecureMimeContentType.CertsOnly, signature.SecureMimeType);
    }

    [Theory]
    [InlineData("application/pkcs7-signature", EmailSignatureFormat.SecureMimeDetached)]
    [InlineData("application/pgp-signature", EmailSignatureFormat.OpenPgpMime)]
    public async Task VerifyAsync_SignaturePartThatCannotBeRead_IsNotVerifiableBecauseMalformed(
        string protocol,
        EmailSignatureFormat expected)
    {
        // Arrange
        using var responders = SignatureFixtures.Responders();
        var message = SignatureFixtures.MessageFrom(
            "From: anna@signer.example.test\r\n" +
            $"Content-Type: multipart/signed; protocol=\"{protocol}\"; micalg=sha-256; boundary=\"b\"\r\n\r\n" +
            "--b\r\nContent-Type: text/plain\r\n\r\nHello.\r\n" +
            $"--b\r\nContent-Type: {protocol}\r\nContent-Transfer-Encoding: base64\r\n\r\nbm90IGEgc2lnbmF0dXJl\r\n" +
            "--b--\r\n");

        // Act
        var signature = await SignatureFixtures.Verifier(responders, trustedKeys: "pgp-robert.asc")
            .VerifyAsync(message, "anna@signer.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureOutcome.NotVerifiable, signature.Outcome);
        Assert.Equal(EmailSignatureUnverifiableReason.MalformedSignature, signature.UnverifiableReason);
        Assert.Equal(expected, signature.Format);
    }

    [Fact]
    public async Task VerifyAsync_OcspAnswerSignedByAKeyTheIssuerDoesNotHold_IsNotVerifiableBecauseRevocationCannotBeChecked()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders(ocspAnswer: "ocsp-anna-good-forged.der");
        var message = SignatureFixtures.Message("smime-detached-anna.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders)
            .VerifyAsync(message, "anna@signer.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureUnverifiableReason.RevocationNotCheckable, signature.UnverifiableReason);
        Assert.Equal(SignatureCheckResult.NotChecked, signature.RevocationCheck);
    }

    [Fact]
    public async Task VerifyAsync_OcspAnswerAboutAnotherCertificate_IsNotVerifiableBecauseRevocationCannotBeChecked()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders(ocspAnswer: "ocsp-anna-good.der");
        var message = SignatureFixtures.Message("smime-detached-dorota.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders)
            .VerifyAsync(message, "dorota@signer.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureUnverifiableReason.RevocationNotCheckable, signature.UnverifiableReason);
        Assert.Equal(SignatureCheckResult.Passed, signature.AuthorityCheck);
    }

    [Fact]
    public async Task VerifyAsync_RevocationListThatIsADelta_IsNotVerifiableBecauseRevocationCannotBeChecked()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders(revocationList: "crl-delta-none-revoked.der");
        var message = SignatureFixtures.Message("smime-detached-celina.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders)
            .VerifyAsync(message, "celina@signer.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureUnverifiableReason.RevocationNotCheckable, signature.UnverifiableReason);
        Assert.Equal(SignatureCheckResult.NotChecked, signature.RevocationCheck);
    }

    [Fact]
    public async Task VerifyAsync_SignatureCarryingMoreCertificatesThanTheBound_IsNotVerifiableBecauseMalformed()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders(ocspAnswer: "ocsp-anna-good.der");
        var message = SignatureFixtures.Message("smime-detached-anna-crowded.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders)
            .VerifyAsync(message, "anna@signer.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureUnverifiableReason.MalformedSignature, signature.UnverifiableReason);
        Assert.Empty(responders.RecordedRequests);
    }

    [Fact]
    public async Task VerifyAsync_OpenPgpSignedContentChangedAfterSigning_FailsWithoutAnUnverifiableReason()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders();
        var tampered = Encoding.UTF8.GetString(SignatureFixtures.Bytes("pgp-robert.eml"))
            .Replace("attached to the next message", "payable to a new account", StringComparison.Ordinal);

        // Act
        var signature = await SignatureFixtures.Verifier(responders, trustedKeys: "pgp-robert.asc")
            .VerifyAsync(SignatureFixtures.MessageFrom(tampered), "robert@pgp.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureOutcome.Failed, signature.Outcome);
        Assert.Equal(SignatureCheckResult.Failed, signature.ContentCheck);
        Assert.Null(signature.UnverifiableReason);
    }

    [Fact]
    public async Task VerifyAsync_OpenPgpSignatureByASigningSubkeyItsPrimaryBound_IsVerified()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders();
        var message = SignatureFixtures.Message("pgp-sara.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders, trustedKeys: "pgp-sara.asc")
            .VerifyAsync(message, "sara@pgp.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureOutcome.Verified, signature.Outcome);
        Assert.True(signature.SignerMatchesAuthor);
    }

    [Fact]
    public async Task VerifyAsync_OpenPgpSignatureByASubkeyAppendedWithoutTheTrustedPrimarysBinding_IsNotVerifiableBecauseTheKeyIsUnknown()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders();
        var message = SignatureFixtures.Message("pgp-orion.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders, trustedKeys: "pgp-orion.asc")
            .VerifyAsync(message, "orion@pgp.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureUnverifiableReason.UnknownKey, signature.UnverifiableReason);
        Assert.Equal(SignatureCheckResult.NotChecked, signature.ContentCheck);
    }

    [Fact]
    public async Task VerifyAsync_OneOpenPgpKeyConfiguredTwice_StillVerifiesItsSignature()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders();
        var message = SignatureFixtures.Message("pgp-robert.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders, true, "pgp-robert.asc", "pgp-robert.asc#rotated")
            .VerifyAsync(message, "robert@pgp.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureOutcome.Verified, signature.Outcome);
    }

    [Fact]
    public async Task VerifyAsync_IssuingAuthorityItsRootRevoked_IsNotVerifiableBecauseRevoked()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders(
            ocspAnswer: "ocsp-irena-good.der",
            rootRevocationList: "crl-root-revocable-revoked.der");
        var message = SignatureFixtures.Message("smime-detached-irena.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders)
            .VerifyAsync(message, "irena@signer.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureUnverifiableReason.Revoked, signature.UnverifiableReason);
        Assert.Equal(SignatureCheckResult.Failed, signature.RevocationCheck);
        Assert.Equal(RevocationCheckMethod.Crl, signature.RevocationMethod);
    }

    [Fact]
    public async Task VerifyAsync_EveryLinkOfTheChainAnsweredGood_IsVerifiedAndReportsTheSignersAnswer()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders(
            ocspAnswer: "ocsp-irena-good.der",
            rootRevocationList: "crl-root-none-revoked.der");
        var message = SignatureFixtures.Message("smime-detached-irena.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders)
            .VerifyAsync(message, "irena@signer.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureOutcome.Verified, signature.Outcome);
        Assert.Equal(RevocationCheckMethod.Ocsp, signature.RevocationMethod);
        Assert.Equal(
            [SignatureFixtures.OcspResponder, SignatureFixtures.RootRevocationListLocation],
            [.. responders.RecordedRequests.Select(request => request.RequestUri)]);
    }

    [Fact]
    public async Task VerifyAsync_IssuingAuthorityWhoseRevocationCannotBeChecked_IsNotVerifiable()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders(ocspAnswer: "ocsp-irena-good.der");
        var message = SignatureFixtures.Message("smime-detached-irena.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders)
            .VerifyAsync(message, "irena@signer.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureUnverifiableReason.RevocationNotCheckable, signature.UnverifiableReason);
    }

    [Theory]
    [InlineData("crl-ca-only.der")]
    [InlineData("crl-elsewhere.der")]
    [InlineData("crl-forged.der")]
    public async Task VerifyAsync_RevocationListScopedElsewhereOrNotSignedByTheIssuer_IsNotVerifiableBecauseRevocationCannotBeChecked(string revocationList)
    {
        // Arrange
        using var responders = SignatureFixtures.Responders(revocationList: revocationList);
        var message = SignatureFixtures.Message("smime-detached-celina.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders)
            .VerifyAsync(message, "celina@signer.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureUnverifiableReason.RevocationNotCheckable, signature.UnverifiableReason);
        Assert.Equal(SignatureCheckResult.NotChecked, signature.RevocationCheck);
    }

    [Fact]
    public async Task VerifyAsync_RevocationListScopedToTheLocationItWasReadAt_IsVerified()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders(revocationList: "crl-scoped-none-revoked.der");
        var message = SignatureFixtures.Message("smime-detached-celina.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders)
            .VerifyAsync(message, "celina@signer.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureOutcome.Verified, signature.Outcome);
    }

    [Fact]
    public async Task VerifyAsync_OcspAnswerNamingNoNextUpdateIssuedRecently_IsVerified()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders(ocspAnswer: "ocsp-anna-good-undated.der");
        var message = SignatureFixtures.Message("smime-detached-anna.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders)
            .VerifyAsync(message, "anna@signer.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureOutcome.Verified, signature.Outcome);
    }

    [Fact]
    public async Task VerifyAsync_OcspAnswerNamingNoNextUpdateIssuedLongAgo_IsNotVerifiableBecauseRevocationCannotBeChecked()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders(ocspAnswer: "ocsp-anna-good-undated-old.der");
        var message = SignatureFixtures.Message("smime-detached-anna.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders)
            .VerifyAsync(message, "anna@signer.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureUnverifiableReason.RevocationNotCheckable, signature.UnverifiableReason);
    }

    [Fact]
    public async Task VerifyAsync_OneEntryHoldingTheSameKeyTwice_IsPassedOverAndTheOtherKeysStillVouch()
    {
        // Arrange
        using var responders = SignatureFixtures.Responders();
        var message = SignatureFixtures.Message("pgp-sara.eml");

        // Act
        var signature = await SignatureFixtures.Verifier(responders, true, "pgp-robert-twice.asc", "pgp-sara.asc")
            .VerifyAsync(message, "sara@pgp.example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailSignatureOutcome.Verified, signature.Outcome);
    }

    private static string OpaqueMessage(string declaredType) =>
        "From: anna@signer.example.test\r\n" +
        $"Content-Type: application/pkcs7-mime; smime-type={declaredType}; name=smime.p7m\r\n" +
        "Content-Transfer-Encoding: base64\r\n\r\n" +
        "bm90IG9wZW5lZA==\r\n";
}
