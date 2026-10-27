// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using Xunit;

namespace MailFathom.Domain.UnitTests.Emails.Authentication;

/// <summary>Covers which authors one account recognizes, which half recognized them, and what the answer is stored with.</summary>
public sealed class SenderTrustPolicyTests
{
    private static readonly Guid Organization = Guid.Parse("0199f000-0000-7000-8000-000000000001");

    /// <summary>Mail one of this deployment's own accounts wrote is recognized wherever it was delivered.</summary>
    [Fact]
    public void Evaluate_AuthorAtAnotherConfiguredAccountsDomain_IsRecognizedAsOwn()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create(
            ownAccountAddresses: [],
            [DomainOf("work.example"), DomainOf("personal.example")],
            configuredTrustedSenders: [],
            storedTrustedSenders: []);

        // Act
        var trust = policy.Evaluate(WrittenBy("work.example"), AddressOf("user@work.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Trusted, trust.Level);
        Assert.Equal(SenderTrustSource.OwnAccountDomain, trust.GrantedBy);
    }

    /// <summary>Mail from the owner to themselves is recognized with nothing configured but the address they hold.</summary>
    [Fact]
    public void Evaluate_AuthenticatedAuthorWritingAsAnOwnAddress_IsRecognizedAsOwnAddress()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create([AddressOf("me@shared.example")], [], [], []);

        // Act
        var trust = policy.Evaluate(WrittenBy("shared.example"), AddressOf("me@shared.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Trusted, trust.Level);
        Assert.Equal(SenderTrustSource.OwnAccountAddress, trust.GrantedBy);
    }

    /// <summary>The owner's address in a <c>From</c> nothing authenticated is a claim anybody can write, so it recognizes nothing.</summary>
    [Theory]
    [InlineData(DmarcOutcome.Fail)]
    [InlineData(DmarcOutcome.NotReported)]
    public void Evaluate_OwnAddressDisplayedButNotAuthenticated_LeavesTheMessageUnknown(DmarcOutcome dmarc)
    {
        // Arrange
        var policy = SenderTrustPolicy.Create([AddressOf("me@shared.example")], [], [], []);

        // Act
        var notEstablished = policy.Evaluate(SenderAuthentication.NotEstablished(), AddressOf("me@shared.example"));
        var failed = policy.Evaluate(
            SenderAuthentication.Failed(DomainOf("shared.example"), dmarc),
            AddressOf("me@shared.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, notEstablished.Level);
        Assert.Equal(SenderTrustLevel.Unknown, failed.Level);
    }

    /// <summary>A stranger on the owner's shared provider authenticates as the same domain and is still not the owner.</summary>
    [Fact]
    public void Evaluate_AnotherAddressAtAnOwnAddressesDomain_LeavesTheMessageUnknown()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create([AddressOf("me@shared.example")], [], [], []);

        // Act
        var trust = policy.Evaluate(WrittenBy("shared.example"), AddressOf("stranger@shared.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
        Assert.Equal(SenderTrustSource.None, trust.GrantedBy);
    }

    /// <summary>Where the owner's domains count as well, the owner writing as themselves is named by the narrower claim.</summary>
    [Fact]
    public void Evaluate_OwnAddressWhoseDomainAlsoCounts_ReportsTheAddress()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create([AddressOf("me@work.example")], [DomainOf("work.example")], [], []);

        // Act
        var trust = policy.Evaluate(WrittenBy("work.example"), AddressOf("me@work.example"));

        // Assert
        Assert.Equal(SenderTrustSource.OwnAccountAddress, trust.GrantedBy);
    }

    /// <summary>Adding an address the owner holds changes what the policy recognizes, so it names another revision.</summary>
    [Fact]
    public void Revision_OwnAddressAdded_IsAnotherRevision()
    {
        // Act
        var without = SenderTrustPolicy.Create([], [], [], []).Revision;
        var with = SenderTrustPolicy.Create([AddressOf("me@shared.example")], [], [], []).Revision;

        // Assert
        Assert.NotEqual(without, with);
    }

    /// <summary>A deployment whose accounts share a provider with everybody says so by supplying no own domains.</summary>
    [Fact]
    public void Evaluate_OwnDomainsWithheld_LeavesTheSameAuthorUnknown()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create(
            ownAccountAddresses: [],
            ownAccountDomains: [],
            configuredTrustedSenders: [],
            storedTrustedSenders: []);

        // Act
        var trust = policy.Evaluate(WrittenBy("work.example"), AddressOf("user@work.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
        Assert.Equal(SenderTrustSource.None, trust.GrantedBy);
    }

    /// <summary>An own domain is the account's own name and never the names beneath it.</summary>
    [Fact]
    public void Evaluate_SubdomainOfAnOwnDomain_IsNotRecognizedAsOwn()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create([], [DomainOf("work.example")], [], []);

        // Act
        var trust = policy.Evaluate(WrittenBy("mail.work.example"), displayedSender: null);

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
    }

    /// <summary>An entry in either half recognizes, and the half that did is what the verdict reports.</summary>
    [Theory]
    [InlineData(true, false, SenderTrustSource.ConfiguredTrustedSender)]
    [InlineData(false, true, SenderTrustSource.StoredTrustedSender)]
    [InlineData(true, true, SenderTrustSource.ConfiguredTrustedSender)]
    public void Evaluate_EntryInEitherHalf_RecognizesAndNamesTheHalf(
        bool configured,
        bool stored,
        SenderTrustSource expected)
    {
        // Arrange
        var entry = DomainEntry("partner.example", includeSubdomains: false);
        var policy = SenderTrustPolicy.Create(
            ownAccountAddresses: [],
            ownAccountDomains: [],
            configured ? [entry] : [],
            stored ? [entry] : []);

        // Act
        var trust = policy.Evaluate(WrittenBy("partner.example"), displayedSender: null);

        // Assert
        Assert.Equal(SenderTrustLevel.Trusted, trust.Level);
        Assert.Equal(expected, trust.GrantedBy);
    }

    /// <summary>A displayed author nothing established is never held against the list, however the list names them.</summary>
    /// <remarks>
    /// This is the forgery the whole design is arranged against: anybody can write a trusted correspondent's address
    /// into <c>From</c>, so an entry naming that correspondent must recognize nothing until something other than the
    /// header says the message really came from them.
    /// </remarks>
    [Theory]
    [InlineData(DmarcOutcome.Fail)]
    [InlineData(DmarcOutcome.NotReported)]
    public void Evaluate_TrustedAuthorDisplayedButAuthenticationFailed_LeavesTheMessageUnknown(DmarcOutcome dmarc)
    {
        // Arrange
        var policy = SenderTrustPolicy.Create(
            ownAccountAddresses: [],
            ownAccountDomains: [],
            [DomainEntry("partner.example", includeSubdomains: false)],
            storedTrustedSenders: []);

        // Act
        var trust = policy.Evaluate(
            SenderAuthentication.Failed(DomainOf("partner.example"), dmarc),
            AddressOf("alice@partner.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
        Assert.Equal(SenderTrustSource.None, trust.GrantedBy);
    }

    /// <summary>A recognized signer cannot vouch for an author it is not, which is what an allow-listed relay would be.</summary>
    /// <remarks>
    /// The message really was signed by a domain the account named, and the domain it displays as its author is not
    /// that one. Anything reading the authenticated identity here would recognize every message the signer ever relays,
    /// whoever it says wrote them.
    /// </remarks>
    [Theory]
    [InlineData(DmarcOutcome.Fail)]
    [InlineData(DmarcOutcome.NotReported)]
    public void Evaluate_TrustedThirdPartySignatureOverAnotherAuthor_LeavesTheMessageUnknown(DmarcOutcome dmarc)
    {
        // Arrange
        var policy = SenderTrustPolicy.Create(
            ownAccountAddresses: [],
            ownAccountDomains: [],
            [DomainEntry("relay.example", includeSubdomains: false)],
            storedTrustedSenders: []);

        // Act
        var trust = policy.Evaluate(
            SenderAuthentication.Authenticated(
                [DomainOf("relay.example")],
                spfDomains: [],
                DomainOf("bank.example"),
                dmarc,
                DmarcRecordLookup.NotPublished),
            AddressOf("security@bank.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
        Assert.Equal(SenderTrustSource.None, trust.GrantedBy);
    }

    /// <summary>The receiving server's own DMARC result establishes the displayed author whatever signed the message.</summary>
    [Fact]
    public void Evaluate_TrustedAuthorEstablishedByDmarcAlone_RecognizesTheAuthor()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create(
            ownAccountAddresses: [],
            ownAccountDomains: [],
            [DomainEntry("partner.example", includeSubdomains: false)],
            storedTrustedSenders: []);

        // Act
        var trust = policy.Evaluate(
            SenderAuthentication.Authenticated(
                [DomainOf("provider.example")],
                spfDomains: [],
                DomainOf("partner.example"),
                DmarcOutcome.Pass,
                DmarcRecordLookup.NotPublished),
            AddressOf("alice@partner.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Trusted, trust.Level);
    }

    /// <summary>Without DMARC, a DKIM signature aligned with the displayed author establishes them all the same.</summary>
    [Fact]
    public void Evaluate_AlignedDkimWithoutDmarc_RecognizesTheAuthor()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create(
            ownAccountAddresses: [],
            ownAccountDomains: [],
            [DomainEntry("partner.example", includeSubdomains: false)],
            storedTrustedSenders: []);

        // Act
        var trust = policy.Evaluate(
            SenderAuthentication.Authenticated(
                [DomainOf("partner.example")],
                spfDomains: [],
                DomainOf("partner.example"),
                DmarcOutcome.NotReported,
                DmarcRecordLookup.NotPublished),
            AddressOf("alice@partner.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Trusted, trust.Level);
        Assert.Equal(SenderTrustSource.ConfiguredTrustedSender, trust.GrantedBy);
    }

    /// <summary>An author aligned only through SPF is authenticated, and still recognized by no list.</summary>
    [Fact]
    public void Evaluate_AuthorAlignedOnlyThroughSpf_LeavesTheMessageUnknown()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create(
            [AddressOf("alice@partner.example")],
            [DomainOf("partner.example")],
            [DomainEntry("partner.example", includeSubdomains: false)],
            storedTrustedSenders: []);
        var authentication = SenderAuthentication.Authenticated(
            [DomainOf("relay.example")],
            [DomainOf("partner.example")],
            DomainOf("partner.example"),
            DmarcOutcome.NotReported,
            DmarcRecordLookup.NotPublished);

        // Act
        var trust = policy.Evaluate(authentication, AddressOf("alice@partner.example"));

        // Assert
        Assert.Equal(AuthorAuthenticationRoute.AlignedSpf, authentication.AuthorAuthenticationRoute);
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
        Assert.Equal(SenderTrustSource.None, trust.GrantedBy);
    }

    /// <summary>A message whose author was authenticated and whom nobody named is the ordinary answer.</summary>
    [Fact]
    public void Evaluate_AuthenticatedAuthorNobodyNamed_LeavesTheMessageUnknown()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create(
            ownAccountAddresses: [],
            [DomainOf("work.example")],
            [DomainEntry("partner.example", includeSubdomains: false)],
            storedTrustedSenders: []);

        // Act
        var trust = policy.Evaluate(WrittenBy("stranger.example"), AddressOf("someone@stranger.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
        Assert.Equal(SenderTrustSource.None, trust.GrantedBy);
    }

    /// <summary>A message nothing was established about is judged against no list at all.</summary>
    [Fact]
    public void Evaluate_NothingEstablished_LeavesTheMessageUnknown()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create([], [DomainOf("work.example")], [], []);

        // Act
        var trust = policy.Evaluate(
            SenderAuthentication.NotEstablished(DomainOf("work.example")),
            AddressOf("user@work.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
        Assert.Equal(SenderTrustSource.None, trust.GrantedBy);
    }

    /// <summary>A message displaying no usable author has nobody to recognize, whatever else authenticated.</summary>
    [Fact]
    public void Evaluate_NoDisplayedAuthorDomain_LeavesTheMessageUnknown()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create(
            ownAccountAddresses: [],
            ownAccountDomains: [],
            [DomainEntry("partner.example", includeSubdomains: false)],
            storedTrustedSenders: []);

        // Act
        var trust = policy.Evaluate(
            SenderAuthentication.Authenticated(
                [DomainOf("partner.example")],
                spfDomains: [],
                fromDomain: null,
                DmarcOutcome.Pass,
                DmarcRecordLookup.NotPublished),
            displayedSender: null);

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
    }

    /// <summary>An entry written in one encoding of an internationalized name recognizes the other one a message carried.</summary>
    [Fact]
    public void Evaluate_InternationalizedEntryAndAsciiVerdict_RecognizesOneName()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create(
            ownAccountAddresses: [],
            ownAccountDomains: [],
            [DomainEntry("bücher.example", includeSubdomains: false)],
            storedTrustedSenders: []);

        // Act
        var trust = policy.Evaluate(WrittenBy("xn--bcher-kva.example"), displayedSender: null);

        // Assert
        Assert.Equal(SenderTrustLevel.Trusted, trust.Level);
    }

    /// <summary>Every verdict carries the list it was reached under, so a later change to that list is legible.</summary>
    [Fact]
    public void Evaluate_AnyMessage_CarriesTheRevisionOfThePolicyThatJudgedIt()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create([], [DomainOf("work.example")], [], []);

        // Act
        var recognized = policy.Evaluate(WrittenBy("work.example"), displayedSender: null);
        var unrecognized = policy.Evaluate(WrittenBy("stranger.example"), displayedSender: null);

        // Assert
        Assert.True(policy.Revision.NamesAPolicy);
        Assert.Equal(policy.Revision, recognized.PolicyRevision);
        Assert.Equal(policy.Revision, unrecognized.PolicyRevision);
    }

    /// <summary>Reordering a list is not a change to it, and adding to either half is.</summary>
    [Fact]
    public void Revision_ListsThatSayTheSameThing_AreOneRevision()
    {
        // Arrange
        var first = DomainEntry("a.example", includeSubdomains: false);
        var second = DomainEntry("b.example", includeSubdomains: true);

        // Act
        var written = SenderTrustPolicy.Create([], [], [first, second], []).Revision;
        var reordered = SenderTrustPolicy.Create([], [], [second, first], []).Revision;
        var moved = SenderTrustPolicy.Create([], [], [first], [second]).Revision;
        var extended = SenderTrustPolicy.Create([], [], [first, second], [DomainEntry("c.example", false)]).Revision;

        // Assert
        Assert.Equal(written, reordered);
        Assert.NotEqual(written, moved);
        Assert.NotEqual(written, extended);
    }

    /// <summary>A deployment that recognizes nobody names no policy, so its verdicts read as judged by nothing.</summary>
    [Fact]
    public void Revision_PolicyWithNothingToSay_NamesNoPolicy()
    {
        // Act
        var revision = SenderTrustPolicy.RecognizingNobody.Revision;

        // Assert
        Assert.False(revision.NamesAPolicy);
        Assert.Equal(SenderTrustPolicyRevision.None, revision);
    }

    /// <summary>An account this deployment no longer serves recognizes nobody rather than failing.</summary>
    [Fact]
    public void Evaluate_PolicyRecognizingNobody_LeavesEveryAuthorUnknown()
    {
        // Act
        var trust = SenderTrustPolicy.RecognizingNobody.Evaluate(
            WrittenBy("partner.example"),
            AddressOf("alice@partner.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
        Assert.False(trust.PolicyRevision.NamesAPolicy);
    }

    /// <summary>An organization's signature proves the address itself, so it trusts a colleague no domain check established.</summary>
    [Fact]
    public void Evaluate_MemberOfTheReceivingOrganizationWithNoAuthenticatedDomain_IsTrustedAsAMember()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create([], [], [], []);

        // Act
        var trust = policy.Evaluate(
            SenderAuthentication.NotEstablished(),
            AddressOf("anna@org.example"),
            new OrganizationSenderVerification(Organization, SenderIsMember: true));

        // Assert
        Assert.Equal(SenderTrustLevel.Trusted, trust.Level);
        Assert.Equal(SenderTrustSource.OrganizationMember, trust.GrantedBy);
        Assert.Equal(Organization, trust.RecognizingOrganizationId);
    }

    /// <summary>Membership ranks first, so a colleague on the organization's own domain is named as a member rather than by it.</summary>
    [Fact]
    public void Evaluate_MemberWhoseDomainIsAlsoAnOwnAccountDomain_ReportsTheOrganization()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create([], [DomainOf("org.example")], [], []);

        // Act
        var trust = policy.Evaluate(
            WrittenBy("org.example"),
            AddressOf("anna@org.example"),
            new OrganizationSenderVerification(Organization, SenderIsMember: true));

        // Assert
        Assert.Equal(SenderTrustSource.OrganizationMember, trust.GrantedBy);
    }

    /// <summary>A signature that proved nothing leaves the message judged by the other sources, exactly as without one.</summary>
    [Fact]
    public void Evaluate_OrganizationVerifiedNoMember_JudgesByTheOtherSources()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create([], [DomainOf("org.example")], [], []);

        // Act
        var trust = policy.Evaluate(
            WrittenBy("org.example"),
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
        var policy = SenderTrustPolicy.Create([], [DomainOf("org.example")], [], []);

        // Act
        var withoutOrganization = policy.Evaluate(WrittenBy("org.example"), displayedSender: null);
        var underOrganization = policy.Evaluate(
            WrittenBy("org.example"),
            displayedSender: null,
            new OrganizationSenderVerification(Organization, SenderIsMember: false));
        var underAnother = policy.Evaluate(
            WrittenBy("org.example"),
            displayedSender: null,
            new OrganizationSenderVerification(Guid.Parse("0199f000-0000-7000-8000-000000000002"), SenderIsMember: false));

        // Assert
        Assert.Equal(policy.Revision, withoutOrganization.PolicyRevision);
        Assert.NotEqual(policy.Revision, underOrganization.PolicyRevision);
        Assert.NotEqual(underOrganization.PolicyRevision, underAnother.PolicyRevision);
    }

    /// <summary>Builds the verdict of a message whose displayed author the receiving server established.</summary>
    private static SenderAuthentication WrittenBy(string domain) =>
        SenderAuthentication.Authenticated(
            [DomainOf(domain)],
            spfDomains: [],
            DomainOf(domain),
            DmarcOutcome.Pass,
            DmarcRecordLookup.NotPublished);

    private static TrustedSenderEntry DomainEntry(string domain, bool includeSubdomains)
    {
        Assert.True(TrustedSenderEntry.TryCreateForDomain(domain, includeSubdomains, out var entry));
        Assert.NotNull(entry);

        return entry;
    }

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
