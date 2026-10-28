// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails.Authentication;
using Xunit;

namespace MailFathom.Domain.UnitTests.Emails.Authentication;

/// <summary>Covers the shape of the verdict itself, and the revision that says which list produced one.</summary>
public sealed class SenderTrustTests
{
    private static readonly ContactId Contact = ContactId.Create(Guid.Parse("0199f000-0000-7000-8000-0000000000c1"));

    /// <summary>A reading no policy has spoken over claims nothing and says that no policy reached it.</summary>
    [Fact]
    public void NotEvaluated_BeforeAPolicyRuns_IsUnknownAndNamesNoPolicy()
    {
        // Act
        var trust = SenderTrust.NotEvaluated;

        // Assert
        Assert.False(trust.IsTrusted);
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
        Assert.Equal(SenderTrustSource.None, trust.GrantedBy);
        Assert.False(trust.PolicyRevision.NamesAPolicy);
    }

    /// <summary>A verdict recognizing the owner names an own source, so a contact or an organization cannot be reached through it.</summary>
    [Theory]
    [InlineData(SenderTrustSource.None)]
    [InlineData(SenderTrustSource.OrganizationMember)]
    [InlineData(SenderTrustSource.PersonalContact)]
    public void TrustedAsOwn_NamingAnotherSource_IsRefused(SenderTrustSource source)
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => SenderTrust.TrustedAsOwn(
            source,
            TrustedAuthorEvidence.Authentication,
            SenderTrustPolicyRevision.None));

        // Assert
        Assert.Equal("grantedBy", refusal.ParamName);
    }

    /// <summary>A verdict a contact reached names a contact source, so the owner's own sources cannot carry a contact.</summary>
    [Fact]
    public void TrustedByContact_NamingAnOwnSource_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => SenderTrust.TrustedByContact(
            SenderTrustSource.OwnAccountAddress,
            Contact,
            TrustedAuthorEvidence.Authentication,
            SenderTrustPolicyRevision.None));

        // Assert
        Assert.Equal("grantedBy", refusal.ParamName);
    }

    /// <summary>A verdict a contact reached says what tied the message to the contact's address, so it is not reached by nothing.</summary>
    [Fact]
    public void TrustedByContact_WithoutEvidence_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => SenderTrust.TrustedByContact(
            SenderTrustSource.PersonalContact,
            Contact,
            TrustedAuthorEvidence.None,
            SenderTrustPolicyRevision.None));

        // Assert
        Assert.Equal("authorEvidence", refusal.ParamName);
    }

    /// <summary>A verdict a contact reached keeps the contact and the evidence, which is what an operator audits it by.</summary>
    [Fact]
    public void TrustedByContact_WithEvidence_RecordsTheContactAndTheEvidence()
    {
        // Act
        var trust = SenderTrust.TrustedByContact(
            SenderTrustSource.OrganizationContact,
            Contact,
            TrustedAuthorEvidence.AuthorSignature,
            SenderTrustPolicyRevision.None);

        // Assert
        Assert.True(trust.IsTrusted);
        Assert.Equal(Contact, trust.RecognizingContactId);
        Assert.Equal(TrustedAuthorEvidence.AuthorSignature, trust.AuthorEvidence);
        Assert.Null(trust.RecognizingOrganizationId);
    }

    /// <summary>The same list always names itself the same way, including in another process.</summary>
    [Fact]
    public void Of_TheSameStatements_ProducesTheSameRevision()
    {
        // Act
        var first = SenderTrustPolicyRevision.Of(["domain:A.EXAMPLE", "domain:B.EXAMPLE"]);
        var second = SenderTrustPolicyRevision.Of(["domain:B.EXAMPLE", "domain:A.EXAMPLE"]);

        // Assert
        Assert.Equal(first, second);
        Assert.Equal(SenderTrustPolicyRevision.Length, first.Value.Length);
        Assert.Equal(first.Value, first.ToString());
    }

    /// <summary>A stored revision is opaque, so reading one back neither re-derives nor re-checks it.</summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("0123456789abcdef0123456789abcdef", true)]
    public void FromStoredValue_WhatAColumnHeld_NamesAPolicyOnlyWhenTheColumnDid(string? stored, bool expected)
    {
        // Act
        var revision = SenderTrustPolicyRevision.FromStoredValue(stored);

        // Assert
        Assert.Equal(expected, revision.NamesAPolicy);
    }
}
