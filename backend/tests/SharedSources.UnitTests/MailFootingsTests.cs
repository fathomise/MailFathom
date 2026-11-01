// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using MailFathom.Application.Mail.Delivery.Footers;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the footer decision the outbox is constructed with in every suite that builds one, and the store behind it.</summary>
public sealed class MailFootingsTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly Guid Organization = new("0198f0aa-0000-7000-8000-0000000000aa");

    private static readonly byte[] RawMime = Encoding.ASCII.GetBytes("Subject: synthetic\r\n\r\nbody\r\n");

    /// <summary>Most tests of a send are about something else, so the default shape has to store the message as composed.</summary>
    [Fact]
    public async Task SendingNone_AnAuthoredMessage_ComesBackUnchanged()
    {
        // Act
        var footed = await MailFootings.SendingNone().AppendAsync(Request(), RawMime, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(RawMime, footed.ToArray());
    }

    /// <summary>A test asserting the footer arrived once needs it written where it can be read back as text.</summary>
    [Fact]
    public async Task Over_AStoreHoldingTheAuthorsFooter_WritesItsTextUnderTheMessage()
    {
        // Arrange
        var store = new InMemoryMailFooterStore().Assigning(SyntheticUser.Deployment, Account);
        await store.SaveOwnAsync(SyntheticUser.Deployment, Account, new MailFooterContent("Anna", []), TestContext.Current.CancellationToken);

        // Act
        var footed = await MailFootings.Over(store).AppendAsync(Request(), RawMime, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Encoding.ASCII.GetString(RawMime) + "\r\n[footer:Anna]", Encoding.ASCII.GetString(footed.Span));
    }

    /// <summary>The store refuses what the database would: a footer beside no assignment, a template beside no organization.</summary>
    [Fact]
    public async Task InMemoryMailFooterStore_WritesBesideNothing_AreRefused()
    {
        // Arrange
        var store = new InMemoryMailFooterStore();
        var footer = new MailFooterContent("Anna", []);

        // Act
        var own = await store.SaveOwnAsync(SyntheticUser.Deployment, Account, footer, TestContext.Current.CancellationToken);
        var template = await store.SaveTemplateAsync(Organization, footer, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(own);
        Assert.False(template);
    }

    /// <summary>A template and an own footer are both read back with the organization that holds the account.</summary>
    [Fact]
    public async Task InMemoryMailFooterStore_ReadSources_ReadsTheTemplateTheOwnFooterAndTheHolder()
    {
        // Arrange
        var store = new InMemoryMailFooterStore().Assigning(SyntheticUser.Deployment, Account).Holding(Organization, Account);
        var own = new MailFooterContent("Anna", []);
        var template = new MailFooterContent("{{displayName}}", []);
        await store.SaveOwnAsync(SyntheticUser.Deployment, Account, own, TestContext.Current.CancellationToken);
        await store.SaveTemplateAsync(Organization, template, TestContext.Current.CancellationToken);

        // Act
        var sources = await store.ReadSourcesAsync(SyntheticUser.Deployment, Account, TestContext.Current.CancellationToken);
        await store.RemoveOwnAsync(SyntheticUser.Deployment, Account, TestContext.Current.CancellationToken);
        await store.RemoveTemplateAsync(Organization, TestContext.Current.CancellationToken);
        var cleared = await store.ReadSourcesAsync(SyntheticUser.Deployment, Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new MailFooterSources(Organization, template, own), sources);
        Assert.Equal(new MailFooterSources(Organization, null, null), cleared);
    }

    /// <summary>The composer's lone form is what a reading of the footer in force reduces.</summary>
    [Fact]
    public void TextAppendingMailFooterComposer_ComposeAlone_WritesTheFootersText()
    {
        // Arrange
        var footer = new MailFooter(MailFooterSource.Own, "<p>Anna</p>", "Anna", []);

        // Act
        var composed = new MailFootings.TextAppendingMailFooterComposer().ComposeAlone(footer);

        // Assert
        Assert.Equal("[footer:Anna]", Encoding.UTF8.GetString(composed.Span));
    }

    private static OutgoingEmailRequest Request()
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, "anna@example.test", out var address));

        return OutgoingEmailRequest.Create(
            Account,
            SyntheticUser.Deployment,
            OutgoingEmailRequester.Command("footer-test"),
            [OutgoingRecipient.Create(address, OutgoingRecipientRole.To)]);
    }
}
