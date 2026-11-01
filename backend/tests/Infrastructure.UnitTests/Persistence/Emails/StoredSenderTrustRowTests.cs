// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Emails.Signatures;
using MailFathom.Infrastructure.Persistence.Emails;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Emails;

/// <summary>Covers how a stored row's verdict is read back and combined with the contacts its account's books hold now.</summary>
public sealed class StoredSenderTrustRowTests
{
    private static readonly ContactId Contact = ContactId.Create(Guid.Parse("0199f000-0000-7000-8000-0000000000c1"));

    /// <summary>A contact holding an authenticated author's address trusts the row on read, naming the contact found then.</summary>
    [Fact]
    public void Recognize_ContactHoldsAnAuthenticatedAuthor_TrustsTheRowThroughTheContact()
    {
        // Act
        var verdict = Row(AuthorAuthenticationRoute.DmarcPass).Recognize(new RecognizingContacts(null, Contact));

        // Assert
        Assert.Equal(SenderTrustLevel.Trusted, verdict.Level);
        Assert.Equal(SenderTrustSource.PersonalContact, verdict.GrantedBy);
        Assert.Equal(Contact, verdict.RecognizingContactId);
    }

    /// <summary>A row the books hold nobody for reads exactly as it was stored.</summary>
    [Fact]
    public void Recognize_NoContact_ReadsTheStoredVerdict()
    {
        // Arrange
        var row = Row(AuthorAuthenticationRoute.DmarcPass) with
        {
            Level = SenderTrustLevel.Trusted,
            GrantedBy = SenderTrustSource.OwnAccountAddress,
            AuthorEvidence = TrustedAuthorEvidence.Authentication,
        };

        // Act
        var read = row.Recognize(RecognizingContacts.None);

        // Assert
        Assert.Equal(SenderTrustLevel.Trusted, read.Level);
        Assert.Equal(SenderTrustSource.OwnAccountAddress, read.GrantedBy);
        Assert.Equal("0123456789abcdef0123456789abcdef", read.PolicyRevision.Value);
    }

    /// <summary>The author's own verified signature, rebuilt from the stored checks, ties the address where nothing authenticated it.</summary>
    [Fact]
    public void Recognize_StoredChecksOfAVerifiedAuthorSignature_TrustsTheRowOnTheSignature()
    {
        // Arrange
        var row = Row(AuthorAuthenticationRoute.None) with
        {
            AuthorAuthentication = AuthorAuthenticationOutcome.NotEstablished,
            IsStrict = true,
            SignatureContentCheck = SignatureCheckResult.Passed,
            SignatureAuthorityCheck = SignatureCheckResult.Passed,
            SignatureValidityCheck = SignatureCheckResult.Passed,
            SignatureRevocationCheck = SignatureCheckResult.Passed,
            SignatureSignerCheck = SignatureCheckResult.Passed,
        };

        // Act
        var verdict = row.Recognize(new RecognizingContacts(Contact, null));

        // Assert
        Assert.Equal(SenderTrustLevel.Trusted, verdict.Level);
        Assert.Equal(TrustedAuthorEvidence.AuthorSignature, verdict.AuthorEvidence);
    }

    /// <summary>A row stored strict is never known by address alone, however many contacts hold it.</summary>
    [Fact]
    public void Recognize_StrictRowNothingTied_StaysUnknown()
    {
        // Act
        var verdict = (Row(AuthorAuthenticationRoute.AlignedSpf) with { IsStrict = true })
            .Recognize(new RecognizingContacts(Contact, Contact));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, verdict.Level);
    }

    /// <summary>A row that keeps no sender address is asked about no contact.</summary>
    [Fact]
    public void Recognize_RowKeepingNoAddress_ReadsTheStoredVerdict()
    {
        // Act
        var verdict = (Row(AuthorAuthenticationRoute.DmarcPass) with { Address = null })
            .Recognize(new RecognizingContacts(null, Contact));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, verdict.Level);
        Assert.Null(verdict.RecognizingContactId);
    }

    private static StoredSenderTrustRow Row(AuthorAuthenticationRoute route) =>
        new(
            MailboxAccountId: "0199f000-0000-7000-8000-0000000000a1",
            Address: "ALICE@PARTNER.EXAMPLE",
            Level: SenderTrustLevel.Unknown,
            GrantedBy: SenderTrustSource.None,
            AuthorEvidence: TrustedAuthorEvidence.None,
            PolicyRevision: "0123456789abcdef0123456789abcdef",
            OrganizationId: null,
            IsStrict: false,
            AuthorAuthentication: AuthorAuthenticationOutcome.Authenticated,
            AuthorRoute: route,
            AuthenticatedAuthorDomain: "PARTNER.EXAMPLE",
            DmarcPolicy: DmarcPolicyReading.NotEstablished,
            SignatureContentCheck: SignatureCheckResult.Skipped,
            SignatureUnverifiableReason: null,
            SignatureAuthorityCheck: SignatureCheckResult.Skipped,
            SignatureValidityCheck: SignatureCheckResult.Skipped,
            SignatureRevocationCheck: SignatureCheckResult.Skipped,
            SignatureSignerCheck: SignatureCheckResult.Skipped);
}
