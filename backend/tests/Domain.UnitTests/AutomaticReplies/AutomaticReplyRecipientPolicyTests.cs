// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.AutomaticReplies;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Domain.UnitTests.AutomaticReplies;

/// <summary>Covers which addresses an automatic reply is never sent to because nobody reads them.</summary>
public sealed class AutomaticReplyRecipientPolicyTests
{
    /// <summary>A no-reply, bounce, or list-administration mailbox is never answered, however its separators are written.</summary>
    [Theory]
    [InlineData("noreply@example.test")]
    [InlineData("No-Reply@example.test")]
    [InlineData("no_reply.billing@example.test")]
    [InlineData("do-not-reply@example.test")]
    [InlineData("MAILER-DAEMON@example.test")]
    [InlineData("postmaster@example.test")]
    [InlineData("owner-staff@lists.example.test")]
    [InlineData("staff-request@lists.example.test")]
    public void IsUnattended_AnAddressNobodyReads_IsUnattended(string address)
    {
        // Arrange
        Assert.True(EmailAddress.TryCreate(displayName: null, address, out var emailAddress));

        // Act
        var unattended = AutomaticReplyRecipientPolicy.IsUnattended(emailAddress);

        // Assert
        Assert.True(unattended);
    }

    /// <summary>A person's mailbox is answered, including one whose name merely contains one of those words.</summary>
    [Theory]
    [InlineData("anna@example.test")]
    [InlineData("replyto.anna@example.test")]
    [InlineData("request-desk@example.test")]
    public void IsUnattended_APersonsAddress_IsAttended(string address)
    {
        // Arrange
        Assert.True(EmailAddress.TryCreate(displayName: null, address, out var emailAddress));

        // Act
        var unattended = AutomaticReplyRecipientPolicy.IsUnattended(emailAddress);

        // Assert
        Assert.False(unattended);
    }
}
