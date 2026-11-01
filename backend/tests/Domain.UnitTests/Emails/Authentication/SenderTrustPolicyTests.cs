// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Emails.Signatures;
using Xunit;

namespace MailFathom.Domain.UnitTests.Emails.Authentication;

/// <summary>Covers which authors one account recognizes, in which order, on what evidence, and what the answer is stored with.</summary>
/// <remarks>
/// Most cases judge a message the way a deployment does end to end: the policy reaches the half a message stores, and
/// the contacts the books hold are combined with it as a read does. The cases asking what is stored call the policy alone.
/// </remarks>
public sealed class SenderTrustPolicyTests
{
    private static readonly Guid Organization = Guid.Parse("0199f000-0000-7000-8000-000000000001");

    private static readonly ContactId OrganizationContact = ContactId.Create(Guid.Parse("0199f000-0000-7000-8000-0000000000c1"));

    private static readonly ContactId PersonalContact = ContactId.Create(Guid.Parse("0199f000-0000-7000-8000-0000000000c2"));

    /// <summary>An address the owner holds is answered as theirs however its letters were cased, and any other address is not.</summary>
    /// <param name="written">The address asked about.</param>
    /// <param name="expected">Whether it is the owner's.</param>
    [Theory]
    [InlineData("Me@Shared.example", true)]
    [InlineData("me@shared.example", true)]
    [InlineData("someone@shared.example", false)]
    public void IsOwnAccountAddress_AnAddress_AnswersWhetherTheOwnerHoldsIt(string written, bool expected)
    {
        // Arrange
        var policy = SenderTrustPolicy.Create([AddressOf("me@shared.example")], [DomainOf("shared.example")]);

        // Act
        var isOwn = policy.IsOwnAccountAddress(AddressOf(written));

        // Assert
        Assert.Equal(expected, isOwn);
    }

    /// <summary>Mail one of this deployment's own accounts wrote is recognized wherever it was delivered.</summary>
    [Fact]
    public void Evaluate_AuthorAtAnotherConfiguredAccountsDomain_IsRecognizedAsOwn()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create([], [DomainOf("work.example"), DomainOf("personal.example")]);

        // Act
        var trust = Judge(policy, WrittenBy("work.example"), "user@work.example");

