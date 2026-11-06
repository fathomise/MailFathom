// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using System.Text.Json.Nodes;
using MailFathom.Application.Accounts;
using MailFathom.Application.Persistence;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Actions;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Editing;
using MailFathom.Application.Rules.Sets;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Synchronization;
using MailFathom.Host.Api;
using MailFathom.Host.Security.Endpoints;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Rules;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers the file an account's rules are exported as, and what the routes importing one answer and refuse.</summary>
/// <remarks>
/// The import routes are driven through the real editor, the real validator, and the real condition compiler over a
/// substituted store, so a round trip is the whole path: the rules as stored, the file the export writes, that file read
/// back by the import as the command sends it, and the rules the import commits.
/// </remarks>
public sealed class MailRuleImportEndpointsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly MailAccountId Account = MailAccountId.Create("work");
    private static readonly JsonSerializerOptions WireFormat = new(JsonSerializerDefaults.Web);

    /// <summary>Every key a rule object has, written by hand the way an operator or a configuration wrote it.</summary>
    private const string Authored = """
        [
          { "Name": "supplier-invoices", "Condition": "senderDomain == 'supplier.example' and attachmentCount > 0",
            "Triggers": [ "Arrival" ], "Actions": { "MoveTo": "invoices", "MarkAsRead": true, "AddKeywords": [ "Invoice" ] }, "StopWhenMatched": true },
          { "Name": "copy-flagged", "Condition": "isFlagged", "Triggers": [ "Arrival", "Schedule" ], "Schedule": "Daily at 03:30 Europe/Warsaw",
            "Actions": { "CopyTo": "archive", "MarkAsFlagged": false, "RemoveKeywords": [ "Todo" ] } },
          { "Name": "relabel", "Condition": "isSeen", "Actions": { "SetKeywords": [] } },
          { "Name": "purge", "Condition": "isSeen and isFlagged", "Triggers": [ "Schedule" ], "Schedule": "Every 7.00:00:00", "Actions": { "Delete": true } }
        ]
        """;

    private readonly IMailRuleSetStore store = Substitute.For<IMailRuleSetStore>();
    private readonly IMailRuleActionPermissionReader permissions = Substitute.For<IMailRuleActionPermissionReader>();
    private readonly StubMailFolderMappings folders = StubMailFolderMappings.Nothing;
    private readonly FakeTimeProvider timeProvider = new(Now);
    private readonly List<MailRuleSetArchiveEntry> committed = [];

    public MailRuleImportEndpointsTests()
    {
        this.permissions.GetRuleActionPermissions(Arg.Any<MailAccountId>()).Returns(MailRuleActionPermissions.Default with { PermitsDelete = true });
        this.folders
            .With(Account, MailFolderMapping.ToRemotePath(MailFolderAlias.Create("invoices"), RemoteFolderPath.Create("INBOX/Invoices")))
            .With(Account, MailFolderMapping.ToRemotePath(MailFolderAlias.Create("archive"), RemoteFolderPath.Create("Archive")));
        this.store
            .CommitAsync(Arg.Any<IPersistenceSession>(), Arg.Do<MailRuleSetArchiveEntry>(this.committed.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        this.Arrange(StoredMailRuleSet.Empty(Account));
    }

    /// <summary>
    /// The acceptance the export exists for: a set written out and imported into an empty account of a fresh deployment
    /// declares exactly what it declared, in the same order, so it evaluates identically once the rules the file states
    /// enabled are enabled; until then each one arrives disabled, as a created rule does.
    /// </summary>
    [Fact]
    public async Task ExportThenImport_IntoAnEmptyAccount_DeclaresEveryRuleAsItWasInTheSameOrderAndArrivesDisabled()
    {
        // Arrange
        await this.ImportAsync(Import("Replace", RuleObjects(Authored)));
        var source = this.committed.Single().Declarations
            .Select((rule, position) => rule with { Enabled = position % 2 == 0 })
            .ToArray();
        var file = JsonSerializer.Serialize(MailRuleSetDocument.For(source), WireFormat);
        this.committed.Clear();
        this.Arrange(StoredMailRuleSet.Empty(Account));

        // Act
        var result = await this.ImportAsync(Import("Replace", AsTheCommandSends(file)));

        // Assert
        Assert.IsType<Ok<MailRuleImportResponse>>(result.Result);
        var imported = this.committed.Single().Declarations;
        Assert.Equal(source.Select(Content), imported.Select(Content));
        Assert.All(imported, rule => Assert.False(rule.Enabled));
        Assert.Equal(
            source.Select(rule => rule.Enabled),
            JsonDocument.Parse(file).RootElement.GetProperty("Rules").EnumerateArray().Select(rule => rule.GetProperty("Enabled").GetBoolean()));
    }

    /// <summary>The file is the rules and nothing else, written as the mail-rules page writes a rule object, with every key a rule does not declare left out.</summary>
    [Fact]
    public async Task ExportRulesAsync_AStoredSet_WritesEachRuleObjectWithItsStateAndNoOtherKey()
    {
        // Arrange
        await this.ImportAsync(Import("Replace", RuleObjects(Authored)));
        var archived = this.committed.Single();
        this.Arrange(new StoredMailRuleSet(Account, archived.Version, archived.Declarations));

        // Act
        var result = await MailRuleEndpoints.ExportRulesAsync(
            Account.Value,
            CatalogServing(Account),
            new MailRuleSetReader(this.store, new NCalcMailRuleConditionCompiler(), MailRuleConditionBounds.Default, AdministrativeGrant.WholeSurface),
            AdministrativeGrant.WholeSurface,
            TestContext.Current.CancellationToken);

        // Assert
        var document = Assert.IsType<Ok<MailRuleSetDocument>>(result.Result).Value!;
        using var written = JsonDocument.Parse(JsonSerializer.Serialize(document, WireFormat));
        Assert.Equal(["Rules"], written.RootElement.EnumerateObject().Select(property => property.Name));
        var first = written.RootElement.GetProperty("Rules")[0];
        Assert.Equal(
            ["Name", "Enabled", "Condition", "Triggers", "Actions", "StopWhenMatched"],
            first.EnumerateObject().Select(property => property.Name));
        Assert.Equal(["MoveTo", "MarkAsRead", "AddKeywords"], first.GetProperty("Actions").EnumerateObject().Select(property => property.Name));
        Assert.Equal("Daily at 03:30 Europe/Warsaw", written.RootElement.GetProperty("Rules")[1].GetProperty("Schedule").GetString());
    }

    /// <summary>A preview reports what would happen to each rule and writes nothing, and an added rule has no identifier until it is written.</summary>
    [Fact]
    public async Task PreviewImportAsync_AnImport_ReportsEachRuleWithoutAnIdentifierForTheAddedOnesAndCommitsNothing()
    {
        // Act
        var result = await MailRuleEndpoints.PreviewImportAsync(
            Import("merge", RuleObjects(Authored)),
            CatalogServing(Account),
            this.CreateEditor(),
            AdministrativeGrant.WholeSurface,
            TestContext.Current.CancellationToken);

        // Assert
        var answer = Assert.IsType<Ok<MailRuleImportResponse>>(result.Result).Value!;
        Assert.False(answer.Committed);
        Assert.Equal(0, answer.Version);
        Assert.All(answer.Rules, rule => Assert.Equal((null, "Added", false), (rule.Rule, rule.Effect, rule.Enabled)));
        Assert.Empty(this.committed);
    }

    /// <summary>One rule object the import cannot read refuses the whole import, naming each such rule by its place and none of what it holds.</summary>
    [Fact]
    public async Task ImportRulesAsync_ARuleObjectStatingEnabled_IsRefusedNamingItByItsPlace()
    {
        // Act
        var result = await this.ImportAsync(Import("Replace", RuleObjects("""
            [ { "Name": "a", "Condition": "isSeen" }, { "Name": "secret-name", "Condition": "isSeen", "Enabled": true } ]
            """)));

        // Assert
        var detail = AssertRefused(result.Result);
        Assert.Contains("Rule #2 of the import:", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Rule #1", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-name", detail, StringComparison.Ordinal);
        Assert.Empty(this.committed);
    }

    /// <summary>Which of the two modes is meant decides whether rules are removed, so an import naming neither is refused rather than defaulted.</summary>
    /// <param name="mode">The mode the request carries.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("overwrite")]
    public async Task ImportRulesAsync_NoModeItReads_IsRefusedAndCommitsNothing(string? mode)
    {
        // Act
        var result = await this.ImportAsync(Import(mode, RuleObjects(Authored)));

        // Assert
        Assert.Contains("Mode", AssertRefused(result.Result), StringComparison.Ordinal);
        Assert.Empty(this.committed);
    }

    /// <summary>An import decided against a version the rules have moved past is the conflict every rule write answers, with its stable code.</summary>
    [Fact]
    public async Task ImportRulesAsync_AStaleVersion_IsAConflictCarryingTheSupersededCode()
    {
        // Arrange
        this.Arrange(new StoredMailRuleSet(Account, 3, []));

        // Act
        var result = await this.ImportAsync(Import("Replace", RuleObjects(Authored), version: 2));

        // Assert
        var conflict = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Equal(MailFathomErrorCode.MailRuleSetVersionSuperseded.Value, conflict.ProblemDetails.Extensions[RouteAuthorization.ErrorCodeExtension]);
        Assert.Empty(this.committed);
    }

    private static string AssertRefused(IResult result)
    {
        var refusal = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);

        return refusal.ProblemDetails.Detail!;
    }

    private static MailRuleImportRequest Import(string? mode, IReadOnlyList<JsonElement> rules, long version = 0) =>
        new(Account.Value, version, mode, rules);

    private static JsonElement[] RuleObjects(string json)
    {
        using var document = JsonDocument.Parse(json);

        return [.. document.RootElement.EnumerateArray().Select(rule => rule.Clone())];
    }

    /// <summary>Reads the exported file the way <c>mfctl rules import</c> does: each rule object, without the state an import does not apply.</summary>
    private static JsonElement[] AsTheCommandSends(string file) =>
    [
        .. JsonNode.Parse(file)!["Rules"]!.AsArray().Select(rule =>
        {
            var declaration = rule!.AsObject().DeepClone().AsObject();
            declaration.Remove("Enabled");

            return JsonSerializer.SerializeToElement(declaration);
        }),
    ];

    private static string Content(MailRuleDeclaration rule) => string.Join(
        " | ",
        rule.Name,
        rule.ConditionText,
        string.Join(", ", rule.Actions.Select(action => action.CanonicalForm)),
        string.Join(", ", rule.Triggers.Select(trigger => trigger.Name)),
        rule.Schedule?.CanonicalForm ?? "no schedule",
        rule.StopWhenMatched);

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

    private MailRuleSetEditor CreateEditor()
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new MailRuleSetEditor(
            this.store,
            new MailRuleSetValidator(new NCalcMailRuleConditionCompiler(), this.permissions, this.folders.Resolver),
            MailRuleConditionBounds.Default,
            new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), this.timeProvider),
            AdministrativeGrant.WholeSurface,
            this.timeProvider);
    }

    private Task<Results<Ok<MailRuleImportResponse>, ProblemHttpResult>> ImportAsync(MailRuleImportRequest request) =>
        MailRuleEndpoints.ImportRulesAsync(
            request,
            CatalogServing(Account),
            this.CreateEditor(),
            AdministrativeGrant.WholeSurface,
            TestContext.Current.CancellationToken);

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
