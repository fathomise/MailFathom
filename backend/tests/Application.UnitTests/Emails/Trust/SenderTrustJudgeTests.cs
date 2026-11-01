// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Emails.Trust;
using MailFathom.Application.Mail;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Trust;

/// <summary>Covers the half of a trust verdict a message stores, reached for each message of a batch under its own account's policy.</summary>
public sealed class SenderTrustJudgeTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("0199f000-0000-7000-8000-0000000000a1");

    private static readonly MailAccountId OtherAccount = MailAccountId.Create("0199f000-0000-7000-8000-0000000000a2");

    /// <summary>Each message is judged under the policy of the account that received it, in the order the batch handed them over.</summary>
    [Fact]
    public void Judge_SeveralAccounts_JudgesEachMessageUnderItsOwnAccountsPolicy()
    {
        // Arrange
        var policies = Substitute.For<ISenderTrustPolicyReader>();
        policies.GetTrustPolicy(Account).Returns(SenderTrustPolicy.Create([AddressOf("me@work.example")], []));
        policies.GetTrustPolicy(OtherAccount).Returns(SenderTrustPolicy.RecognizingNobody);
        var judge = new SenderTrustJudge(policies);

        // Act
        var judged = judge.Judge([MessageFrom("me@work.example"), MessageFrom("me@work.example", OtherAccount)]);

        // Assert
        Assert.Equal(
            [SenderTrustSource.OwnAccountAddress, SenderTrustSource.None],
            judged.Select(static message => message.SenderTrust.GrantedBy));
        Assert.Equal([Account, OtherAccount], judged.Select(static message => message.AccountId));
    }

    /// <summary>The stored half names no contact, so nothing a contact write changes is ever fixed onto a message.</summary>
    [Fact]
    public void Judge_AuthorNoConfigurationNames_StoresAnUnknownVerdictNamingNoContact()
    {
        // Arrange
        var judge = new SenderTrustJudge(PoliciesFor(SenderTrustPolicy.RecognizingNobody));

        // Act
        var judged = Assert.Single(judge.Judge([MessageFrom("alice@partner.example")]));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, judged.SenderTrust.Level);
        Assert.Null(judged.SenderTrust.RecognizingContactId);
    }

    /// <summary>Whether the account was strict is stored with the verdict, because a contact found on a later read is held against it.</summary>
    [Fact]
    public void Judge_StrictAccount_StoresTheStrictness()
    {
        // Arrange
        var judge = new SenderTrustJudge(PoliciesFor(SenderTrustPolicy.Create([], [], isStrict: true)));

        // Act
        var judged = Assert.Single(judge.Judge([MessageFrom("alice@partner.example")]));

        // Assert
        Assert.True(judged.SenderTrust.IsStrict);
    }

    /// <summary>The owner's address is matched against the mailbox <c>From</c> displays, and never against whoever <c>Sender</c> names.</summary>
    [Fact]
    public void Judge_OwnAddressOnlyInSender_LeavesTheDisplayedAuthorUnknown()
    {
        // Arrange
        var judge = new SenderTrustJudge(PoliciesFor(SenderTrustPolicy.Create([AddressOf("me@partner.example")], [])));
        var message = MessageWith(
            WrittenBy("partner.example"),
            [
                new EmailParticipant(EmailAddressRole.Sender, AddressOf("me@partner.example")),
                new EmailParticipant(EmailAddressRole.From, AddressOf("mallory@partner.example")),
            ]);

        // Act
        var judged = Assert.Single(judge.Judge([message]));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, judged.SenderTrust.Level);
    }

    private static ISenderTrustPolicyReader PoliciesFor(SenderTrustPolicy policy)
    {
        var policies = Substitute.For<ISenderTrustPolicyReader>();

        policies.GetTrustPolicy(Arg.Any<MailAccountId>()).Returns(policy);

        return policies;
    }

    private static ExtractedEmailMetadata MessageFrom(string from, MailAccountId? account = null) =>
        MessageWith(
            WrittenBy(from[(from.IndexOf('@', StringComparison.Ordinal) + 1)..]),
            [new EmailParticipant(EmailAddressRole.From, AddressOf(from))],
            account);

    private static ExtractedEmailMetadata MessageWith(
        SenderAuthentication authentication,
        IReadOnlyList<EmailParticipant> participants,
        MailAccountId? account = null) =>
        new(
            account ?? Account,
            Subject: "Subject",
            SentAt: null,
            ReceivedAt: null,
            participants,
            EmailThreadReferences.None,
            EmailAttachmentSummary.None,
            ExtractedEmailText.FromPlainTextBody("body", "body"),
            authentication);

    private static SenderAuthentication WrittenBy(string domain)
    {
        Assert.True(SenderDomain.TryCreate(domain, out var author));

        return SenderAuthentication.Authenticated([author], spfDomains: [], author, DmarcOutcome.Pass, DmarcRecordLookup.NotPublished);
    }

    private static EmailAddress AddressOf(string written)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, written, out var address));

        return address;
    }
}
