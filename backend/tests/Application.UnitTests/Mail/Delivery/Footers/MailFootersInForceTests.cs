// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using MailFathom.Application.Contacts.Members;
using MailFathom.Application.Mail.Delivery.Footers;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Footers;

public sealed class MailFootersInForceTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly Guid Organization = new("0198f0aa-0000-7000-8000-0000000000aa");

    private static readonly byte[] RawMime = Encoding.ASCII.GetBytes("Subject: synthetic\r\n\r\nbody\r\n");

    [Fact]
    public async Task FindAsync_NeitherATemplateNorAnOwnFooter_FindsNone()
    {
        // Arrange
        var footers = MailFootings.Over(new InMemoryMailFooterStore().Assigning(SyntheticUser.Deployment, Account));

        // Act
        var footer = await footers.FindAsync(SyntheticUser.Deployment, Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(footer);
    }

    [Fact]
    public async Task FindAsync_AnOwnFooterOnly_FindsItAsTheUsersOwn()
    {
        // Arrange
        var store = await StoreWithOwnFooterAsync("<p>Anna</p>");
        var footers = MailFootings.Over(store);

        // Act
        var footer = await footers.FindAsync(SyntheticUser.Deployment, Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(footer);
        Assert.Equal(MailFooterSource.Own, footer.Source);
        Assert.Equal("<p>Anna</p>", footer.Html);
        Assert.Equal("<p>Anna</p>", footer.PlainText);
    }

    [Fact]
    public async Task FindAsync_ATemplateBesideAnOwnFooter_FillsTheTemplateFromTheMembersRecordInstead()
    {
        // Arrange
        var store = (await StoreWithOwnFooterAsync("<p>Anna</p>")).Holding(Organization, Account);
        await store.SaveTemplateAsync(
            Organization,
            new MailFooterContent("<p>{{displayName}}, {{jobTitle}}, {{phoneNumber}}</p>", []),
            TestContext.Current.CancellationToken);
        var members = Substitute.For<IMemberDirectory>();
        members.FindAsync(Organization, SyntheticUser.Deployment, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<MemberContact?>(Member("Anna Kowalska", "Head of R&D", "+48 600 000 000", "+48 22 000 00 00")));
        var footers = MailFootings.Over(store, members: members);

        // Act
        var footer = await footers.FindAsync(SyntheticUser.Deployment, Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(footer);
        Assert.Equal(MailFooterSource.Organization, footer.Source);
        Assert.Equal("<p>Anna Kowalska, Head of R&amp;D, +48 600 000 000</p>", footer.Html);
    }

    [Fact]
    public async Task FindAsync_ATemplateForSomebodyTheDirectoryDoesNotHold_FillsItWithNothing()
    {
        // Arrange
        var store = new InMemoryMailFooterStore().Holding(Organization, Account);
        await store.SaveTemplateAsync(
            Organization,
            new MailFooterContent("<p>[{{displayName}}]</p>", []),
            TestContext.Current.CancellationToken);
        var footers = MailFootings.Over(store);

        // Act
        var footer = await footers.FindAsync(SyntheticUser.Deployment, Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(footer);
        Assert.Equal("<p>[]</p>", footer.Html);
    }

    [Fact]
    public async Task AppendAsync_ASendTheUserAskedFor_AppendsTheirFooterOnce()
    {
        // Arrange
        var footers = MailFootings.Over(await StoreWithOwnFooterAsync("Anna"));

        // Act
        var footed = await footers.AppendAsync(Request(SyntheticUser.Deployment), RawMime, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Encoding.ASCII.GetString(RawMime) + "\r\n[footer:Anna]", Encoding.ASCII.GetString(footed.Span));
    }

    [Fact]
    public async Task AppendAsync_ASendNoUserAskedFor_LeavesTheMessageAsComposed()
    {
        // Arrange
        var footers = MailFootings.Over(await StoreWithOwnFooterAsync("Anna"));

        // Act
        var footed = await footers.AppendAsync(Request(author: null), RawMime, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(RawMime, footed.ToArray());
    }

    [Fact]
    public async Task AppendAsync_AFooterCarryingTheMessagePastTheLargestSent_LeavesTheMessageAsComposed()
    {
        // Arrange
        var composer = Substitute.For<IMailFooterComposer>();
        composer.Append(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<MailFooter>()).Returns(new byte[25_000_001]);
        var footers = MailFootings.Over(await StoreWithOwnFooterAsync("Anna"), composer);

        // Act
        var footed = await footers.AppendAsync(Request(SyntheticUser.Deployment), RawMime, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(RawMime, footed.ToArray());
    }

    private static async Task<InMemoryMailFooterStore> StoreWithOwnFooterAsync(string html)
    {
        var store = new InMemoryMailFooterStore().Assigning(SyntheticUser.Deployment, Account);
        await store.SaveOwnAsync(SyntheticUser.Deployment, Account, new MailFooterContent(html, []), TestContext.Current.CancellationToken);

        return store;
    }

    private static OutgoingEmailRequest Request(UserId? author)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, "anna@example.test", out var address));

        return OutgoingEmailRequest.Create(
            Account,
            author,
            OutgoingEmailRequester.Command("footer-test"),
            [OutgoingRecipient.Create(address, OutgoingRecipientRole.To)]);
    }

    private static MemberContact Member(string displayName, string jobTitle, params string[] phoneNumbers) =>
        new(
            SyntheticUser.Deployment,
            ContactDisplayName.Create(displayName),
            [],
            PreferredAddress: null,
            [.. phoneNumbers.Select(static number => ContactPhoneNumber.Create(number, ContactPhoneLabel.Work))],
            jobTitle,
            "Nordwind",
            Portrait: null,
            TimeZone: null,
            WorkingHours: null);
}
