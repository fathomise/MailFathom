// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Emails.Signatures;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail;

/// <summary>Covers which authors configuration says an account recognizes as its owner.</summary>
public sealed class SenderTrustPolicyConfigurationTests
{
    /// <summary>Mail from one of the owner's accounts to another is theirs, and is recognized with nothing configured.</summary>
    [Fact]
    public void GetTrustPolicy_MailFromAnOwnAddressToAnotherOwnAccount_IsRecognizedAsOwnAddress()
    {
        // Arrange
        var options = OptionsFor(
            AccountAt("work", "user@work.example"),
            AccountAt("personal", "user@personal.example"));

        // Act
        var trust = options
            .Readers.SenderTrustPolicies.GetTrustPolicy(MailAccountId.Create("personal"))
            .Evaluate(WrittenBy("work.example"), EmailSignature.NotSigned, AddressOf("user@work.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Trusted, trust.Level);
        Assert.Equal(SenderTrustSource.OwnAccountAddress, trust.GrantedBy);
    }

    /// <summary>The address an account sends as is the owner's as well, even where it is not the account's user name.</summary>
    [Fact]
    public void GetTrustPolicy_MailFromTheAddressAnAccountSendsAs_IsRecognizedAsOwnAddress()
    {
        // Arrange
        var work = AccountAt("work", "login");
        work.Delivery = new MailAccountDeliveryOptions { Host = "smtp.example.test", FromAddress = "user@work.example" };
        var options = OptionsFor(work);

        // Act
        var trust = options
            .Readers.SenderTrustPolicies.GetTrustPolicy(MailAccountId.Create("work"))
            .Evaluate(WrittenBy("work.example"), EmailSignature.NotSigned, AddressOf("user@work.example"));

        // Assert
        Assert.Equal(SenderTrustSource.OwnAccountAddress, trust.GrantedBy);
    }

    /// <summary>The owner's domains are opt-in, so by default a colleague at the owner's domain is judged by the other sources alone.</summary>
    [Fact]
    public void GetTrustPolicy_NoAccountOptedIntoOwnDomains_LeavesAnotherAuthorAtThatDomainUnrecognized()
    {
        // Arrange
        var options = OptionsFor(
            AccountAt("work", "user@work.example"),
            AccountAt("personal", "user@personal.example"));

        // Act
        var trust = options
            .Readers.SenderTrustPolicies.GetTrustPolicy(MailAccountId.Create("personal"))
            .Evaluate(WrittenBy("work.example"), EmailSignature.NotSigned, AddressOf("colleague@work.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
    }

    /// <summary>An account that opts in recognizes every author at its owner's domains, and the owner's other accounts stay as they were.</summary>
    [Fact]
    public void GetTrustPolicy_OneAccountOptedIntoOwnDomains_RecognizesThereAndNowhereElse()
    {
        // Arrange
        var personal = AccountAt("personal", "user@personal.example");
        personal.TrustOwnAccountDomains = true;
        var options = OptionsFor(AccountAt("work", "user@work.example"), personal);

        // Act
        var onPersonal = options
            .Readers.SenderTrustPolicies.GetTrustPolicy(MailAccountId.Create("personal"))
            .Evaluate(WrittenBy("work.example"), EmailSignature.NotSigned, AddressOf("colleague@work.example"));
        var onWork = options
            .Readers.SenderTrustPolicies.GetTrustPolicy(MailAccountId.Create("work"))
            .Evaluate(WrittenBy("work.example"), EmailSignature.NotSigned, AddressOf("colleague@work.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Trusted, onPersonal.Level);
        Assert.Equal(SenderTrustSource.OwnAccountDomain, onPersonal.GrantedBy);
        Assert.Equal(SenderTrustLevel.Unknown, onWork.Level);
    }

    /// <summary>An IMAP user name that is a bare login names no mail domain, so an account that opted in still contributes none.</summary>
    [Fact]
    public void GetTrustPolicy_AccountWhoseUserNameIsALogin_ContributesNoOwnDomain()
    {
        // Arrange
        var account = AccountAt("work", "mailfathom");
        account.TrustOwnAccountDomains = true;
        var options = OptionsFor(account);

        // Act
        var trust = options
            .Readers.SenderTrustPolicies.GetTrustPolicy(MailAccountId.Create("work"))
            .Evaluate(WrittenBy("work.example"), EmailSignature.NotSigned, AddressOf("user@work.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
    }

    /// <summary>Every account is relaxed unless it sets <c>Strict</c>, and setting it on one account leaves the owner's others as they were.</summary>
    [Fact]
    public void GetTrustPolicy_OneAccountSetStrict_IsStrictThereAndNowhereElse()
    {
        // Arrange
        var work = AccountAt("work", "user@work.example");
        work.Strict = true;
        var options = OptionsFor(work, AccountAt("personal", "user@personal.example"));
        var unauthenticated = SenderAuthentication.NotEstablished();

        // Act
        var onWork = options
            .Readers.SenderTrustPolicies.GetTrustPolicy(MailAccountId.Create("work"))
            .Evaluate(unauthenticated, EmailSignature.NotSigned, AddressOf("user@personal.example"));
        var onPersonal = options
            .Readers.SenderTrustPolicies.GetTrustPolicy(MailAccountId.Create("personal"))
            .Evaluate(unauthenticated, EmailSignature.NotSigned, AddressOf("user@work.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, onWork.Level);
        Assert.Equal(SenderTrustLevel.Known, onPersonal.Level);
        Assert.Equal(SenderTrustSource.OwnAccountAddress, onPersonal.GrantedBy);
    }

    /// <summary>An extraction may run over an account a reload removed, and recognizing nobody is the answer.</summary>
    [Fact]
    public void GetTrustPolicy_AccountThisSnapshotNoLongerNames_RecognizesNobody()
    {
        // Act
        var policy = OptionsFor(AccountAt("work", "user@work.example"))
            .Readers.SenderTrustPolicies.GetTrustPolicy(MailAccountId.Create("removed"));

        // Assert
        Assert.Same(SenderTrustPolicy.RecognizingNobody, policy);
    }

    /// <summary>Two served users are two people, so one person's address and domain are not correspondence the other recognizes.</summary>
    [Fact]
    public void GetTrustPolicy_AnotherUsersAccountAddressAndDomain_AreNotRecognizedInsideThisUsersMailbox()
    {
        // Arrange
        var work = AccountAt("work", "user@work.example");
        work.TrustOwnAccountDomains = true;
        var options = new MailSynchronizationOptions().WithServedUsers(
        [
            User(SyntheticUser.Deployment, work),
            User(SyntheticUser.Another, AccountAt("theirs", "other@elsewhere.example")),
        ]);

        // Act
        var trust = options
            .Readers.SenderTrustPolicies.GetTrustPolicy(MailAccountId.Create("work"))
            .Evaluate(WrittenBy("elsewhere.example"), EmailSignature.NotSigned, AddressOf("other@elsewhere.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
    }

    /// <summary>Builds the verdict of a message whose displayed author the receiving server established.</summary>
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

    private static MailSynchronizationOptions OptionsFor(params MailSynchronizationAccountOptions[] accounts) =>
        new MailSynchronizationOptions().Serving(accounts);

    private static ServedUser User(UserId user, params MailSynchronizationAccountOptions[] accounts) =>
        new(user, "a user this deployment serves", accounts);

    private static MailSynchronizationAccountOptions AccountAt(string accountId, string userName) => new()
    {
        AccountId = accountId,
        DisplayName = $"Account {accountId}",
        Host = "imap.example.test",
        UserName = userName,
        Secrets = new MailAccountSecretOptions
        {
            Password = new ConfiguredSecret { SecretReference = $"systemd-credential:imap-{accountId}-password" },
        },
    };
}