        // Assert
        Assert.Equal(SenderTrustLevel.Trusted, trust.Level);
        Assert.Equal(SenderTrustSource.OwnAccountDomain, trust.GrantedBy);
        Assert.Equal(TrustedAuthorEvidence.Authentication, trust.AuthorEvidence);
    }

    /// <summary>Mail from the owner to themselves is recognized with nothing configured but the address they hold.</summary>
    [Fact]
    public void Evaluate_AuthenticatedAuthorWritingAsAnOwnAddress_IsRecognizedAsOwnAddress()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create([AddressOf("me@shared.example")], []);

        // Act
        var trust = Judge(policy, WrittenBy("shared.example"), "me@shared.example");

        // Assert
        Assert.Equal(SenderTrustLevel.Trusted, trust.Level);
        Assert.Equal(SenderTrustSource.OwnAccountAddress, trust.GrantedBy);
    }

    /// <summary>An address is one mailbox however its letters were cased, so a differently cased <c>From</c> is still the owner.</summary>
    [Fact]
    public void Evaluate_OwnAddressDisplayedInAnotherCase_IsRecognizedAsOwnAddress()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create([AddressOf("Me@Shared.example")], []);

        // Act
        var trust = Judge(policy, WrittenBy("shared.example"), "ME@SHARED.EXAMPLE");

        // Assert
        Assert.Equal(SenderTrustSource.OwnAccountAddress, trust.GrantedBy);
    }

    /// <summary>On a strict account, the owner's address in a <c>From</c> nothing authenticated is a claim anybody can write, so it recognizes nothing.</summary>
    [Theory]
    [InlineData(DmarcOutcome.Fail)]
    [InlineData(DmarcOutcome.NotReported)]
    public void Evaluate_OwnAddressDisplayedButNotAuthenticatedOnAStrictAccount_LeavesTheMessageUnknown(DmarcOutcome dmarc)
    {
        // Arrange
        var policy = SenderTrustPolicy.Create([AddressOf("me@shared.example")], [], isStrict: true);

        // Act
        var notEstablished = Judge(policy, SenderAuthentication.NotEstablished(), "me@shared.example");
        var failed = Judge(policy, SenderAuthentication.Failed(DomainOf("shared.example"), dmarc), "me@shared.example");

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, notEstablished.Level);
        Assert.Equal(SenderTrustLevel.Unknown, failed.Level);
    }

    /// <summary>A stranger on the owner's shared provider authenticates as the same domain and is still not the owner.</summary>
    [Fact]
    public void Evaluate_AnotherAddressAtAnOwnAddressesDomain_LeavesTheMessageUnknown()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create([AddressOf("me@shared.example")], []);

        // Act
        var trust = Judge(policy, WrittenBy("shared.example"), "stranger@shared.example");

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
        Assert.Equal(SenderTrustSource.None, trust.GrantedBy);
    }

    /// <summary>An own domain is the account's own name and never the names beneath it.</summary>
    [Fact]
    public void Evaluate_SubdomainOfAnOwnDomain_IsNotRecognizedAsOwn()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create([], [DomainOf("work.example")]);

        // Act
        var trust = Judge(policy, WrittenBy("mail.work.example"), "user@mail.work.example");

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
    }

    /// <summary>A domain written in one encoding of an internationalized name recognizes the other one a message carried.</summary>
    [Fact]
    public void Evaluate_InternationalizedOwnDomainAndAsciiVerdict_RecognizesOneName()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create([], [DomainOf("bücher.example")]);

        // Act
        var trust = Judge(policy, WrittenBy("xn--bcher-kva.example"), "anna@xn--bcher-kva.example");

        // Assert
        Assert.Equal(SenderTrustSource.OwnAccountDomain, trust.GrantedBy);
    }

    /// <summary>Each source is asked only where every source above it recognized nobody, so the highest that did is reported.</summary>
    [Theory]
    [InlineData(true, true, true, true, SenderTrustSource.OrganizationContact)]
    [InlineData(false, true, true, true, SenderTrustSource.OwnAccountAddress)]
    [InlineData(false, false, true, true, SenderTrustSource.OwnAccountDomain)]
    [InlineData(false, false, false, true, SenderTrustSource.PersonalContact)]
    [InlineData(false, false, false, false, SenderTrustSource.None)]
    public void Evaluate_SeveralSourcesRecognizeTheAuthor_ReportsTheHighest(
        bool organizationContact,
        bool ownAddress,
        bool ownDomain,
        bool personalContact,
        SenderTrustSource expected)
    {
        // Arrange
        var policy = SenderTrustPolicy.Create(
            ownAddress ? [AddressOf("me@work.example")] : [],
            ownDomain ? [DomainOf("work.example")] : []);
        var contacts = new RecognizingContacts(
            organizationContact ? OrganizationContact : null,
            personalContact ? PersonalContact : null);

        // Act
        var trust = Judge(policy, WrittenBy("work.example"), "me@work.example", contacts);

        // Assert
        Assert.Equal(expected, trust.GrantedBy);
    }

    /// <summary>A contact's verdict names the contact found when the message was read.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Evaluate_AuthorAContactHolds_RecordsTheContact(bool inOrganizationBook)
    {
        // Arrange
        var contacts = inOrganizationBook
            ? new RecognizingContacts(OrganizationContact, null)
            : new RecognizingContacts(null, PersonalContact);

        // Act
        var trust = Judge(SenderTrustPolicy.RecognizingNobody, WrittenBy("partner.example"), "alice@partner.example", contacts);

        // Assert
        Assert.Equal(SenderTrustLevel.Trusted, trust.Level);
        Assert.Equal(inOrganizationBook ? OrganizationContact : PersonalContact, trust.RecognizingContactId);
        Assert.Equal(TrustedAuthorEvidence.Authentication, trust.AuthorEvidence);
    }

    /// <summary>The receiving server's own DMARC result establishes the displayed author whatever signed the message.</summary>
    [Fact]
    public void Evaluate_ContactEstablishedByDmarcAlone_RecognizesTheAuthor()
    {
        // Arrange
        var authentication = SenderAuthentication.Authenticated(
            [DomainOf("provider.example")],
            spfDomains: [],
            DomainOf("partner.example"),
            DmarcOutcome.Pass,
            DmarcRecordLookup.NotPublished);

        // Act
        var trust = Judge(SenderTrustPolicy.RecognizingNobody, authentication, "alice@partner.example", ByPersonalContact);

        // Assert
        Assert.Equal(SenderTrustSource.PersonalContact, trust.GrantedBy);
    }

    /// <summary>Without DMARC, a DKIM signature aligned with the displayed author establishes them all the same.</summary>
    [Fact]
    public void Evaluate_ContactEstablishedByAlignedDkimWithoutDmarc_RecognizesTheAuthor()
    {
        // Arrange
        var authentication = SenderAuthentication.Authenticated(
            [DomainOf("partner.example")],
            spfDomains: [],
            DomainOf("partner.example"),
            DmarcOutcome.NotReported,
            DmarcRecordLookup.NotPublished);

        // Act
        var trust = Judge(SenderTrustPolicy.RecognizingNobody, authentication, "alice@partner.example", ByPersonalContact);

        // Assert
        Assert.Equal(AuthorAuthenticationRoute.AlignedDkim, authentication.AuthorAuthenticationRoute);
        Assert.Equal(SenderTrustSource.PersonalContact, trust.GrantedBy);
    }

    /// <summary>
    /// SPF proves only which server handed the message over, so on a strict account an author aligned through it alone is
    /// recognized by no source, the owner's own included.
    /// </summary>
    [Fact]
    public void Evaluate_AuthorAlignedOnlyThroughSpfOnAStrictAccount_LeavesTheMessageUnknown()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create([AddressOf("alice@partner.example")], [DomainOf("partner.example")], isStrict: true);
        var authentication = SenderAuthentication.Authenticated(
            [DomainOf("relay.example")],
            [DomainOf("partner.example")],
            DomainOf("partner.example"),
            DmarcOutcome.NotReported,
            DmarcRecordLookup.NotPublished);

        // Act
        var trust = Judge(policy, authentication, "alice@partner.example", new RecognizingContacts(OrganizationContact, PersonalContact));

        // Assert
        Assert.Equal(AuthorAuthenticationRoute.AlignedSpf, authentication.AuthorAuthenticationRoute);
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
        Assert.Equal(SenderTrustSource.None, trust.GrantedBy);
    }

    /// <summary>A signature by a domain other than the author's ties the message to nobody a contact holds, so a strict account recognizes nobody.</summary>
    [Theory]
    [InlineData(DmarcOutcome.Fail)]
    [InlineData(DmarcOutcome.NotReported)]
    public void Evaluate_ThirdPartySignatureOverAContactsAddressOnAStrictAccount_LeavesTheMessageUnknown(DmarcOutcome dmarc)
    {
        // Arrange
        var authentication = SenderAuthentication.Authenticated(
            [DomainOf("relay.example")],
            spfDomains: [],
            DomainOf("bank.example"),
            dmarc,
            DmarcRecordLookup.NotPublished);

        // Act
        var trust = Judge(StrictRecognizingNobody, authentication, "security@bank.example", ByPersonalContact);

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
    }

    /// <summary>
    /// A verified signature naming the author ties the message to the address even where the receiving server reported
    /// DMARC <c>fail</c>, because a forward that broke the domain's checks leaves the author's own signature intact.
    /// </summary>
    [Fact]
    public void Evaluate_VerifiedAuthorSignatureAfterDmarcFail_RecognizesTheContactOnTheSignature()
    {
        // Arrange
        var authentication = SenderAuthentication.Failed(DomainOf("partner.example"), DmarcOutcome.Fail);

        // Act
        var trust = Judge(
            SenderTrustPolicy.RecognizingNobody,
            authentication,
            "alice@partner.example",
            ByPersonalContact,
            VerifiedSignature(signerMatchesAuthor: true));

        // Assert
        Assert.Equal(SenderTrustSource.PersonalContact, trust.GrantedBy);
        Assert.Equal(TrustedAuthorEvidence.AuthorSignature, trust.AuthorEvidence);
    }

    /// <summary>Where authentication already tied the message, the verdict records that rather than the signature beside it.</summary>
    [Fact]
    public void Evaluate_AuthenticatedAndSigned_RecordsAuthenticationAsTheEvidence()
    {
        // Act
        var trust = Judge(
            SenderTrustPolicy.RecognizingNobody,
            WrittenBy("partner.example"),
            "alice@partner.example",
            ByPersonalContact,
            VerifiedSignature(signerMatchesAuthor: true));

        // Assert
        Assert.Equal(TrustedAuthorEvidence.Authentication, trust.AuthorEvidence);
    }

    /// <summary>A verified signature by somebody other than the author ties the message to nobody, so a strict account recognizes nobody.</summary>
    [Fact]
    public void Evaluate_VerifiedSignatureOfAnotherSignerOnAStrictAccount_LeavesTheMessageUnknown()
    {
        // Act
        var trust = Judge(
            StrictRecognizingNobody,
            SenderAuthentication.NotEstablished(),
            "alice@partner.example",
            ByPersonalContact,
            VerifiedSignature(signerMatchesAuthor: false));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
    }

    /// <summary>A message whose <c>From</c> named no usable mailbox has no address any source could hold.</summary>
    [Fact]
    public void Evaluate_NoDisplayedSender_LeavesTheMessageUnknown()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create([], [DomainOf("work.example")]);

        // Act
        var trust = policy.Evaluate(WrittenBy("work.example"), EmailSignature.NotSigned, displayedSender: null);

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
    }

    /// <summary>
    /// On a relaxed account, a message nothing tied to its address is known by whichever source holds that address, in
    /// the same order trust asks them, and names the contact where the source is one.
    /// </summary>
    [Theory]
    [InlineData(true, true, true, true, SenderTrustSource.OrganizationContact)]
    [InlineData(false, true, true, true, SenderTrustSource.OwnAccountAddress)]
    [InlineData(false, false, true, true, SenderTrustSource.OwnAccountDomain)]
    [InlineData(false, false, false, true, SenderTrustSource.PersonalContact)]
    public void Evaluate_AddressASourceHoldsButNothingTiedTheMessageOnARelaxedAccount_IsKnownByTheHighest(
        bool organizationContact,
        bool ownAddress,
        bool ownDomain,
        bool personalContact,
        SenderTrustSource expected)
    {
        // Arrange
        var policy = SenderTrustPolicy.Create(
            ownAddress ? [AddressOf("me@work.example")] : [],
            ownDomain ? [DomainOf("work.example")] : []);
        var contacts = new RecognizingContacts(
            organizationContact ? OrganizationContact : null,
            personalContact ? PersonalContact : null);
        ContactId? expectedContact = expected switch
        {
            SenderTrustSource.OrganizationContact => OrganizationContact,
            SenderTrustSource.PersonalContact => PersonalContact,
            _ => null,
        };

        // Act
        var trust = Judge(policy, SenderAuthentication.NotEstablished(DomainOf("work.example")), "me@work.example", contacts);

        // Assert
        Assert.Equal(SenderTrustLevel.Known, trust.Level);
        Assert.Equal(expected, trust.GrantedBy);
        Assert.Equal(TrustedAuthorEvidence.None, trust.AuthorEvidence);
        Assert.Equal(expectedContact, trust.RecognizingContactId);
        Assert.False(trust.IsTrusted);
    }

    /// <summary>An author aligned only through SPF is not tied to the address, so a relaxed account knows it by the address alone.</summary>
    [Fact]
    public void Evaluate_ContactAlignedOnlyThroughSpfOnARelaxedAccount_IsKnown()
    {
        // Arrange
        var authentication = SenderAuthentication.Authenticated(
            [DomainOf("relay.example")],
            [DomainOf("partner.example")],
            DomainOf("partner.example"),
            DmarcOutcome.NotReported,
            DmarcRecordLookup.NotPublished);

        // Act
        var trust = Judge(SenderTrustPolicy.RecognizingNobody, authentication, "alice@partner.example", ByPersonalContact);

        // Assert
        Assert.Equal(SenderTrustLevel.Known, trust.Level);
        Assert.Equal(PersonalContact, trust.RecognizingContactId);
    }

    /// <summary>The address is one mailbox however its letters were cased, so a differently cased <c>From</c> is still known as the owner's.</summary>
    [Fact]
    public void Evaluate_OwnAddressInAnotherCaseNotAuthenticatedOnARelaxedAccount_IsKnownAsOwnAddress()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create([AddressOf("Me@Shared.example")], []);

        // Act
        var trust = Judge(policy, SenderAuthentication.NotEstablished(DomainOf("shared.example")), "ME@SHARED.EXAMPLE");

        // Assert
        Assert.Equal(SenderTrustLevel.Known, trust.Level);
        Assert.Equal(SenderTrustSource.OwnAccountAddress, trust.GrantedBy);
    }

    /// <summary>A message that authenticates is trusted under the same source whether the account is relaxed or strict.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Evaluate_AuthenticatedContact_IsTrustedOnEitherKindOfAccount(bool isStrict)
    {
        // Arrange
        var policy = SenderTrustPolicy.Create([], [], isStrict);

        // Act
        var trust = Judge(policy, WrittenBy("partner.example"), "alice@partner.example", ByPersonalContact);

        // Assert
        Assert.Equal(SenderTrustLevel.Trusted, trust.Level);
        Assert.Equal(SenderTrustSource.PersonalContact, trust.GrantedBy);
    }

    /// <summary>A strict account never knows a sender by address alone, whichever source holds it.</summary>
    [Fact]
    public void Evaluate_AddressEverySourceHoldsButNotAuthenticatedOnAStrictAccount_LeavesTheMessageUnknown()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create([AddressOf("me@work.example")], [DomainOf("work.example")], isStrict: true);

        // Act
        var trust = Judge(
            policy,
            SenderAuthentication.NotEstablished(DomainOf("work.example")),
            "me@work.example",
            new RecognizingContacts(OrganizationContact, PersonalContact));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
        Assert.Equal(SenderTrustSource.None, trust.GrantedBy);
        Assert.Null(trust.RecognizingContactId);
    }

    /// <summary>
    /// A server's DMARC <c>fail</c> stays a failure on a relaxed account unless the domain asks only to be monitored, and
    /// stays one on a strict account whatever the domain asks.
    /// </summary>
    [Theory]
    [InlineData(false, DmarcPolicyReading.MonitoringOnly, SenderTrustLevel.Known)]
    [InlineData(false, DmarcPolicyReading.Enforcing, SenderTrustLevel.Unknown)]
    [InlineData(false, DmarcPolicyReading.NotEstablished, SenderTrustLevel.Unknown)]
    [InlineData(true, DmarcPolicyReading.MonitoringOnly, SenderTrustLevel.Unknown)]
    public void Evaluate_ContactAfterAServerDmarcFail_IsKnownOnlyUnderAMonitoringOnlyPolicyOnARelaxedAccount(
        bool isStrict,
        DmarcPolicyReading policyRead,
        SenderTrustLevel expected)
    {
        // Arrange
        var authentication = SenderAuthentication.Failed(
            DomainOf("partner.example"),
            DmarcOutcome.Fail,
            LookupReading(policyRead));

        // Act
        var trust = Judge(SenderTrustPolicy.Create([], [], isStrict), authentication, "alice@partner.example", ByPersonalContact);

        // Assert
        Assert.Equal(policyRead, authentication.DmarcPolicy);
        Assert.Equal(AuthorAuthenticationOutcome.Failed, authentication.AuthorAuthentication);
        Assert.Equal(expected, trust.Level);
    }

    /// <summary>A sender no source holds is unknown on a relaxed account too, however little authenticated.</summary>
    [Fact]
    public void Evaluate_AddressNoSourceHoldsOnARelaxedAccount_LeavesTheMessageUnknown()
    {
        // Act
        var trust = Judge(SenderTrustPolicy.RecognizingNobody, SenderAuthentication.NotEstablished(), "stranger@partner.example");

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
    }

    /// <summary>Turning an account strict, or back, changes what it recognizes, so it names another revision.</summary>
    [Fact]
    public void Revision_StrictAccount_IsAnotherRevisionThanARelaxedOne()
    {
        // Act
        var relaxed = SenderTrustPolicy.Create([AddressOf("me@shared.example")], []).Revision;
        var strict = SenderTrustPolicy.Create([AddressOf("me@shared.example")], [], isStrict: true).Revision;

        // Assert
        Assert.NotEqual(relaxed, strict);
    }

    /// <summary>Adding an address the owner holds changes what the policy recognizes, so it names another revision.</summary>
    [Fact]
    public void Revision_OwnAddressAdded_IsAnotherRevision()
    {
        // Act
        var without = SenderTrustPolicy.Create([], []).Revision;
        var with = SenderTrustPolicy.Create([AddressOf("me@shared.example")], []).Revision;

        // Assert
        Assert.NotEqual(without, with);
    }

    /// <summary>Reordering what the configuration states is not a change to it, and adding to it is.</summary>
    [Fact]
    public void Revision_StatementsThatSayTheSameThing_AreOneRevision()
    {
        // Act
        var written = SenderTrustPolicy.Create([AddressOf("a@one.example"), AddressOf("b@one.example")], [DomainOf("one.example")]).Revision;
        var reordered = SenderTrustPolicy.Create([AddressOf("b@one.example"), AddressOf("a@one.example")], [DomainOf("one.example")]).Revision;
        var extended = SenderTrustPolicy.Create([AddressOf("a@one.example"), AddressOf("b@one.example")], [DomainOf("one.example"), DomainOf("two.example")]).Revision;

        // Assert
        Assert.Equal(written, reordered);
        Assert.NotEqual(written, extended);
    }

    /// <summary>A deployment that states nothing names no policy, so its verdicts read as judged by nothing it configured.</summary>
    [Fact]
    public void Revision_PolicyWithNothingToSay_NamesNoPolicy()
    {
        // Act
        var revision = SenderTrustPolicy.RecognizingNobody.Revision;

        // Assert
        Assert.False(revision.NamesAPolicy);
        Assert.Equal(SenderTrustPolicyRevision.None, revision);
    }

    /// <summary>Every verdict carries the configuration it was reached under, so a later change to it is legible.</summary>
    [Fact]
    public void Evaluate_AnyMessage_CarriesTheRevisionOfThePolicyThatJudgedIt()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create([], [DomainOf("work.example")]);

        // Act
        var recognized = Judge(policy, WrittenBy("work.example"), "user@work.example");
        var unrecognized = Judge(policy, WrittenBy("stranger.example"), "user@stranger.example");

        // Assert
        Assert.True(policy.Revision.NamesAPolicy);
        Assert.Equal(policy.Revision, recognized.PolicyRevision);
        Assert.Equal(policy.Revision, unrecognized.PolicyRevision);
    }

    /// <summary>An organization's signature proves the address itself, so it trusts a colleague no domain check established.</summary>
    [Fact]
    public void Evaluate_MemberOfTheReceivingOrganizationWithNoAuthenticatedDomain_IsTrustedAsAMember()
    {
        // Act
        var trust = Judge(
            SenderTrustPolicy.RecognizingNobody,
            SenderAuthentication.NotEstablished(),
            "anna@org.example",
            new RecognizingContacts(OrganizationContact, null),
            organizationVerification: new OrganizationSenderVerification(Organization, SenderIsMember: true));

        // Assert
        Assert.Equal(SenderTrustLevel.Trusted, trust.Level);
        Assert.Equal(SenderTrustSource.OrganizationMember, trust.GrantedBy);
        Assert.Equal(Organization, trust.RecognizingOrganizationId);
        Assert.Null(trust.RecognizingContactId);
    }

    /// <summary>A signature that proved nothing leaves the message judged by the other sources, exactly as without one.</summary>
    [Fact]
    public void Evaluate_OrganizationVerifiedNoMember_JudgesByTheOtherSources()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create([], [DomainOf("org.example")]);

        // Act
        var trust = policy.Evaluate(
            WrittenBy("org.example"),
            EmailSignature.NotSigned,
            AddressOf("anna@org.example"),
            new OrganizationSenderVerification(Organization, SenderIsMember: false));

        // Assert
        Assert.Equal(SenderTrustSource.OwnAccountDomain, trust.GrantedBy);
        Assert.Null(trust.RecognizingOrganizationId);
    }

    /// <summary>A verdict reached under an organization's keys says whose they were, and one reached under none says nothing new.</summary>
    [Fact]
    public void Evaluate_ReceivingAccountInAnOrganization_NamesTheOrganizationInTheRevision()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create([], [DomainOf("org.example")]);
        var sender = AddressOf("anna@org.example");

        // Act
        var withoutOrganization = policy.Evaluate(WrittenBy("org.example"), EmailSignature.NotSigned, sender);
        var underOrganization = policy.Evaluate(
            WrittenBy("org.example"),
            EmailSignature.NotSigned,
            sender,
            new OrganizationSenderVerification(Organization, SenderIsMember: false));
        var underAnother = policy.Evaluate(
            WrittenBy("org.example"),
            EmailSignature.NotSigned,
            sender,
            new OrganizationSenderVerification(Guid.Parse("0199f000-0000-7000-8000-000000000002"), SenderIsMember: false));

        // Assert
        Assert.Equal(policy.Revision, withoutOrganization.PolicyRevision);
        Assert.NotEqual(policy.Revision, underOrganization.PolicyRevision);
        Assert.NotEqual(underOrganization.PolicyRevision, underAnother.PolicyRevision);
    }

    /// <summary>What a message stores never names a contact, however many hold its author's address.</summary>
    [Fact]
    public void Evaluate_AuthorOnlyAContactCouldRecognize_StoresAnUnknownVerdictNamingNoContact()
    {
        // Act
        var stored = SenderTrustPolicy.RecognizingNobody.Evaluate(
            WrittenBy("partner.example"),
            EmailSignature.NotSigned,
            AddressOf("alice@partner.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, stored.Level);
        Assert.Equal(SenderTrustSource.None, stored.GrantedBy);
        Assert.Null(stored.RecognizingContactId);
    }

    /// <summary>The stored half says whether the account was strict, which is what a contact found on a later read is held against.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Evaluate_AnyAccount_StoresWhetherItWasStrict(bool isStrict)
    {
        // Act
        var stored = SenderTrustPolicy.Create([], [], isStrict).Evaluate(
            SenderAuthentication.NotEstablished(),
            EmailSignature.NotSigned,
            AddressOf("alice@partner.example"));

        // Assert
        Assert.Equal(isStrict, stored.IsStrict);
    }

    private static RecognizingContacts ByPersonalContact => new(null, PersonalContact);

    private static SenderTrustPolicy StrictRecognizingNobody => SenderTrustPolicy.Create([], [], isStrict: true);

    /// <summary>Builds what reading a record found at the displayed domain answers with, for each policy it can read as.</summary>
    private static DmarcRecordLookup LookupReading(DmarcPolicyReading policy) =>
        policy == DmarcPolicyReading.NotEstablished
            ? DmarcRecordLookup.NotReadable
            : DmarcRecordLookup.Found(new DmarcRecord(DkimAlignmentMode.Relaxed) { PolicyAtDomain = policy });

    /// <summary>Stores a message's verdict, then reads it with the contacts the books hold, as a deployment does.</summary>
    private static SenderTrust Judge(
        SenderTrustPolicy policy,
        SenderAuthentication authentication,
        string sender,
        RecognizingContacts? contacts = null,
        EmailSignature? signature = null,
        OrganizationSenderVerification? organizationVerification = null)
    {
        var address = AddressOf(sender);
        var stored = policy.Evaluate(authentication, signature ?? EmailSignature.NotSigned, address, organizationVerification);

        Assert.True(SenderDomain.TryCreateFromMailbox(address.Address, out var senderDomain));

        return stored.RecognizedBy(
            contacts ?? RecognizingContacts.None,
            SenderAddressRecognition.Of(authentication, signature ?? EmailSignature.NotSigned, senderDomain, stored.IsStrict));
    }

    /// <summary>Builds the verdict of a message whose displayed author the receiving server established.</summary>
    private static SenderAuthentication WrittenBy(string domain) =>
        SenderAuthentication.Authenticated(
            [DomainOf(domain)],
            spfDomains: [],
            DomainOf(domain),
            DmarcOutcome.Pass,
            DmarcRecordLookup.NotPublished);

    private static EmailSignature VerifiedSignature(bool signerMatchesAuthor) =>
        new()
        {
            ContentCheck = SignatureCheckResult.Passed,
            AuthorityCheck = SignatureCheckResult.Passed,
            ValidityCheck = SignatureCheckResult.Passed,
            RevocationCheck = SignatureCheckResult.Passed,
            SignerCheck = signerMatchesAuthor ? SignatureCheckResult.Passed : SignatureCheckResult.Failed,
        };

    private static SenderDomain DomainOf(string written)
    {
        Assert.True(SenderDomain.TryCreate(written, out var domain));

        return domain;
    }

    private static EmailAddress AddressOf(string written)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, written, out var address));

        return address;
    }
}
