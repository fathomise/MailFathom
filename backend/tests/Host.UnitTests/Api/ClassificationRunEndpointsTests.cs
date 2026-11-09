// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Categories;
using MailFathom.Application.Classification;
using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam;
using MailFathom.Application.Spam.Actions;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;
using MailFathom.Domain.Synchronization;
using MailFathom.Host.Api;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers the terms a run is started on and what bounds them.</summary>
/// <remarks>
/// The scope is the subject of most of it: what each function covers is both the default and the bound, because a run
/// over a folder neither reaches would read a whole folder and record nothing. The other half is the dry run being what
/// a caller gets by not asking for anything.
/// </remarks>
public sealed class ClassificationRunEndpointsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountId Account = MailAccountId.Create("work");

    /// <summary>The account a stored run names, which is the user and the identifier together.</summary>
    private static readonly MailAccountId AccountIdentity =
        Account;

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private static readonly MailFolderAlias Archive = MailFolderAlias.Create("ARCHIVE");

    private static readonly OwnMailCategory Project = new("Project Heron", "Mail about the Heron building project.");

    private static readonly EmailCategorizationSettings CategorizationOn = new(IsEnabled: true, [Project]);

    private readonly IClassificationRunStore runs = Substitute.For<IClassificationRunStore>();

    private readonly FakeTimeProvider timeProvider = new(Now);

    /// <summary>
    /// The deployment's half of an agreement with a command it cannot reference. <c>mfctl</c> composes this path from a
    /// constant of its own, and a rename on either side compiles cleanly while the command reaches a 404 that reads
    /// exactly like an endpoint nobody enabled.
    /// </summary>
    [Fact]
    public void Route_IsThePathTheCommandComposes() =>
        Assert.Equal("/classification/runs", ClassificationRunEndpoints.RunsRoute);

    [Fact]
    public void MapClassificationRuns_TheRunRoute_CarriesTheRequestBodyBound()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddRouting();
        services.AddLogging();

        var endpoints = new TestEndpointRouteBuilder(services.BuildServiceProvider());

        // Act
        endpoints.MapGroup(string.Empty).MapClassificationRuns();

        // Assert
        var runRoute = endpoints.DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .First(endpoint => endpoint.RoutePattern.RawText == ClassificationRunEndpoints.RunsRoute
                && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("POST"));

        Assert.Equal(
            ClassificationRunEndpoints.MaxRunRequestBytes,
            runRoute.Metadata.GetMetadata<IRequestSizeLimitMetadata>()!.MaxRequestBodySize);
    }

    /// <summary>Acting on somebody's mailbox is said rather than defaulted to, and the scope defaults to what is configured.</summary>
    [Fact]
    public async Task StartRunAsync_ARequestNamingOnlyTheAccount_StartsADryRunOverTheConfiguredScope()
    {
        // Arrange
        this.runs.FindOutstandingAsync(AccountIdentity, Arg.Any<CancellationToken>()).Returns((ClassificationRun?)null);

        // Act
        var result = await this.StartRunAsync(new ClassificationRunRequestBody(
            Account.Value,
            Folders: null,
            Apply: null,
            Rescore: null, Category: null));

        // Assert
        var started = Assert.IsType<Ok<ClassificationRunStartResponse>>(result.Result);
        Assert.True(started.Value!.Started);
        Assert.Equal(nameof(SpamActionPosture.DryRun), started.Value.Run.Posture);
        Assert.Equal([Inbox.Value], started.Value.Run.SpamFolders);
        Assert.False(started.Value.Run.Rescores);
        Assert.Null(started.Value.Run.Profile);
    }

    [Fact]
    public async Task StartRunAsync_ARequestAskingToApply_StartsARunThatActs()
    {
        // Arrange
        this.runs.FindOutstandingAsync(AccountIdentity, Arg.Any<CancellationToken>()).Returns((ClassificationRun?)null);

        // Act
        var result = await this.StartRunAsync(new ClassificationRunRequestBody(
            Account.Value,
            [Inbox.Value],
            Apply: true,
            Rescore: true, Category: null));

        // Assert
        var started = Assert.IsType<Ok<ClassificationRunStartResponse>>(result.Result);
        Assert.Equal(nameof(SpamActionPosture.Acting), started.Value!.Run.Posture);
        Assert.True(started.Value.Run.Rescores);
    }

    /// <summary>Asking twice is asking once, and the terms of the second request are not applied to the walk under way.</summary>
    [Fact]
    public async Task StartRunAsync_ARunAlreadyOutstanding_AnswersWithItAndItsOwnTerms()
    {
        // Arrange
        this.runs.FindOutstandingAsync(AccountIdentity, Arg.Any<CancellationToken>()).Returns(new ClassificationRun
        {
            Account = AccountIdentity,
            RequestedAt = Now.AddMinutes(-5),
            Terms = ClassificationRunTerms.Create([Inbox], [], SpamActionPosture.DryRun, rescores: false),
            ClassifiedEmailCount = 120,
        });

        // Act
        var result = await this.StartRunAsync(new ClassificationRunRequestBody(
            Account.Value,
            Folders: null,
            Apply: true,
            Rescore: null, Category: null));

        // Assert
        var started = Assert.IsType<Ok<ClassificationRunStartResponse>>(result.Result);
        Assert.False(started.Value!.Started);
        Assert.Equal(nameof(SpamActionPosture.DryRun), started.Value.Run.Posture);
        Assert.Equal(120, started.Value.Run.ClassifiedEmailCount);
        await this.runs.DidNotReceive().SaveAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<ClassificationRun>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A run over a folder nobody classifies would read the whole of it and record nothing.</summary>
    [Fact]
    public async Task StartRunAsync_AFolderNeitherFunctionCovers_IsRefusedAndNamesTheRemedy()
    {
        // Act
        var result = await this.StartRunAsync(new ClassificationRunRequestBody(
            Account.Value,
            [Archive.Value],
            Apply: null,
            Rescore: null, Category: null));

        // Assert
        var refusal = AssertRefused(result.Result);
        Assert.Contains(Archive.Value, refusal.ProblemDetails.Detail, StringComparison.Ordinal);
        await this.runs.DidNotReceive().SaveAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<ClassificationRun>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The scope is asked before the folders are held against the classification scope, so a served account outside it
    /// is answered in the sentence an unconfigured one is rather than by the one naming a folder its user does not
    /// classify.
    /// </summary>
    [Fact]
    public async Task StartRunAsync_AnAccountOutsideTheCallersOrganizationWithAFolderOutsideTheConfiguredScope_RefusesItAsUnknown()
    {
        // Arrange
        var authorization = AccessAuthorizations.ForAdministratorScoped(
            ScopedGrant.Of(
            [
                (MailFathomPermission.AdminOperate,
                    AssignmentScope.Organization(new Guid("0198f0aa-0000-7000-8000-0000000000e7"))),
            ]),
            new StatedAdministrativeTargets().WithMailAccount(Account, new Guid("0198f0aa-0000-7000-8000-0000000000e8")));

        // Act
        var result = await this.StartRunAsync(
            new ClassificationRunRequestBody(Account.Value, [Archive.Value], Apply: null, Rescore: null, Category: null),
            authorization: authorization);

        // Assert
        var refusal = AssertRefused(result.Result);
        Assert.Equal(AdminAccountRequest.Refuse(Account.Value).ProblemDetails.Detail, refusal.ProblemDetails.Detail);
        await this.runs.DidNotReceive().SaveAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<ClassificationRun>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartRunAsync_AFolderNameNoAliasCouldBe_IsRefusedRatherThanRaised()
    {
        // Act
        var result = await this.StartRunAsync(new ClassificationRunRequestBody(
            Account.Value,
            ["in\u0007box"],
            Apply: null,
            Rescore: null, Category: null));

        // Assert
        AssertRefused(result.Result);
    }

    [Fact]
    public async Task StartRunAsync_ARequestNamingOnlyBlankFolders_IsRefused()
    {
        // Act
        var result = await this.StartRunAsync(new ClassificationRunRequestBody(
            Account.Value,
            ["   "],
            Apply: null,
            Rescore: null, Category: null));

        // Assert
        AssertRefused(result.Result);
    }

    /// <summary>An account whose switch is on and whose scope is empty has nothing to default to, so a run over it is refused.</summary>
    [Fact]
    public async Task StartRunAsync_AnAccountCoveringNoFolder_IsRefused()
    {
        // Act
        var result = await this.StartRunAsync(
            new ClassificationRunRequestBody(Account.Value, Folders: null, Apply: null, Rescore: null, Category: null),
            SpamClassificationSettings.Create(isEnabled: true, []));

        // Assert
        AssertRefused(result.Result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nowhere")]
    public async Task StartRunAsync_ARequestNamingNoAccountThisDeploymentServes_IsRefused(string? account)
    {
        // Act
        var result = await this.StartRunAsync(
            new ClassificationRunRequestBody(account, Folders: null, Apply: null, Rescore: null, Category: null));

        // Assert
        AssertRefused(result.Result);
    }

    [Fact]
    public async Task StartRunAsync_ARequestWithNoBody_IsRefusedWithoutStartingAnything()
    {
        // Act
        var result = await this.StartRunAsync(request: null);

        // Assert
        AssertRefused(result.Result);
    }

    /// <summary>An account nobody has asked for a run is an outcome this deployment can state, not a missing resource.</summary>
    [Fact]
    public async Task ReadRunAsync_AnAccountNobodyHasAskedForARun_AnswersWithNoRunRatherThanARefusal()
    {
        // Arrange
        this.runs.FindLatestAsync(AccountIdentity, Arg.Any<CancellationToken>()).Returns((ClassificationRun?)null);

        // Act
        var result = await ClassificationRunEndpoints.ReadRunAsync(
            Account.Value,
            CatalogServing(Account),
            new ClassificationRunReader(this.runs, AdministrativeGrant.WholeSurface),
            AdministrativeGrant.WholeSurface,
            TestContext.Current.CancellationToken);

        // Assert
        var state = Assert.IsType<Ok<ClassificationRunStateResponse>>(result.Result);
        Assert.Equal(Account.Value, state.Value!.Account);
        Assert.Null(state.Value.Run);
    }

    [Fact]
    public async Task ReadRunAsync_ARunThatHasEnded_ReportsItsCountsAndHowItEnded()
    {
        // Arrange
        this.runs.FindLatestAsync(AccountIdentity, Arg.Any<CancellationToken>()).Returns(new ClassificationRun
        {
            Account = AccountIdentity,
            RequestedAt = Now.AddHours(-1),
            Terms = ClassificationRunTerms.Create([Inbox], [], SpamActionPosture.Acting, rescores: false),
            Profile = SpamClassificationProfile.Restore("a67d92672a05"),
            ClassifiedEmailCount = 400,
            SpamEmailCount = 12,
            SkippedEmailCount = 3,
            ActedEmailCount = 12,
            CategorizedEmailCount = 30,
            CategoriesCurrentEmailCount = 90,
            CategoriesUnansweredEmailCount = 5,
            CategoriesClearedEmailCount = 7,
            EndedAt = Now.AddMinutes(-50),
            Ending = ClassificationRunEnding.Completed,
        });

        // Act
        var result = await ClassificationRunEndpoints.ReadRunAsync(
            Account.Value,
            CatalogServing(Account),
            new ClassificationRunReader(this.runs, AdministrativeGrant.WholeSurface),
            AdministrativeGrant.WholeSurface,
            TestContext.Current.CancellationToken);

        // Assert
        var run = Assert.IsType<Ok<ClassificationRunStateResponse>>(result.Result).Value!.Run;
        Assert.Equal((400, 12, 3, 12), (
            run!.ClassifiedEmailCount,
            run.SpamEmailCount,
            run.SkippedEmailCount,
            run.ActedEmailCount));
        Assert.Equal((30, 90, 5, 7), (
            run.CategorizedEmailCount,
            run.CategoriesCurrentEmailCount,
            run.CategoriesUnansweredEmailCount,
            run.CategoriesClearedEmailCount));
        Assert.Equal(nameof(ClassificationRunEnding.Completed), run.Ending);
        Assert.NotNull(run.Profile);
    }

    [Fact]
    public async Task ReadRunAsync_AnAccountThisDeploymentDoesNotServe_IsRefused()
    {
        // Act
        var result = await ClassificationRunEndpoints.ReadRunAsync(
            "nowhere",
            CatalogServing(Account),
            new ClassificationRunReader(this.runs, AdministrativeGrant.WholeSurface),
            AdministrativeGrant.WholeSurface,
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefused(result.Result);
    }

    /// <summary>The signals reach the wire by name, and the change a verdict asked for is pointed at rather than described.</summary>

    [Fact]
    public async Task StartRunAsync_BothFunctionsAndNoFolderNamed_WalksWhatEachCovers()
    {
        // Act
        var result = await this.StartRunAsync(Body(), categorization: CategorizationOn, visibleFolders: [Inbox, Archive]);

        // Assert
        var run = Assert.IsType<Ok<ClassificationRunStartResponse>>(result.Result).Value!.Run;
        Assert.Equal([Inbox.Value], run.SpamFolders);
        Assert.Equal([Archive.Value, Inbox.Value], run.CategoryFolders);
    }

    [Fact]
    public async Task StartRunAsync_AFolderOnlyCategoriesCover_GetsOnlyThatHalf()
    {
        // Act
        var result = await this.StartRunAsync(
            Body(folders: [Archive.Value]),
            categorization: CategorizationOn,
            visibleFolders: [Inbox, Archive]);

        // Assert
        var run = Assert.IsType<Ok<ClassificationRunStartResponse>>(result.Result).Value!.Run;
        Assert.Empty(run.SpamFolders);
        Assert.Equal([Archive.Value], run.CategoryFolders);
    }

    /// <summary>The spam half treats a folder withheld from AI as it always has; the category half never reaches it.</summary>
    [Fact]
    public async Task StartRunAsync_AFolderNotVisibleToAI_GetsOnlyTheSpamHalf()
    {
        // Act
        var result = await this.StartRunAsync(
            Body(folders: [Inbox.Value]),
            categorization: CategorizationOn,
            visibleFolders: [Archive]);

        // Assert
        var run = Assert.IsType<Ok<ClassificationRunStartResponse>>(result.Result).Value!.Run;
        Assert.Equal([Inbox.Value], run.SpamFolders);
        Assert.Empty(run.CategoryFolders);
    }

    [Fact]
    public async Task StartRunAsync_SpamClassificationOnly_CoversNoCategories()
    {
        // Act
        var result = await this.StartRunAsync(Body());

        // Assert
        var run = Assert.IsType<Ok<ClassificationRunStartResponse>>(result.Result).Value!.Run;
        Assert.Equal([Inbox.Value], run.SpamFolders);
        Assert.Empty(run.CategoryFolders);
    }

    [Fact]
    public async Task StartRunAsync_CategoriesOnly_CoversNoSpam()
    {
        // Act
        var result = await this.StartRunAsync(
            Body(),
            SpamClassificationSettings.Disabled,
            CategorizationOn,
            visibleFolders: [Inbox]);

        // Assert
        var run = Assert.IsType<Ok<ClassificationRunStartResponse>>(result.Result).Value!.Run;
        Assert.Empty(run.SpamFolders);
        Assert.Equal([Inbox.Value], run.CategoryFolders);
    }

    [Fact]
    public async Task StartRunAsync_NeitherFunctionOn_IsRefusedNamingBothSwitches()
    {
        // Act
        var result = await this.StartRunAsync(Body(), SpamClassificationSettings.Disabled);

        // Assert
        var refusal = AssertRefused(result.Result);
        Assert.Contains("SpamClassification:Enabled", refusal.ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.Contains("Categorization:Enabled", refusal.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartRunAsync_OneOwnCategory_AsksAboutThatAloneOverVisibleFolders()
    {
        // Act
        var result = await this.StartRunAsync(
            Body(category: Project.Name.ToUpperInvariant()),
            categorization: CategorizationOn,
            visibleFolders: [Inbox]);

        // Assert
        var run = Assert.IsType<Ok<ClassificationRunStartResponse>>(result.Result).Value!.Run;
        Assert.Empty(run.SpamFolders);
        Assert.Equal([Inbox.Value], run.CategoryFolders);
        Assert.Equal(Project.Name, run.Category);
    }

    [Fact]
    public async Task StartRunAsync_OneOwnCategoryOverAFolderNotVisibleToAI_IsRefused()
    {
        // Act
        var result = await this.StartRunAsync(
            Body(folders: [Archive.Value], category: Project.Name),
            categorization: CategorizationOn,
            visibleFolders: [Inbox]);

        // Assert
        AssertRefused(result.Result);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task StartRunAsync_OneOwnCategoryBesideRescoreOrApply_IsRefused(bool apply, bool rescore)
    {
        // Act
        var result = await this.StartRunAsync(
            new ClassificationRunRequestBody(Account.Value, Folders: null, apply, rescore, Project.Name),
            categorization: CategorizationOn,
            visibleFolders: [Inbox]);

        // Assert
        AssertRefused(result.Result);
    }

    /// <summary>The name is a subject somebody chose, and a refusal is written to a log.</summary>
    [Fact]
    public async Task StartRunAsync_AnOwnCategoryTheAccountDoesNotName_IsRefusedWithoutRepeatingIt()
    {
        // Act
        var result = await this.StartRunAsync(
            Body(category: "Divorce proceedings"),
            categorization: CategorizationOn,
            visibleFolders: [Inbox]);

        // Assert
        var refusal = AssertRefused(result.Result);
        Assert.DoesNotContain("Divorce", refusal.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartRunAsync_OneOwnCategoryWhereTheAccountDoesNotCategorize_IsRefused()
    {
        // Act
        var result = await this.StartRunAsync(Body(category: Project.Name));

        // Assert
        AssertRefused(result.Result);
    }

    private static ClassificationRunRequestBody Body(IReadOnlyList<string>? folders = null, string? category = null) =>
        new(Account.Value, folders, Apply: null, Rescore: null, category);

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

    private static ISpamClassificationSettingsReader SettingsReading(SpamClassificationSettings settings)
    {
        var reader = Substitute.For<ISpamClassificationSettingsReader>();
        reader.SettingsFor(Arg.Any<MailAccountId>()).Returns(settings);

        return reader;
    }

    private static IEmailCategorizationSettingsReader CategorizationReading(EmailCategorizationSettings settings)
    {
        var reader = Substitute.For<IEmailCategorizationSettingsReader>();
        reader.SettingsFor(Arg.Any<MailAccountId>()).Returns(settings);

        return reader;
    }

    private static IDeploymentMailFolders FoldersVisibleToAI(IReadOnlyList<MailFolderAlias> visible)
    {
        var folders = Substitute.For<IDeploymentMailFolders>();
        folders
            .ReadAsync(MailFolderSelection.VisibleToAI, Arg.Any<IReadOnlyCollection<MailAccountId>>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MailFolderIdentity>)[.. visible.Select(alias => new MailFolderIdentity(Account, alias))]);

        return folders;
    }

    private async Task<Results<Ok<ClassificationRunStartResponse>, ProblemHttpResult>> StartRunAsync(
        ClassificationRunRequestBody? request,
        SpamClassificationSettings? settings = null,
        EmailCategorizationSettings? categorization = null,
        IReadOnlyList<MailFolderAlias>? visibleFolders = null,
        AccessAuthorization? authorization = null)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        using var scoped = AccountScopedRequest.Serving(
            services =>
            {
                services.AddSingleton(SettingsReading(settings ?? SpamClassificationSettings.Create(isEnabled: true, [Inbox])));
                services.AddSingleton(CategorizationReading(categorization ?? EmailCategorizationSettings.Disabled));
            },
            Account);

        return await ClassificationRunEndpoints.StartRunAsync(
            request,
            CatalogServing(Account),
            scoped.MailSettings,
            new ClassificationRunRequests(
                this.runs,
                new OptimisticConcurrencyRetryPolicy(
                    sessionFactory,
                    new PersistenceConcurrencyOptions(),
                    this.timeProvider),
                this.timeProvider,
                AdministrativeGrant.WholeSurface),
            FoldersVisibleToAI(visibleFolders ?? []),
            scoped.Context,
            authorization ?? AdministrativeGrant.WholeSurface,
            TestContext.Current.CancellationToken);
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
