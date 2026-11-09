// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Categories;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration.Categories;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Categories;

/// <summary>Covers how each account's own record becomes what it offers.</summary>
public sealed class ConfiguredMailCategoryOfferReaderTests
{
    private const string AccountId = "primary";

    [Fact]
    public void OfferOf_AnAccountThatCategorizes_OffersItsDefaultsAndItsOwnCategories()
    {
        // Arrange
        var reader = ReaderOver(new MailAccountCategorizationOptions
        {
            Enabled = true,
            DisabledDefaults = ["finance"],
            OwnCategories = [new MailAccountOwnCategoryOptions { Name = "Contracts", Description = "Signed agreements." }],
        });

        // Act
        var offer = reader.OfferOf(MailAccountId.Create(AccountId));

        // Assert
        Assert.DoesNotContain("finance", offer.Names);
        Assert.Contains("travel", offer.Names);
        Assert.Contains("Contracts", offer.Names);
    }

    [Fact]
    public void OfferOf_AnAccountThatDoesNotCategorize_OffersNothing()
    {
        // Arrange
        var reader = ReaderOver(new MailAccountCategorizationOptions
        {
            OwnCategories = [new MailAccountOwnCategoryOptions { Name = "Contracts", Description = "Signed agreements." }],
        });

        // Act
        var offer = reader.OfferOf(MailAccountId.Create(AccountId));

        // Assert
        Assert.Same(MailCategoryOffer.None, offer);
    }

    [Fact]
    public void OfferOf_AnAccountThisScopeDoesNotServe_OffersNothing()
    {
        // Arrange
        var reader = ReaderOver(new MailAccountCategorizationOptions { Enabled = true });

        // Act
        var offer = reader.OfferOf(MailAccountId.Create("somebody-elses"));

        // Assert
        Assert.Same(MailCategoryOffer.None, offer);
    }

    private static ConfiguredMailCategoryOfferReader ReaderOver(MailAccountCategorizationOptions categorization) =>
        new(new MailSynchronizationOptions().WithServedUsers(
        [
            new ServedUser(
                SyntheticUser.Deployment,
                AccountId,
                [
                    new MailSynchronizationAccountOptions
                    {
                        AccountId = AccountId,
                        DisplayName = "The primary mailbox",
                        Host = "imap.example.test",
                        UserName = "mailfathom@example.test",
                        Secrets = new MailAccountSecretOptions
                        {
                            Password = new ConfiguredSecret { SecretReference = "systemd-credential:imap-primary-password" },
                        },
                        Categorization = categorization,
                    },
                ]),
        ]));
}
