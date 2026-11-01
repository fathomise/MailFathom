// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Emails.Signatures;
using Xunit;

namespace MailFathom.Domain.UnitTests.Emails.Authentication;

/// <summary>Covers how a stored verdict is combined with the contacts the books hold when a message is read.</summary>
public sealed class SenderTrustRecognitionTests
{
    private static readonly Guid Organization = Guid.Parse("0199f000-0000-7000-8000-000000000001");

    private static readonly ContactId OrganizationContact = ContactId.Create(Guid.Parse("0199f000-0000-7000-8000-0000000000c1"));

    private static readonly ContactId PersonalContact = ContactId.Create(Guid.Parse("0199f000-0000-7000-8000-0000000000c2"));

    private static readonly SenderTrustPolicyRevision Revision = SenderTrustPolicyRevision.Of(["own:ME@WORK.EXAMPLE"]);

    /// <summary>A contact written after the message was stored recognizes it on the next read, with nothing stored having changed.</summary>
    [Fact]
    public void RecognizedBy_ContactWrittenAfterTheMessageWasStored_TrustsTheAuthorAndNamesTheContact()
    {
        // Arrange
        var stored = SenderTrust.FromStored(SenderTrustLevel.Unknown, SenderTrustSource.None, TrustedAuthorEvidence.None, Revision, null, isStrict: false);

        // Act
        var read = stored.RecognizedBy(new RecognizingContacts(null, PersonalContact), Recognition(AuthorAuthenticationRoute.DmarcPass));

        // Assert
        Assert.Equal(SenderTrustLevel.Trusted, read.Level);
        Assert.Equal(SenderTrustSource.PersonalContact, read.GrantedBy);
        Assert.Equal(PersonalContact, read.RecognizingContactId);
        Assert.Equal(TrustedAuthorEvidence.Authentication, read.AuthorEvidence);
        Assert.Equal(Revision, read.PolicyRevision);
    }

    /// <summary>A contact removed after it granted trust leaves nothing to recognize the author by, so the stored answer stands.</summary>
    [Fact]
    public void RecognizedBy_NoContactHoldsTheAddressAnyMore_LeavesTheStoredVerdict()
    {
        // Arrange
        var stored = SenderTrust.FromStored(SenderTrustLevel.Unknown, SenderTrustSource.None, TrustedAuthorEvidence.None, Revision, null, isStrict: false);

        // Act
        var read = stored.RecognizedBy(RecognizingContacts.None, Recognition(AuthorAuthenticationRoute.DmarcPass));

        // Assert
        Assert.Same(stored, read);
        Assert.Null(read.RecognizingContactId);
    }

    /// <summary>An organization contact outranks the owner's own address, and a personal contact ranks below it.</summary>
    [Theory]
    [InlineData(true, true, SenderTrustSource.OrganizationContact)]
    [InlineData(false, true, SenderTrustSource.OwnAccountAddress)]
    public void RecognizedBy_StoredOwnAddressAndContacts_ReportsTheHighestSource(
        bool organizationContact,
        bool personalContact,
        SenderTrustSource expected)
    {
        // Arrange
        var stored = SenderTrust.TrustedAsOwn(SenderTrustSource.OwnAccountAddress, TrustedAuthorEvidence.Authentication, Revision);

        // Act
        var read = stored.RecognizedBy(
            new RecognizingContacts(organizationContact ? OrganizationContact : null, personalContact ? PersonalContact : null),
            Recognition(AuthorAuthenticationRoute.DmarcPass));

        // Assert
        Assert.Equal(expected, read.GrantedBy);
    }

    /// <summary>The organization's signature proves more than any contact does, so a contact never displaces it.</summary>
    [Fact]
    public void RecognizedBy_StoredOrganizationMember_IsNeverDisplaced()
    {
        // Arrange
        var stored = SenderTrust.TrustedAsOrganizationMember(Organization, Revision);

        // Act
        var read = stored.RecognizedBy(new RecognizingContacts(OrganizationContact, PersonalContact), Recognition(AuthorAuthenticationRoute.DmarcPass));

        // Assert
        Assert.Same(stored, read);
    }

