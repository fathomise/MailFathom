// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Spam.History;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Spam;
using MailFathom.Domain.Synchronization;
using MailFathom.Host.Api;
using MailFathom.Host.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what a reading of the recorded classifications may carry.</summary>
public sealed class SpamClassificationEndpointsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private readonly ISpamClassificationHistoryReader classifications =
        Substitute.For<ISpamClassificationHistoryReader>();

    /// <summary>
    /// The deployment's half of an agreement with a command it cannot reference. <c>mfctl</c> composes these paths from
    /// constants of its own, and a rename on either side compiles cleanly while every spam command reaches a 404 that
    /// reads exactly like an endpoint nobody enabled.
    /// </summary>
    [Fact]
    public void Routes_AreThePathsTheCommandComposes()
    {
        Assert.Equal("/spam/classifications", SpamClassificationEndpoints.ClassificationsRoute);
    }

    [Fact]
    public async Task ReadClassificationsAsync_ARecordedClassification_ServesItsSignalsByNameAndPointsAtTheMutation()
    {
        // Arrange
        var recordId = MailboxMutationRecordId.Create(Guid.CreateVersion7());
        this.ArrangePage(new SpamClassificationHistoryEntry(
            StoredEmailId.Create(Guid.CreateVersion7()),
            Inbox,
            SpamVerdict.Spam,
            SpamClassificationStage.Deterministic,
            SpamSubcategory.Phishing,
            SpamClassificationStage.Model,
            SpamPhishingRequest.OneTimeCode,
            SpamImpersonatedParty.Courier,
            SpamAssessment.Create(15.2, 5.0),
            SpamClassificationProfile.Restore("a67d92672a05"),
            ["X-Spam-Flag", "X-Spam-Status"],
            Now,
            [new SpamClassificationRequestedMutation(recordId, MailboxMutation.Relocate)]));

        // Act
        var result = await this.ReadClassificationsAsync();

        // Assert
        var served = Assert.Single(Assert.IsType<Ok<SpamClassificationPageResponse>>(result.Result)
            .Value!
            .Classifications);
        Assert.Equal(
            ("Deterministic", "Phishing", "Model", "OneTimeCode", "Courier"),
            (served.DecidedBy, served.SpamSubcategory, served.SpamSubcategoryNamedBy, served.PhishingRequest, served.ImpersonatedParty));
        Assert.Equal(["X-Spam-Flag", "X-Spam-Status"], served.Signals);
        Assert.Equal(
            (recordId.Value, MailboxMutation.Relocate.Name),
            (served.RequestedMutations[0].Record, served.RequestedMutations[0].Mutation));
    }

    [Fact]
    public async Task ReadClassificationsAsync_AVerdictFilter_NarrowsTheReadingToIt()
    {
        // Arrange
        this.ArrangePage();

        // Act
        await this.ReadClassificationsAsync(verdict: "spam");

        // Assert
        await this.classifications.Received(1).ReadPageAsync(
            Arg.Is<SpamClassificationHistoryQuery>(query => query!.Verdict == SpamVerdict.Spam),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadClassificationsAsync_AVerdictNoRecordCouldCarry_IsRefused()
    {
        // Act
        var result = await this.ReadClassificationsAsync(verdict: "junk");

        // Assert
        AssertRefused(result.Result);
        await this.classifications.DidNotReceive().ReadPageAsync(
            Arg.Any<SpamClassificationHistoryQuery>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadClassificationsAsync_ACursorThisDeploymentDidNotIssue_IsRefused()
    {
        // Act
        var result = await this.ReadClassificationsAsync(cursor: "not-a-cursor!!");

        // Assert
        AssertRefused(result.Result);
    }

    [Fact]
    public async Task ReadClassificationsAsync_APageSizeOutsideTheServedRange_IsRefused()
    {
        // Act
        var result = await this.ReadClassificationsAsync(pageSize: SpamClassificationHistoryQuery.MaximumPageSize + 1);

        // Assert
        AssertRefused(result.Result);
    }

    [Fact]
    public async Task ReadClassificationsAsync_AnAccountThisDeploymentDoesNotServe_IsRefused()
    {
        // Act
        var result = await this.ReadClassificationsAsync(account: "nowhere");

        // Assert
        AssertRefused(result.Result);
    }

    private static ProblemHttpResult AssertRefused(IResult result)
    {
        var refusal = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);

        return refusal;
    }

    private static IDeploymentMailAccountCatalog CatalogServing(params MailAccountId[] accounts)
    {
        var catalog = Substitute.For<IDeploymentMailAccountCatalog>();
        IReadOnlyList<ServedMailAccount> served =
        [
            .. accounts.Select(account => new ServedMailAccount(
                account,
                MailAccountDisplayName.Create(account.Value),
                MailSynchronizationMode.Polling)),
        ];

        catalog.ReadServedAccountsAsync(Arg.Any<CancellationToken>()).Returns(served);
        catalog.ReadServedAccountsAsync(Arg.Any<IReadOnlyCollection<MailAccountId>>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlyList<ServedMailAccount>)
                [.. served.Where(account => call.ArgAt<IReadOnlyCollection<MailAccountId>>(0).Contains(account.Id))]);

        return catalog;
    }

    private void ArrangePage(params SpamClassificationHistoryEntry[] entries) =>
        this.classifications
            .ReadPageAsync(Arg.Any<SpamClassificationHistoryQuery>(), Arg.Any<CancellationToken>())
            .Returns(new SpamClassificationHistoryPage(entries, NextCursor: null));

    private Task<Results<Ok<SpamClassificationPageResponse>, ProblemHttpResult>> ReadClassificationsAsync(
        string? account = null,
        Guid? email = null,
        string? verdict = null,
        DateTimeOffset? from = null,
        DateTimeOffset? before = null,
        int? pageSize = null,
        string? cursor = null) =>
        SpamClassificationEndpoints.ReadClassificationsAsync(
            account ?? Account.Value,
            email,
            verdict,
            from,
            before,
            pageSize,
            cursor,
            CatalogServing(Account),
            new SpamClassificationHistory(this.classifications, AdministrativeGrant.WholeSurface),
            AdministrativeGrant.WholeSurface,
            TestContext.Current.CancellationToken);
}
