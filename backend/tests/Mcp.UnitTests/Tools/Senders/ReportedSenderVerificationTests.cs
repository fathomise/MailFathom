// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Emails.Signatures;
using MailFathom.Mcp.Tools.Senders;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools.Senders;

/// <summary>What the boundary publishes for the two conclusions an email carries about its displayed author.</summary>
/// <remarks>
/// The expected values are asserted as their member names rather than as the internal enumeration, because the wire
/// value is the camel-cased member name and a public test signature cannot carry an internal enum. Naming them here is
/// therefore both what the accessibility rules allow and what states the published contract.
/// </remarks>
public sealed class ReportedSenderVerificationTests
{
    /// <summary>Every combination the two axes reach is published as two independent values.</summary>
    /// <remarks>
    /// Six rows because the axes are independent and neither is derived from the other. The pair that matters most is
    /// authenticated beside unknown, which is the ordinary state of legitimate mail from a correspondent nobody has
    /// named and must never read as a failed authentication.
    /// </remarks>
    [Theory]
    [InlineData(AuthorAuthenticationOutcome.NotEstablished, SenderTrustLevel.Unknown, "NotEstablished", "Unknown")]
    [InlineData(AuthorAuthenticationOutcome.NotEstablished, SenderTrustLevel.Trusted, "NotEstablished", "Trusted")]
    [InlineData(AuthorAuthenticationOutcome.Failed, SenderTrustLevel.Unknown, "Failed", "Unknown")]
    [InlineData(AuthorAuthenticationOutcome.Failed, SenderTrustLevel.Trusted, "Failed", "Trusted")]
    [InlineData(AuthorAuthenticationOutcome.Authenticated, SenderTrustLevel.Unknown, "Authenticated", "Unknown")]
    [InlineData(AuthorAuthenticationOutcome.Authenticated, SenderTrustLevel.Trusted, "Authenticated", "Trusted")]
    public void From_StoredPair_PublishesBothOutcomesSeparately(
        AuthorAuthenticationOutcome authorAuthentication,
        SenderTrustLevel deploymentTrust,
        string expectedAuthorAuthentication,
        string expectedDeploymentTrust)
    {
        // Arrange
        var stored = new SenderVerification
        {
            AuthorAuthentication = authorAuthentication,
            DeploymentTrust = deploymentTrust,
        };

        // Act
        var published = ReportedSenderVerification.From(stored);

        // Assert
        Assert.Equal(expectedAuthorAuthentication, published.AuthorAuthentication.ToString());
        Assert.Equal(expectedDeploymentTrust, published.DeploymentTrust.ToString());
    }

    /// <summary>Mail stored before the columns existed is published as it is stored, not as a state nobody recorded.</summary>
    [Fact]
    public void From_VerdictNothingEverJudged_PublishesTheStoredDefaultRatherThanInventingAState()
    {
        // Act
        var published = ReportedSenderVerification.From(SenderVerification.NotEstablished);

        // Assert
        Assert.Equal("NotEstablished", published.AuthorAuthentication.ToString());
        Assert.Equal("Unknown", published.DeploymentTrust.ToString());
    }

    /// <summary>A stored author conclusion nobody decided how to publish is refused rather than guessed at.</summary>
    [Fact]
    public void From_AuthorConclusionWithNoPublishedValue_IsRefused()
    {
        // Arrange
        var stored = new SenderVerification
        {
            AuthorAuthentication = (AuthorAuthenticationOutcome)99,
            DeploymentTrust = SenderTrustLevel.Unknown,
        };

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => ReportedSenderVerification.From(stored));
    }

    /// <summary>A stored trust level nobody decided how to publish is refused rather than guessed at.</summary>
    [Fact]
    public void From_TrustLevelWithNoPublishedValue_IsRefused()
    {
        // Arrange
        var stored = new SenderVerification
        {
            AuthorAuthentication = AuthorAuthenticationOutcome.Authenticated,
            DeploymentTrust = (SenderTrustLevel)99,
        };

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => ReportedSenderVerification.From(stored));
    }

    /// <summary>A signature is published as provenance beside the two answers: the outcome, its reason, and who signed.</summary>
    [Fact]
    public void From_AVerificationThatCouldNotComplete_PublishesTheOutcomeItsReasonAndTheSigner()
    {
        // Arrange
        var signedAt = new DateTimeOffset(2026, 8, 27, 9, 13, 0, TimeSpan.Zero);
        var stored = SenderVerification.NotEstablished with
        {
            Signature = new EmailSignature
            {
                Format = EmailSignatureFormat.SecureMimeDetached,
                SignedAt = signedAt,
                ContentCheck = SignatureCheckResult.Passed,
                AuthorityCheck = SignatureCheckResult.Failed,
                SignerCheck = SignatureCheckResult.Passed,
                UnverifiableReason = EmailSignatureUnverifiableReason.UntrustedRoot,
                Signer = new EmailSigner { Address = "anna@signer.example.test" },
            },
        };

        // Act
        var published = ReportedSenderVerification.From(stored).Signature;

        // Assert
        Assert.Equal("NotVerifiable", published.Outcome.ToString());
        Assert.Equal("SecureMimeDetached", published.Format?.ToString());
        Assert.Equal("UntrustedRoot", published.UnverifiableReason?.ToString());
        Assert.True(published.SignerMatchesAuthor);
        Assert.Equal("anna@signer.example.test", published.SignerAddress);
        Assert.Equal(signedAt, published.SignedAt);
    }

    /// <summary>Every stored reason has a published value, so a reason added to the domain cannot reach a caller unnamed.</summary>
    [Fact]
    public void From_EveryUnverifiableReason_IsPublishedUnderItsOwnName()
    {
        // Arrange
        var reasons = Enum.GetValues<EmailSignatureUnverifiableReason>();

        // Act
        var published = reasons.Select(reason => ReportedSenderVerification.From(SenderVerification.NotEstablished with
        {
            Signature = new EmailSignature { Format = EmailSignatureFormat.OpenPgpMime, UnverifiableReason = reason },
        }).Signature.UnverifiableReason?.ToString());

        // Assert
        Assert.Equal(reasons.Select(reason => reason.ToString()), published);
    }

    /// <summary>A message carrying no signature is published as not signed, with nothing about a signer.</summary>
    [Fact]
    public void From_AnUnsignedMessage_PublishesNotSigned()
    {
        // Act
        var published = ReportedSenderVerification.From(SenderVerification.NotEstablished).Signature;

        // Assert
        Assert.Equal("NotSigned", published.Outcome.ToString());
        Assert.Null(published.Format);
        Assert.Null(published.SignerAddress);
        Assert.False(published.SignerMatchesAuthor);
    }

    /// <summary>Nothing is published for a pair that was never handed over.</summary>
    [Fact]
    public void From_WithoutAPair_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => ReportedSenderVerification.From(null!));
    }
}
