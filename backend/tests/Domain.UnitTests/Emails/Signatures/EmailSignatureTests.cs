// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails.Signatures;
using Xunit;

namespace MailFathom.Domain.UnitTests.Emails.Signatures;

/// <summary>The outcome is derived from the steps, so no producer can publish a verified signature a step contradicts.</summary>
public sealed class EmailSignatureTests
{
    private static readonly EmailSignature EveryStepPassed = new()
    {
        Format = EmailSignatureFormat.SecureMimeDetached,
        ContentCheck = SignatureCheckResult.Passed,
        AuthorityCheck = SignatureCheckResult.Passed,
        ValidityCheck = SignatureCheckResult.Passed,
        RevocationCheck = SignatureCheckResult.Passed,
    };

    [Fact]
    public void Outcome_ContentAndEveryTrustStepPassed_IsVerified()
    {
        // Act
        var outcome = EveryStepPassed.Outcome;

        // Assert
        Assert.Equal(EmailSignatureOutcome.Verified, outcome);
    }

    [Theory]
    [InlineData(SignatureCheckResult.Failed, SignatureCheckResult.Passed, SignatureCheckResult.Passed)]
    [InlineData(SignatureCheckResult.Passed, SignatureCheckResult.Skipped, SignatureCheckResult.Passed)]
    [InlineData(SignatureCheckResult.Passed, SignatureCheckResult.Passed, SignatureCheckResult.NotChecked)]
    public void Outcome_ContentPassedBesideATrustStepThatDidNotPassAndNoReason_IsNotVerifiable(
        SignatureCheckResult authority,
        SignatureCheckResult validity,
        SignatureCheckResult revocation)
    {
        // Arrange
        var signature = EveryStepPassed with { AuthorityCheck = authority, ValidityCheck = validity, RevocationCheck = revocation };

        // Act
        var outcome = signature.Outcome;

        // Assert
        Assert.Equal(EmailSignatureOutcome.NotVerifiable, outcome);
    }

    [Fact]
    public void Outcome_ContentMismatchBesideEveryOtherStepPassed_IsFailed()
    {
        // Act
        var outcome = (EveryStepPassed with { ContentCheck = SignatureCheckResult.Failed }).Outcome;

        // Assert
        Assert.Equal(EmailSignatureOutcome.Failed, outcome);
    }
}
