// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail.Delivery.Footers;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Footers;

public sealed class OwnMailFootersTests
{
    private static readonly MailAccountId Shared = MailAccountId.Create("shared");

    private static readonly MailAccountId Held = MailAccountId.Create("held");

    private static readonly MailAccountId Unassigned = MailAccountId.Create("elsewhere");

    private static readonly Guid Organization = new("0198f0aa-0000-7000-8000-0000000000aa");

    private readonly InMemoryMailFooterStore store = new InMemoryMailFooterStore()
        .Assigning(SyntheticUser.Deployment, Shared)
        .Assigning(SyntheticUser.Another, Shared)
        .Assigning(SyntheticUser.Deployment, Held)
        .Holding(Organization, Held);

    private readonly StubMailAccountAssignments assignments = new StubMailAccountAssignments()
        .Assigning(SyntheticUser.Deployment, Shared, Held)
        .Assigning(SyntheticUser.Another, Shared);

    [Fact]
    public async Task SaveAsync_TwoUsersOfOneSharedMailbox_EachKeepsTheirOwn()
    {
        // Arrange
        var anna = this.Footers(SyntheticUser.Deployment, MailFathomPermission.ProfileWrite, MailFathomPermission.ProfileRead);
        var jan = this.Footers(SyntheticUser.Another, MailFathomPermission.ProfileWrite, MailFathomPermission.ProfileRead);

        // Act
        await anna.SaveAsync(Shared, "<p>Anna</p>", [], TestContext.Current.CancellationToken);
        await jan.SaveAsync(Shared, "<p>Jan</p>", [], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("<p>Anna</p>", (await anna.ReadAsync(Shared, TestContext.Current.CancellationToken))?.Footer?.Html);
        Assert.Equal("<p>Jan</p>", (await jan.ReadAsync(Shared, TestContext.Current.CancellationToken))?.Footer?.Html);
    }

    [Fact]
    public async Task SaveAsync_AnAccountNotTheUsers_IsNotWritten()
    {
        // Arrange
        var footers = this.Footers(SyntheticUser.Deployment, MailFathomPermission.ProfileWrite);

        // Act
        var write = await footers.SaveAsync(Unassigned, "<p>Anna</p>", [], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFooterWriteOutcome.AccountNotAssigned, write.Outcome);
    }

    [Fact]
    public async Task SaveAsync_AnAccountWhoseOrganizationSetsATemplate_IsRefusedAsSetByTheOrganization()
    {
        // Arrange
        await this.store.SaveTemplateAsync(Organization, new MailFooterContent("<p>{{displayName}}</p>", []), TestContext.Current.CancellationToken);
        var footers = this.Footers(SyntheticUser.Deployment, MailFathomPermission.ProfileWrite);

        // Act
        var write = await footers.SaveAsync(Held, "<p>Anna</p>", [], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFooterWriteOutcome.SetByOrganization, write.Outcome);
        Assert.Null(await this.store.ReadOwnAsync(SyntheticUser.Deployment, Held, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveAsync_AnOrganizationsAccountWithoutATemplate_IsTheUsersToWrite()
    {
        // Arrange
        var footers = this.Footers(SyntheticUser.Deployment, MailFathomPermission.ProfileWrite);

        // Act
        var write = await footers.SaveAsync(Held, "<p>Anna</p>", [], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFooterWriteOutcome.Saved, write.Outcome);
    }

    [Fact]
    public async Task SaveAsync_AFooterOverItsBound_IsRefusedWithTheReason()
    {
        // Arrange
        var footers = this.Footers(SyntheticUser.Deployment, MailFathomPermission.ProfileWrite);

        // Act
        var write = await footers.SaveAsync(
            Shared,
            new string('a', MailFooterContent.MaximumHtmlLength + 1),
            [],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailFooterWriteOutcome.Refused, write.Outcome);
        Assert.Equal(MailFooterRefusalReason.MarkupTooLong, write.Refusal?.Reason);
    }

    [Fact]
    public async Task SaveAsync_ACallerWithoutTheProfileWriteGrant_IsRefused()
    {
        // Arrange
        var footers = this.Footers(SyntheticUser.Deployment, MailFathomPermission.ProfileRead);

        // Act
        var save = () => footers.SaveAsync(Shared, "<p>Anna</p>", [], TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(save);
    }

    [Fact]
    public async Task ReadAsync_AnOrganizationsAccountWithATemplate_SaysTheOrganizationSetsIt()
    {
        // Arrange
        await this.store.SaveTemplateAsync(Organization, new MailFooterContent("<p>{{displayName}}</p>", []), TestContext.Current.CancellationToken);
        var footers = this.Footers(SyntheticUser.Deployment, MailFathomPermission.ProfileRead);

        // Act
        var reading = await footers.ReadAsync(Held, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(reading);
        Assert.True(reading.IsSetByOrganization);
        Assert.Null(reading.Footer);
    }

    [Fact]
    public async Task ReadAsync_AnAccountNotTheUsers_ReadsNothing()
    {
        // Arrange
        var footers = this.Footers(SyntheticUser.Deployment, MailFathomPermission.ProfileRead);

        // Act
        var reading = await footers.ReadAsync(Unassigned, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(reading);
    }

    [Fact]
    public async Task RemoveAsync_AFooterTheUserWrote_IsGone()
    {
        // Arrange
        var footers = this.Footers(SyntheticUser.Deployment, MailFathomPermission.ProfileWrite, MailFathomPermission.ProfileRead);
        await footers.SaveAsync(Shared, "<p>Anna</p>", [], TestContext.Current.CancellationToken);

        // Act
        var removed = await footers.RemoveAsync(Shared, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(removed);
        Assert.Null((await footers.ReadAsync(Shared, TestContext.Current.CancellationToken))?.Footer);
    }

    [Fact]
    public async Task ReadInForceAsync_AccountsWithAndWithoutAFooter_ReadsOnlyThoseThatSendWithOneAndWhoseItIs()
    {
        // Arrange
        await this.store.SaveOwnAsync(SyntheticUser.Deployment, Shared, new MailFooterContent("<p>Anna</p>", []), TestContext.Current.CancellationToken);
        await this.store.SaveTemplateAsync(Organization, new MailFooterContent("<p>{{displayName}}</p>", []), TestContext.Current.CancellationToken);
        var footers = this.Footers(SyntheticUser.Deployment, MailFathomPermission.ProfileRead);

        // Act
        var readings = await footers.ReadInForceAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [(Shared, MailFooterSource.Own), (Held, MailFooterSource.Organization)],
            readings.Select(static reading => (reading.Account, reading.Source)));
    }

    [Fact]
    public async Task ReadInForceAsync_NoFooterAnywhere_ReadsNothing()
    {
        // Arrange
        var footers = this.Footers(SyntheticUser.Deployment, MailFathomPermission.ProfileRead);

        // Act
        var readings = await footers.ReadInForceAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(readings);
    }

    private OwnMailFooters Footers(UserId user, params MailFathomPermission[] granted)
    {
        var renderer = Substitute.For<IEmailContentRenderer>();
        renderer.RenderAsync(Arg.Any<StoredEmailContent>(), Arg.Any<EmailContentRenderingBounds>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailContentRenderingResult.Unreadable()));

        return new OwnMailFooters(
            AccessAuthorizations.ForUserGranted(user, granted),
            this.store,
            this.assignments,
            new MailFootings.VerbatimMailFooterMarkup(),
            MailFootings.Over(this.store),
            new MailFootings.TextAppendingMailFooterComposer(),
            renderer);
    }
}
