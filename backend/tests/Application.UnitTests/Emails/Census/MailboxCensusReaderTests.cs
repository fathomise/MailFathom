// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Categories;
using MailFathom.Application.Emails.Census;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Census;

public sealed class MailboxCensusReaderTests
{
    private static readonly MailAccountId Primary = MailAccountId.Create("primary");

    private readonly IStoredEmailCensusReader store = Substitute.For<IStoredEmailCensusReader>();

    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));

    /// <summary>A count is a read of the person's mail, so it is refused to a caller who may not read it before anything is counted.</summary>
    [Fact]
    public async Task CountAsync_ACallerNotGrantedMailRead_IsRefusedWithoutCounting()
    {
        // Arrange
        var reader = this.Reader(AccessAuthorizations.ForCallerGranted());

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            reader.CountAsync(new CountEmailsRequest(), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailRead, refusal.RequiredPermission);
        await this.store.DidNotReceiveWithAnyArgs().CountAsync(default!, default!, default, TestContext.Current.CancellationToken);
    }

    /// <summary>A ceiling outside the accepted range is refused rather than clamped, as a listing's page size is.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(MailboxCensusReader.MaximumLeadingGroups + 1)]
    public async Task CountAsync_AGroupCeilingOutsideTheRange_IsRefused(int groups)
    {
        // Arrange
        var reader = this.Reader();

        // Act, Assert
        await Assert.ThrowsAsync<MailboxQueryPageSizeOutOfRangeException>(() =>
            reader.CountAsync(new CountEmailsRequest { MaximumGroups = groups }, TestContext.Current.CancellationToken));
    }

    /// <summary>A request naming no ceiling asks storage for the default number of groups, over the scope and filters it was given.</summary>
    [Fact]
    public async Task CountAsync_NoCeilingNamed_AsksForTheDefaultNumberOfGroupsOverTheFilters()
    {
        // Arrange
        var census = new MailCensus([new MailCensusGroup("ada@example.org", 3)], 1, 3, 3);
        this.store.CountAsync(Arg.Any<MailboxEmailSelection>(), Arg.Any<MailCensusGrouping>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(census);
        var reader = this.Reader();

        // Act
        var counted = await reader.CountAsync(
            new CountEmailsRequest { Dimension = MailCensusDimension.SenderAddress, SenderAddress = "Ada@Example.org" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(census, counted);
        await this.store.Received(1).CountAsync(
            Arg.Is<MailboxEmailSelection>(selection => string.Equals(selection.SenderNormalizedAddress, "ada@example.org", StringComparison.OrdinalIgnoreCase)),
            Arg.Is<MailCensusGrouping>(grouping => grouping.Dimension == MailCensusDimension.SenderAddress),
            MailboxCensusReader.DefaultLeadingGroups,
            Arg.Any<CancellationToken>());
    }

    /// <summary>A day is read on the person's clock, so the zone the request names reaches storage with the grouping.</summary>
    [Fact]
    public async Task CountAsync_APeriod_HandsStorageThePersonsZone()
    {
        // Arrange
        Assert.True(UserTimeZone.TryRead("Europe/Warsaw", out var warsaw));
        this.store.CountAsync(Arg.Any<MailboxEmailSelection>(), Arg.Any<MailCensusGrouping>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(MailCensus.Empty);
        var reader = this.Reader();

        // Act
        await reader.CountAsync(
            new CountEmailsRequest { Dimension = MailCensusDimension.Day, Zone = warsaw },
            TestContext.Current.CancellationToken);

        // Assert
        await this.store.Received(1).CountAsync(
            Arg.Any<MailboxEmailSelection>(),
            Arg.Is<MailCensusGrouping>(grouping => grouping.Zone == warsaw && grouping.ShownCategories.Count == 0),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A census by category may count only what each account shows, so storage is handed every account's offer and nothing else.</summary>
    [Fact]
    public async Task CountAsync_ByCategory_HandsStorageWhatEachAccountShows()
    {
        // Arrange
        var offer = MailCategoryOffer.Of(disabledDefaults: [], ownCategoryNames: ["Invoices"]);
        var categories = StubMailCategories.Offering(new StubMailCategories(
            new Dictionary<MailAccountId, MailCategoryOffer> { [Primary] = offer },
            new Dictionary<StoredEmailId, IReadOnlyList<string>>()));
        this.store.CountAsync(Arg.Any<MailboxEmailSelection>(), Arg.Any<MailCensusGrouping>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(MailCensus.Empty);
        var reader = this.Reader(categories: categories);

        // Act
        await reader.CountAsync(new CountEmailsRequest { Dimension = MailCensusDimension.Category }, TestContext.Current.CancellationToken);

        // Assert
        await this.store.Received(1).CountAsync(
            Arg.Any<MailboxEmailSelection>(),
            Arg.Is<MailCensusGrouping>(grouping => grouping.ShownCategories[Primary].SequenceEqual(offer.Names)),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A count that outlives its time bound is abandoned and answers nothing, so the caller narrows it rather than waiting.</summary>
    [Fact]
    public async Task CountAsync_ACountOutlivingItsTimeBound_AnswersNothing()
    {
        // Arrange
        var counting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        this.store.CountAsync(Arg.Any<MailboxEmailSelection>(), Arg.Any<MailCensusGrouping>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                counting.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, this.clock, call.Arg<CancellationToken>());

                return MailCensus.Empty;
            });
        var reader = this.Reader();

        // Act
        var census = reader.CountAsync(new CountEmailsRequest(), TestContext.Current.CancellationToken);
        await counting.Task;
        this.clock.Advance(MailboxCensusReader.MaximumDuration);
        var counted = await census;

        // Assert
        Assert.Null(counted);
    }

    /// <summary>The caller stopping is not the count running out of time, so it propagates rather than reading as an empty answer.</summary>
    [Fact]
    public async Task CountAsync_TheCallerCancelling_PropagatesTheCancellation()
    {
        // Arrange
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();
        this.store.CountAsync(Arg.Any<MailboxEmailSelection>(), Arg.Any<MailCensusGrouping>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromCanceled<MailCensus>(call.Arg<CancellationToken>()));
        var reader = this.Reader();

        // Act, Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.CountAsync(new CountEmailsRequest(), caller.Token));
    }

    /// <summary>A deployment serving the caller no account holds nothing to count, which is a census of nothing rather than a query.</summary>
    [Fact]
    public async Task CountAsync_NoAccountServed_AnswersAnEmptyCensusWithoutCounting()
    {
        // Arrange
        var reader = this.Reader(servedAccounts: []);

        // Act
        var counted = await reader.CountAsync(new CountEmailsRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(MailCensus.Empty, counted);
        await this.store.DidNotReceiveWithAnyArgs().CountAsync(default!, default!, default, TestContext.Current.CancellationToken);
    }

    /// <summary>What lies beyond the leading groups is read off the figures rather than counted again.</summary>
    [Fact]
    public void Census_LeadingGroupsOfALargerCensus_StatesWhatLiesBeyondThem()
    {
        // Arrange
        var census = new MailCensus([new MailCensusGroup("a.test", 7), new MailCensusGroup("b.test", 5)], 5, 20, 20);

        // Act
        var beyond = (census.GroupsBeyond, census.CountedBeyond);

        // Assert
        Assert.Equal((3, 8), beyond);
    }

    private MailboxCensusReader Reader(
        AccessAuthorization? authorization = null,
        OfferedMailCategories? categories = null,
        MailAccountId[]? servedAccounts = null)
    {
        var catalog = Substitute.For<ICallerMailAccountCatalog>();
        catalog.ReadAssignedAccountsAsync(Arg.Any<CancellationToken>())
            .Returns([.. (servedAccounts ?? [Primary]).Select(static account => SyntheticServedAccount.Of(account))]);

        return new MailboxCensusReader(
            this.store,
            new MailboxScopeResolver(
                catalog,
                StubMailFolderParticipation.Nothing,
                StubDeploymentMailFolders.Of(StubMailFolderParticipation.Nothing),
                StubMailFolderMappings.ResolvingNothing),
            new RecordingMailboxReadTelemetry(),
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead),
            categories ?? StubMailCategories.OfferingNothing,
            this.clock);
    }
}
