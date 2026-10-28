// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the judge a test that is not about trust stores its messages through.</summary>
public sealed class SenderTrustJudgesTests
{
    /// <summary>Left to its defaults the judge recognizes nobody, so a stored message reads exactly as an unknown author's.</summary>
    [Fact]
    public async Task Over_Defaults_JudgesAnAuthenticatedAuthorUnknown()
    {
        // Arrange
        Assert.True(SenderDomain.TryCreate("partner.example", out var domain));
        Assert.True(EmailAddress.TryCreate(displayName: null, "alice@partner.example", out var address));
        var message = new ExtractedEmailMetadata(
            MailAccountId.Create("primary"),
            Subject: "Subject",
            SentAt: null,
            ReceivedAt: null,
            [new EmailParticipant(EmailAddressRole.From, address)],
            EmailThreadReferences.None,
            EmailAttachmentSummary.None,
            ExtractedEmailText.FromPlainTextBody("body", "body"),
            SenderAuthentication.Authenticated([domain], spfDomains: [], domain, DmarcOutcome.Pass, DmarcRecordLookup.NotPublished));

        // Act
        var judged = await SenderTrustJudges.Over().JudgeAsync(
            Substitute.For<IPersistenceSession>(),
            [message],
            TestContext.Current.CancellationToken);

        // Assert
        var verdict = Assert.Single(judged).SenderTrust;
        Assert.Equal(SenderTrustLevel.Unknown, verdict.Level);
        Assert.Equal(SenderTrustPolicyRevision.None, verdict.PolicyRevision);
    }
}
