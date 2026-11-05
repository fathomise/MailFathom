// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Actions;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Editing;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Application.Rules.Preview;
using MailFathom.Application.Rules.Sets;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Synchronization;
using MailFathom.Host.Api;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Rules;
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

/// <summary>Covers what the routes changing and previewing an account's rules answer, and what they refuse.</summary>
/// <remarks>
/// Each route is driven through the real editor and the real preview over a substituted store, so what is asserted is
/// the whole path a request takes: the account it names, the version it states, the rule object it carries, the
/// outcome the editor reached, and a refusal that names what to fix without repeating what the rule declares.
/// </remarks>
public sealed class MailRuleChangeEndpointsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private readonly IMailRuleSetStore store = Substitute.For<IMailRuleSetStore>();
    private readonly IMailRuleEvaluationStore mail = Substitute.For<IMailRuleEvaluationStore>();
    private readonly IMailRuleActionPermissionReader permissions = Substitute.For<IMailRuleActionPermissionReader>();
    private readonly FakeTimeProvider timeProvider = new(Now);
    private readonly List<MailRuleSetArchiveEntry> committed = [];

    public MailRuleChangeEndpointsTests()
    {
        this.permissions.GetRuleActionPermissions(Arg.Any<MailAccountId>()).Returns(MailRuleActionPermissions.Default);
        this.store
            .CommitAsync(Arg.Any<IPersistenceSession>(), Arg.Do<MailRuleSetArchiveEntry>(this.committed.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        this.mail
            .GetStoredEmailsAsync(Arg.Any<MailAccountId>(), Arg.Any<StoredEmailId?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        this.Arrange(new StoredMailRuleSet(Account, 4, [Declare("file-invoices"), Declare("mark-newsletters", enabled: false)]));
    }

    /// <summary>The bound on each change body, read by verb because the create shares its path with the reading.</summary>
    /// <param name="route">The route the change is made on.</param>
    [Theory]
    [InlineData(MailRuleEndpoints.RulesRoute)]
    [InlineData(MailRuleEndpoints.ReplacementRoute)]
    [InlineData(MailRuleEndpoints.EnablementRoute)]
    [InlineData(MailRuleEndpoints.DisablementRoute)]
    [InlineData(MailRuleEndpoints.PositionRoute)]
    [InlineData(MailRuleEndpoints.RemovalRoute)]
    [InlineData(MailRuleEndpoints.PreviewRoute)]
    public void MapMailRules_AChangeOrPreviewRoute_CarriesTheRequestBodyBound(string route)
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddRouting();
        services.AddLogging();

        var endpoints = new TestEndpointRouteBuilder(services.BuildServiceProvider());

        // Act
        endpoints.MapGroup(string.Empty).MapMailRules();

        // Assert
        var change = endpoints.DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText == route
                && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("POST"));

        Assert.Equal(
            MailRuleEndpoints.MaxChangeRequestBytes,
            change.Metadata.GetMetadata<IRequestSizeLimitMetadata>()!.MaxRequestBodySize);
    }

    /// <summary>A created rule never acts before somebody enables it, and the answer names what an operator checks the write by.</summary>
    [Fact]
    public async Task CreateRuleAsync_ARuleObject_CommitsItDisabledAtTheEndAndReportsItsIdentityAndTheNewVersion()
    {
        // Act
        var result = await this.CreateAsync(Change(declaration: """
            { "Name": "flag-supplier", "Condition": "senderDomain == 'supplier.example'", "Triggers": [ "Arrival" ], "Actions": { "MarkAsFlagged": true } }
            """));

        // Assert
        var answered = Assert.IsType<Ok<MailRuleChangeResponse>>(result.Result).Value!;
        var archived = Assert.Single(this.committed);
        var created = archived.Declarations[^1];
        Assert.Equal((Account.Value, "flag-supplier", 5L), (answered.Account, answered.Name, answered.Version));
        Assert.Equal(created.Id.Value, answered.Rule);
        Assert.Equal(archived.Revision.Value, answered.Revision);
        Assert.False(created.Enabled);
        Assert.Equal([MailRuleAction.SetFlagged(isFlagged: true)], created.Actions);
        Assert.Equal([MailRuleTrigger.Arrival], created.Triggers);
    }

    /// <summary>A key a rule does not have is refused by name, because a rule bound to a second mailbox or enabled on creation is exactly what the shape exists to rule out.</summary>
    /// <param name="key">The key the rule object carries.</param>
    /// <param name="value">What the key holds.</param>
    [Theory]
    [InlineData("Accounts", "[ \"personal\" ]")]
    [InlineData("Enabled", "true")]
    public async Task CreateRuleAsync_ARuleObjectCarryingAKeyARuleDoesNotHave_IsRefusedNamingTheKeyAndCommitsNothing(string key, string value)
    {
        // Act
        var result = await this.CreateAsync(Change(declaration: $$"""{ "Name": "flag-supplier", "Condition": "isSeen", "{{key}}": {{value}} }"""));

        // Assert
        var refusal = AssertRefused(result.Result);
        Assert.Contains($"$.{key}", refusal.ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("personal", refusal.ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.Empty(this.committed);
    }

    /// <summary>What the validator cannot see because it is not a value at all is refused by the key that held it, quoting nothing it held.</summary>
    /// <param name="declaration">The rule object.</param>
    /// <param name="key">The key the refusal names.</param>
    [Theory]
    [InlineData("""{ "Name": "a", "Condition": "isSeen", "Triggers": [ "OnTuesdays" ] }""", "Triggers")]
    [InlineData("""{ "Name": "a", "Condition": "isSeen", "Triggers": [ "Schedule" ], "Schedule": "OnTuesdays" }""", "Schedule")]
    [InlineData("""{ "Name": "a", "Condition": "isSeen", "Actions": { "MoveTo": "  " } }""", "MoveTo")]
    [InlineData("""{ "Name": "a", "Condition": "isSeen", "Actions": { "AddKeywords": [] } }""", "AddKeywords")]
    [InlineData("""{ "Name": "a", "Condition": "isSeen", "Actions": { "SetKeywords": [ "two words" ] } }""", "SetKeywords")]
    public async Task CreateRuleAsync_AKeyHoldingNoValueItTakes_IsRefusedNamingTheKey(string declaration, string key)
    {
        // Act
        var result = await this.CreateAsync(Change(declaration: declaration));

        // Assert
        var refusal = AssertRefused(result.Result);
        Assert.Contains(key, refusal.ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("OnTuesdays", refusal.ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("two words", refusal.ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.Empty(this.committed);
    }

    /// <summary>
    /// Every defect is listed, the rule named by its identifier, and nothing it declares is repeated: the condition and
    /// the folder are text a person typed, and the refusal names the compiler's reading and the position instead.
    /// </summary>
    [Fact]
    public async Task CreateRuleAsync_ACandidateTheValidatorRefuses_ListsEveryDefectWithoutRepeatingTheRule()
    {
        // Act
        var result = await this.CreateAsync(Change(declaration: """
            { "Name": "file-supplier", "Condition": "senderDomain == 'supplier.example' and", "Actions": { "MoveTo": "supplier-letters" } }
            """));

        // Assert
        var detail = AssertRefused(result.Result).ProblemDetails.Detail!;
        var defects = detail.Split('\n').Skip(1).ToArray();
        Assert.True(defects.Length >= 2, detail);
        Assert.All(defects, defect => Assert.StartsWith("- ", defect, StringComparison.Ordinal));
        Assert.DoesNotContain("supplier.example", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("supplier-letters", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("file-supplier", detail, StringComparison.Ordinal);
        Assert.Empty(this.committed);
    }

    /// <summary>A change written against an older version is refused as a conflict naming both versions, so the operator knows to read again.</summary>
    [Fact]
    public async Task CreateRuleAsync_AStaleVersion_IsAConflictNamingBothVersionsAndCommitsNothing()
    {
        // Act
        var result = await this.CreateAsync(Change(version: 3, declaration: """{ "Name": "a", "Condition": "isSeen" }"""));

        // Assert
        var conflict = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Contains("version 4", conflict.ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.Contains("version 3", conflict.ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.Equal(MailFathomErrorCode.MailRuleSetVersionSuperseded.Value, conflict.ProblemDetails.Extensions["errorCode"]);
        Assert.Empty(this.committed);
    }

    /// <summary>A change stating no version would overwrite whatever it found, which is the lost update the version exists to refuse.</summary>
    /// <param name="version">The version the request states.</param>
    [Theory]
    [InlineData(null)]
    [InlineData(-1L)]
    public async Task CreateRuleAsync_NoUsableVersion_IsRefusedWithoutWriting(long? version)
    {
        // Act
        var result = await this.CreateAsync(new MailRuleChangeRequest(Account.Value, version, Declaration: Json("""{ "Name": "a", "Condition": "isSeen" }""")));

        // Assert
        Assert.Contains("version", AssertRefused(result.Result).ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.Empty(this.committed);
    }

    /// <summary>Every change names the account it acts on, and one this deployment does not serve is refused before anything is read.</summary>
    /// <param name="account">The account the request names.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nowhere")]
    public async Task RemoveRuleAsync_AnAccountThisDeploymentDoesNotServe_IsRefusedWithoutTakingTheLock(string? account)
    {
        // Act
        var result = await this.RemoveAsync(Change(account: account, rule: "file-invoices"));

        // Assert
        AssertRefused(result.Result);
        await this.store.DidNotReceive().LockAsync(Arg.Any<IPersistenceSession>(), Arg.Any<MailAccountId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>An account outside the caller's organization is answered as one the deployment does not serve, so its existence is not told.</summary>
    [Fact]
    public async Task EnableRuleAsync_AnAccountOutsideTheCallersOrganization_RefusesItAsUnknownWithoutWriting()
    {
        // Arrange
        var authorization = AccessAuthorizations.ForAdministratorScoped(
            ScopedGrant.Of(
            [
                (MailFathomPermission.AdminConfigurationWrite,
                    AssignmentScope.Organization(new Guid("0198f0aa-0000-7000-8000-0000000000e9"))),
            ]),
            new StatedAdministrativeTargets().WithMailAccount(Account, new Guid("0198f0aa-0000-7000-8000-0000000000ea")));

        // Act
        var result = await MailRuleEndpoints.EnableRuleAsync(
            Change(rule: "mark-newsletters"),
            CatalogServing(Account),
            this.CreateEditor(authorization),
            authorization,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AdminAccountRequest.Refuse(Account.Value).ProblemDetails.Detail, AssertRefused(result.Result).ProblemDetails.Detail);
        Assert.Empty(this.committed);
    }

    /// <summary>A person names a rule by what they read, so the name resolves against the account's set and the answer names the identifier.</summary>
    [Fact]
    public async Task EnableRuleAsync_ARuleNamedByItsName_EnablesThatRuleAndReportsItsIdentifier()
    {
        // Act
        var result = await MailRuleEndpoints.EnableRuleAsync(
            Change(rule: "Mark-Newsletters"),
            CatalogServing(Account),
            this.CreateEditor(),
            AdministrativeGrant.WholeSurface,
            TestContext.Current.CancellationToken);

        // Assert
        var answered = Assert.IsType<Ok<MailRuleChangeResponse>>(result.Result).Value!;
        Assert.Equal((Declare("mark-newsletters").Id.Value, "mark-newsletters"), (answered.Rule, answered.Name));
        Assert.True(Assert.Single(this.committed).Declarations[1].Enabled);
    }

    [Fact]
    public async Task DisableRuleAsync_AnEnabledRuleNamedByItsIdentifier_DisablesIt()
    {
        // Act
        var result = await MailRuleEndpoints.DisableRuleAsync(
            Change(rule: Declare("file-invoices").Id.ToString()),
            CatalogServing(Account),
            this.CreateEditor(),
            AdministrativeGrant.WholeSurface,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<Ok<MailRuleChangeResponse>>(result.Result);
        Assert.False(Assert.Single(this.committed).Declarations[0].Enabled);
    }

    /// <summary>A replacement keeps the rule's state, so editing an enabled rule cannot quietly leave it off, nor a disabled one on.</summary>
    [Fact]
    public async Task ReplaceRuleAsync_ADisabledRule_ReplacesWhatItDeclaresAndLeavesItDisabled()
    {
        // Act
        var result = await MailRuleEndpoints.ReplaceRuleAsync(
            Change(rule: "mark-newsletters", declaration: """{ "Name": "mark-old-newsletters", "Condition": "ageInDays > 30", "Actions": { "MarkAsRead": true } }"""),
            CatalogServing(Account),
            this.CreateEditor(),
            AdministrativeGrant.WholeSurface,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("mark-old-newsletters", Assert.IsType<Ok<MailRuleChangeResponse>>(result.Result).Value!.Name);
        var replaced = Assert.Single(this.committed).Declarations[1];
        Assert.Equal((Declare("mark-newsletters").Id, false, "ageInDays > 30"), (replaced.Id, replaced.Enabled, replaced.ConditionText));
    }

    [Fact]
    public async Task MoveRuleAsync_APosition_PutsTheRuleThere()
    {
        // Act
        var result = await MailRuleEndpoints.MoveRuleAsync(
            Change(rule: "mark-newsletters", position: 0),
            CatalogServing(Account),
            this.CreateEditor(),
            AdministrativeGrant.WholeSurface,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<Ok<MailRuleChangeResponse>>(result.Result);
        Assert.Equal(["mark-newsletters", "file-invoices"], Assert.Single(this.committed).Declarations.Select(rule => rule.Name));
    }

    [Fact]
    public async Task MoveRuleAsync_NoPosition_IsRefusedWithoutWriting()
    {
        // Act
        var result = await MailRuleEndpoints.MoveRuleAsync(
            Change(rule: "mark-newsletters"),
            CatalogServing(Account),
            this.CreateEditor(),
            AdministrativeGrant.WholeSurface,
            TestContext.Current.CancellationToken);

        // Assert
        AssertRefused(result.Result);
        Assert.Empty(this.committed);
    }

    /// <summary>A rule the account does not hold is refused without repeating the name, which is text the caller typed.</summary>
    [Fact]
    public async Task RemoveRuleAsync_ARuleTheAccountDoesNotHold_IsRefusedWithoutRepeatingTheName()
    {
        // Act
        var result = await this.RemoveAsync(Change(rule: "file-old-invoices"));

        // Assert
        Assert.DoesNotContain("file-old-invoices", AssertRefused(result.Result).ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.Empty(this.committed);
    }

    [Fact]
    public async Task RemoveRuleAsync_ARuleNamedByItsName_RemovesItAndReportsWhatItWasCalled()
    {
        // Act
        var result = await this.RemoveAsync(Change(rule: "file-invoices"));

        // Assert
        Assert.Equal("file-invoices", Assert.IsType<Ok<MailRuleChangeResponse>>(result.Result).Value!.Name);
        Assert.Equal(["mark-newsletters"], Assert.Single(this.committed).Declarations.Select(rule => rule.Name));
    }

    /// <summary>A preview of a rule not yet written answers a page and writes nothing, which is what lets it come before the create.</summary>
    [Fact]
    public async Task PreviewRuleAsync_ACandidate_AnswersThePageAndWritesNothing()
    {
        // Act
        var result = await this.PreviewAsync(new MailRulePreviewPageRequest(
            Account.Value,
            Declaration: Json("""{ "Name": "flag-supplier", "Condition": "isSeen", "Actions": { "MarkAsFlagged": true } }""")));

        // Assert
        var page = Assert.IsType<Ok<MailRulePreviewResponse>>(result.Result).Value!;
        Assert.Equal((Account.Value, 0, (Guid?)null), (page.Account, page.ExaminedEmailCount, page.ContinueAfter));
        Assert.Equal([("set-flagged", (bool?)true)], page.Requested.Select(action => (action.Mutation, action.DesiredFlaggedState)));
        Assert.Empty(this.committed);
    }

    /// <summary>A preview previews one rule, so naming a stored one and carrying a candidate, or neither, is refused rather than guessed between.</summary>
    /// <param name="namesRule">Whether the request names a stored rule.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PreviewRuleAsync_BothOrNeitherRuleAndCandidate_IsRefused(bool namesRule)
    {
        // Act
        var result = await this.PreviewAsync(new MailRulePreviewPageRequest(
            Account.Value,
            Rule: namesRule ? "file-invoices" : null,
            Declaration: namesRule ? Json("""{ "Name": "a", "Condition": "isSeen" }""") : null));

        // Assert
        AssertRefused(result.Result);
    }

    [Fact]
    public async Task PreviewRuleAsync_AStoredRuleTheAccountDoesNotHold_IsRefused()
    {
        // Act
        var result = await this.PreviewAsync(new MailRulePreviewPageRequest(Account.Value, Rule: "file-old-invoices"));

        // Assert
        AssertRefused(result.Result);
    }

    [Fact]
    public async Task PreviewRuleAsync_ACandidateTheValidatorRefuses_IsRefusedListingItsDefects()
    {
        // Act
        var result = await this.PreviewAsync(new MailRulePreviewPageRequest(
            Account.Value,
            Declaration: Json("""{ "Name": "a", "Condition": "noSuchFact == 1" }""")));

        // Assert
        Assert.StartsWith("- The condition of mail rule ", AssertRefused(result.Result).ProblemDetails.Detail!.Split('\n')[1], StringComparison.Ordinal);
    }

    private static ProblemHttpResult AssertRefused(IResult result)
    {
        var refusal = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);

        return refusal;
    }

    private static MailRuleChangeRequest Change(
        string? account = "work",
        long? version = 4,
        string? rule = null,
        string? declaration = null,
        int? position = null) =>
        new(account, version, rule, declaration is null ? null : Json(declaration), position);

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }

    private static MailRuleDeclaration Declare(string name, bool enabled = true) => new(
        MailRuleId.Create(new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(name)).AsSpan(0, 16))),
        name,
        enabled,
        "isSeen",
        [],
        StopWhenMatched: false,
        Triggers: [MailRuleTrigger.Arrival],
        Schedule: null);

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

        catalog.ReadServedAccountsAsync(Arg.Any<IReadOnlyCollection<MailAccountId>>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlyList<ServedMailAccount>)
                [.. served.Where(account => call.ArgAt<IReadOnlyCollection<MailAccountId>>(0).Contains(account.Id))]);

        return catalog;
    }

    private void Arrange(StoredMailRuleSet stored)
    {
        this.store.ReadAsync(stored.Account, Arg.Any<CancellationToken>()).Returns(stored);
        this.store.LockAsync(Arg.Any<IPersistenceSession>(), stored.Account, Arg.Any<CancellationToken>()).Returns(stored);
    }

    private MailRuleSetValidator CreateValidator() => new(
        new NCalcMailRuleConditionCompiler(),
        this.permissions,
        new MailFolderReferenceResolver(Substitute.For<IMailFolderMappingReader>()));

    private MailRuleSetEditor CreateEditor(AccessAuthorization? authorization = null)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new MailRuleSetEditor(
            this.store,
            this.CreateValidator(),
            MailRuleConditionBounds.Default,
            new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), this.timeProvider),
            authorization ?? AdministrativeGrant.WholeSurface,
            this.timeProvider);
    }

    private Task<Results<Ok<MailRuleChangeResponse>, ProblemHttpResult>> CreateAsync(MailRuleChangeRequest request) =>
        MailRuleEndpoints.CreateRuleAsync(
            request,
            CatalogServing(Account),
            this.CreateEditor(),
            AdministrativeGrant.WholeSurface,
            TestContext.Current.CancellationToken);

    private Task<Results<Ok<MailRuleChangeResponse>, ProblemHttpResult>> RemoveAsync(MailRuleChangeRequest request) =>
        MailRuleEndpoints.RemoveRuleAsync(
            request,
            CatalogServing(Account),
            this.CreateEditor(),
            AdministrativeGrant.WholeSurface,
            TestContext.Current.CancellationToken);

    private Task<Results<Ok<MailRulePreviewResponse>, ProblemHttpResult>> PreviewAsync(MailRulePreviewPageRequest request) =>
        MailRuleEndpoints.PreviewRuleAsync(
            request,
            CatalogServing(Account),
            new MailRulePreview(
                this.store,
                this.mail,
                this.CreateValidator(),
                new NCalcMailRuleConditionCompiler(),
                new MailRuleSetEvaluator(this.timeProvider),
                Substitute.For<IMailFolderMappingReader>(),
                MailRuleConditionBounds.Default,
                AdministrativeGrant.WholeSurface,
                this.timeProvider),
            AdministrativeGrant.WholeSurface,
            TestContext.Current.CancellationToken);

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