    /// <summary>A strict account kept as strict in the stored half never knows a contact by address alone on a later read.</summary>
    [Theory]
    [InlineData(false, SenderTrustLevel.Known)]
    [InlineData(true, SenderTrustLevel.Unknown)]
    public void RecognizedBy_ContactNothingTiedTheMessageTo_IsKnownOnlyWhereTheAccountWasNotStrict(bool isStrict, SenderTrustLevel expected)
    {
        // Arrange
        var stored = SenderTrust.FromStored(SenderTrustLevel.Unknown, SenderTrustSource.None, TrustedAuthorEvidence.None, Revision, null, isStrict);

        // Act
        var read = stored.RecognizedBy(
            new RecognizingContacts(OrganizationContact, null),
            Recognition(AuthorAuthenticationRoute.None, isStrict: isStrict));

        // Assert
        Assert.Equal(expected, read.Level);
    }

    /// <summary>A message stored before the author's route was recorded never becomes trusted through a contact, only known.</summary>
    [Fact]
    public void Of_AuthenticatedAuthorWithNoRecordedRoute_TiesNothing()
    {
        // Act
        var recognition = SenderAddressRecognition.Of(
            AuthorAuthenticationOutcome.Authenticated,
            AuthorAuthenticationRoute.None,
            DomainOf("partner.example"),
            DmarcPolicyReading.NotEstablished,
            EmailSignature.NotSigned,
            DomainOf("partner.example"),
            isStrict: false);

        // Assert
        Assert.Equal(TrustedAuthorEvidence.None, recognition.Evidence);
        Assert.True(recognition.KnowsByAddressAlone);
    }

    /// <summary>SPF alone authenticates whoever handed the message over, so it ties a contact's address to nothing.</summary>
    [Fact]
    public void Of_AuthorAlignedOnlyThroughSpf_TiesNothing()
    {
        // Act
        var recognition = Recognition(AuthorAuthenticationRoute.AlignedSpf);

        // Assert
        Assert.Equal(TrustedAuthorEvidence.None, recognition.Evidence);
    }

    /// <summary>A server's DMARC failure keeps the address from recognizing anybody, unless the domain asks only to be monitored.</summary>
    [Theory]
    [InlineData(DmarcPolicyReading.MonitoringOnly, true)]
    [InlineData(DmarcPolicyReading.Enforcing, false)]
    [InlineData(DmarcPolicyReading.NotEstablished, false)]
    public void Of_ServerDmarcFailure_KnowsTheAddressOnlyUnderAMonitoringOnlyPolicy(DmarcPolicyReading policy, bool expected)
    {
        // Act
        var recognition = SenderAddressRecognition.Of(
            AuthorAuthenticationOutcome.Failed,
            AuthorAuthenticationRoute.None,
            authenticatedAuthorDomain: null,
            policy,
            EmailSignature.NotSigned,
            DomainOf("partner.example"),
            isStrict: false);

        // Assert
        Assert.Equal(expected, recognition.KnowsByAddressAlone);
    }

    /// <summary>The author's own verified signature ties the address where nothing authenticated the domain.</summary>
    [Fact]
    public void Of_VerifiedSignatureNamingTheAuthor_TiesTheAddressBySignature()
    {
        // Arrange
        var signature = new EmailSignature
        {
            ContentCheck = SignatureCheckResult.Passed,
            AuthorityCheck = SignatureCheckResult.Passed,
            ValidityCheck = SignatureCheckResult.Passed,
            RevocationCheck = SignatureCheckResult.Passed,
            SignerCheck = SignatureCheckResult.Passed,
        };

        // Act
        var recognition = SenderAddressRecognition.Of(
            AuthorAuthenticationOutcome.NotEstablished,
            AuthorAuthenticationRoute.None,
            authenticatedAuthorDomain: null,
            DmarcPolicyReading.NotEstablished,
            signature,
            DomainOf("partner.example"),
            isStrict: true);

        // Assert
        Assert.Equal(TrustedAuthorEvidence.AuthorSignature, recognition.Evidence);
    }

    private static SenderAddressRecognition Recognition(AuthorAuthenticationRoute route, bool isStrict = false) =>
        SenderAddressRecognition.Of(
            route == AuthorAuthenticationRoute.None ? AuthorAuthenticationOutcome.NotEstablished : AuthorAuthenticationOutcome.Authenticated,
            route,
            DomainOf("partner.example"),
            DmarcPolicyReading.NotEstablished,
            EmailSignature.NotSigned,
            DomainOf("partner.example"),
            isStrict);

    private static SenderDomain DomainOf(string written)
    {
        Assert.True(SenderDomain.TryCreate(written, out var domain));

        return domain;
    }
}
