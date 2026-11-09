// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Categories;
using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Gating;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Categories;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;
using MailFathom.Domain.Synchronization;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Categories;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.Infrastructure.Persistence.Users.AccountSettings;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves which stored category records leave in the commit that records an account's settings, and what a read of them finds.</summary>
/// <remarks>
/// The delete joins through the stored message to reach its account, which is a statement only PostgreSQL can run, and
/// a mistake in it reaches derived personal data: too narrow keeps what the account withdrew, too wide takes another
/// account's. Every account here is one the test seeded, under an identifier of its own, so no class in the collection
/// reads what this one writes.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedEmailCategoryRecordTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "email-category-withdrawal";

    private const string FolderNotVisibleToAI = "email-category-withheld";

    private const string StillDeclared = "Invoices";

    private const string Withdrawn = "Contracts";

    private const string SwitchedOffDefault = "finance";

    private const uint MessageUid = 7101;

    private static readonly DateTimeOffset RecordedAt = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Rewriting the settings without one own category takes that category's records from that account alone: what is
    /// still declared stays, a default stays because switching one off only hides it, and another account holding the
    /// same name keeps it.
    /// </summary>
    [Fact]
    public async Task RecordSettingsAsync_AnOwnCategoryLeavingTheDeclaration_RemovesOnlyThatAccountsWithdrawnRecords()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var account = Guid.CreateVersion7();
        var otherAccount = Guid.CreateVersion7();

        try
        {
            await SeedAccountsAsync(services, cancellationToken, account, otherAccount);
            var message = await StoreCategorizedMessageAsync(services, account, cancellationToken);
            var otherMessage = await StoreCategorizedMessageAsync(services, otherAccount, cancellationToken);

            // Act
            var recorded = await RecordSettingsAsync(services, account, Declaring(StillDeclared), cancellationToken);

            // Assert
            Assert.True(recorded);
            Assert.Equal(
                [StillDeclared, SwitchedOffDefault],
                await ReadCategoriesAsync(services, message, cancellationToken));
            Assert.Equal(
                [Withdrawn, StillDeclared, SwitchedOffDefault],
                await ReadCategoriesAsync(services, otherMessage, cancellationToken));
        }
        finally
        {
            await RemoveAccountsAsync(services, account, otherAccount);
        }
    }

    /// <summary>
    /// A document that no longer binds says nothing about what the account declares, so the empty list of names it
    /// projects to is not a withdrawal of every own category.
    /// </summary>
    [Fact]
    public async Task RecordSettingsAsync_AnUnreadableDocument_RemovesNoRecord()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var account = Guid.CreateVersion7();

        try
        {
            await SeedAccountsAsync(services, cancellationToken, account);
            var message = await StoreCategorizedMessageAsync(services, account, cancellationToken);

            // Act
            var recorded = await RecordSettingsAsync(services, account, MailAccountQueryableSettings.Unreadable, cancellationToken);

            // Assert
            Assert.True(recorded);
            Assert.Equal(
                [Withdrawn, StillDeclared, SwitchedOffDefault],
                await ReadCategoriesAsync(services, message, cancellationToken));
        }
        finally
        {
            await RemoveAccountsAsync(services, account);
        }
    }

    /// <summary>The read port answers every record of a message, in ordinal order, and nothing for a message holding none.</summary>
    [Fact]
    public async Task ReadAsync_ACategorizedMessageAndAnUnknownOne_AnswersOnlyTheRecordsThatExist()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var account = Guid.CreateVersion7();
        var unknown = StoredEmailId.Create(Guid.CreateVersion7());

        try
        {
            await SeedAccountsAsync(services, cancellationToken, account);
            var message = await StoreCategorizedMessageAsync(services, account, cancellationToken);

            // Act
            var recorded = await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IEmailCategoryRecordReader>().ReadAsync([message, unknown], token),
                cancellationToken);

            // Assert
            Assert.Equal([Withdrawn, StillDeclared, SwitchedOffDefault], recorded[message]);
            Assert.False(recorded.ContainsKey(unknown));
        }
        finally
        {
            await RemoveAccountsAsync(services, account);
        }
    }

    /// <summary>
    /// A run naming one own category replaces that category's record and leaves the message's other answers; a run naming
    /// none replaces the whole set, so an answer holding fewer categories than before leaves fewer records.
    /// </summary>
    [Fact]
    public async Task ReplaceAsync_OneCategoryNamedAndThenNone_ReplacesOnlyWhatTheRunAsked()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var account = Guid.CreateVersion7();
        const string Replacement = "travel";

        try
        {
            await SeedAccountsAsync(services, cancellationToken, account);
            var message = await StoreCategorizedMessageAsync(services, account, cancellationToken);

            // Act
            await ReplaceAsync(services, message, [], StillDeclared, cancellationToken);
            var afterOneCategory = await ReadCategoriesAsync(services, message, cancellationToken);
            await ReplaceAsync(
                services,
                message,
                [new EmailCategoryAssignment(EmailCategoryKind.Default, Replacement, 0.8, "provider/build", "def456")],
                ownCategory: null,
                cancellationToken);

            // Assert
            Assert.Equal([Withdrawn, SwitchedOffDefault], afterOneCategory);
            Assert.Equal([Replacement], await ReadCategoriesAsync(services, message, cancellationToken));
        }
        finally
        {
            await RemoveAccountsAsync(services, account);
        }
    }

    /// <summary>
    /// Clearing takes the categories of the named spam in visible folders of that account and nothing else: not a message
    /// that is not spam, not spam in a folder withheld from AI, not spam outside the batch, and not another account's.
    /// </summary>
    [Fact]
    public async Task ClearCategoriesOfSpamAsync_ABatchOfMixedMail_ClearsAndCountsOnlyTheNamedSpamInTheAccountsVisibleFolders()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var account = Guid.CreateVersion7();
        var otherAccount = Guid.CreateVersion7();

        try
        {
            await SeedAccountsAsync(services, cancellationToken, account, otherAccount);
            var spam = await StoreCategorizedMessageAsync(services, account, FolderAlias, 7201, cancellationToken);
            var notSpam = await StoreCategorizedMessageAsync(services, account, FolderAlias, 7202, cancellationToken);
            var withheldSpam = await StoreCategorizedMessageAsync(services, account, FolderNotVisibleToAI, 7203, cancellationToken);
            var spamOutsideTheBatch = await StoreCategorizedMessageAsync(services, account, FolderAlias, 7204, cancellationToken);
            var otherAccountsSpam = await StoreCategorizedMessageAsync(services, otherAccount, FolderAlias, 7201, cancellationToken);
            await RecordSpamVerdictsAsync(services, cancellationToken, spam, withheldSpam, spamOutsideTheBatch, otherAccountsSpam);

            // Act
            var clearedEmailCount = await ClearCategoriesOfSpamAsync(
                services,
                account,
                [spam, notSpam, withheldSpam, otherAccountsSpam],
                ownCategory: null,
                cancellationToken);

            // Assert
            string[] untouched = [Withdrawn, StillDeclared, SwitchedOffDefault];
            Assert.Equal(1, clearedEmailCount);
            Assert.Empty(await ReadCategoriesAsync(services, spam, cancellationToken));
            Assert.Equal(untouched, await ReadCategoriesAsync(services, notSpam, cancellationToken));
            Assert.Equal(untouched, await ReadCategoriesAsync(services, withheldSpam, cancellationToken));
            Assert.Equal(untouched, await ReadCategoriesAsync(services, spamOutsideTheBatch, cancellationToken));
            Assert.Equal(untouched, await ReadCategoriesAsync(services, otherAccountsSpam, cancellationToken));
        }
        finally
        {
            await RemoveAccountsAsync(services, account, otherAccount);
        }
    }

    /// <summary>A run applying one own category takes only that category from a message that is spam.</summary>
    [Fact]
    public async Task ClearCategoriesOfSpamAsync_OneOwnCategoryNamed_RemovesOnlyThatCategory()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var account = Guid.CreateVersion7();

        try
        {
            await SeedAccountsAsync(services, cancellationToken, account);
            var spam = await StoreCategorizedMessageAsync(services, account, cancellationToken);
            await RecordSpamVerdictsAsync(services, cancellationToken, spam);

            // Act
            var clearedEmailCount = await ClearCategoriesOfSpamAsync(services, account, [spam], StillDeclared, cancellationToken);

            // Assert
            Assert.Equal(1, clearedEmailCount);
            Assert.Equal([Withdrawn, SwitchedOffDefault], await ReadCategoriesAsync(services, spam, cancellationToken));
        }
        finally
        {
            await RemoveAccountsAsync(services, account);
        }
    }

    /// <summary>A run reads a message the rules have evaluated together with its records, and leaves one still arriving to the arrival stage.</summary>
    [Fact]
    public async Task GetStoredEmailsToCategorizeAsync_EvaluatedAndArrivingMail_AnswersTheEvaluatedMessageWithItsRecords()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var account = Guid.CreateVersion7();

        try
        {
            await SeedAccountsAsync(services, cancellationToken, account);
            var evaluated = await StoreCategorizedMessageAsync(services, account, FolderAlias, 7301, cancellationToken);
            var arriving = await StoreCategorizedMessageAsync(services, account, FolderAlias, 7302, cancellationToken);
            await MarkContentAvailableAsync(services, cancellationToken, evaluated, arriving);
            await OrchestratedRuleEvaluationStamp.ApplyAsync(services, evaluated, RecordedAt, cancellationToken);

            // Act
            var candidates = await services.InScopeAsync(
                (scope, token) => StoreIn(scope).GetStoredEmailsToCategorizeAsync(
                    MailAccountId.Create(account.ToString("D")),
                    [evaluated, arriving],
                    token),
                cancellationToken);

            // Assert
            var candidate = Assert.Single(candidates);
            Assert.Equal(evaluated, candidate.Email.Id);
            Assert.Equal(
                [Withdrawn, StillDeclared, SwitchedOffDefault],
                candidate.Records.Select(static record => record.Category).Order(StringComparer.Ordinal));
        }
        finally
        {
            await RemoveAccountsAsync(services, account);
        }
    }

    /// <summary>Builds the store over a deployment in which every test account's one folder is visible to AI and none is junk.</summary>
    /// <remarks>
    /// The folders are named per account rather than taken from the deployment's mapping, because the accounts here are
    /// seeded per test and the mapping serves one of its own.
    /// </remarks>
    private static EmailCategoryStore StoreIn(IServiceProvider scope)
    {
        var deploymentFolders = Substitute.For<IDeploymentMailFolders>();
        deploymentFolders
            .ReadAsync(MailFolderSelection.VisibleToAI, Arg.Any<IReadOnlyCollection<MailAccountId>>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlyList<MailFolderIdentity>)
            [
                .. call.Arg<IReadOnlyCollection<MailAccountId>>()
                    .Select(static account => new MailFolderIdentity(account, MailFolderAlias.Create(FolderAlias))),
            ]);
        deploymentFolders
            .ReadAsync(MailFolderSelection.Junk, Arg.Any<IReadOnlyCollection<MailAccountId>>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MailFolderIdentity>)[]);

        return new EmailCategoryStore(
            scope.GetRequiredService<MailFathomDbContext>(),
            deploymentFolders,
            scope.GetRequiredService<DerivedWorkGate>());
    }

    private static async Task<int> ClearCategoriesOfSpamAsync(
        OrchestratedMailFathomServices services,
        Guid account,
        IReadOnlyList<StoredEmailId> emails,
        string? ownCategory,
        CancellationToken cancellationToken)
    {
        var clearedEmailCount = 0;

        Assert.Equal(
            PersistenceCommitResult.Committed,
            await services.CommitAsync(
                async (scope, session, token) => clearedEmailCount = await StoreIn(scope).ClearCategoriesOfSpamAsync(
                    session,
                    MailAccountId.Create(account.ToString("D")),
                    emails,
                    ownCategory,
                    token),
                cancellationToken));

        return clearedEmailCount;
    }

    private static async Task RecordSpamVerdictsAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken,
        params StoredEmailId[] messages) =>
        Assert.Equal(
            PersistenceCommitResult.Committed,
            await services.CommitAsync(
                async (_, session, token) =>
                {
                    var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, token);

                    context.EmailSpamClassifications.AddRange(messages.Select(static message => new EmailSpamClassificationEntity
                    {
                        StoredEmailId = message.Value,
                        Verdict = SpamVerdict.Spam,
                        DecidedBy = SpamClassificationStage.Deterministic,
                        EvaluatedAt = RecordedAt,
                    }));

                    await context.SaveChangesAsync(token);
                },
                cancellationToken));

    private static async Task MarkContentAvailableAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken,
        params StoredEmailId[] messages)
    {
        Guid[] identifiers = [.. messages.Select(static message => message.Value)];

        await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .StoredEmails
                .Where(email => identifiers.Contains(email.Id))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(email => email.ContentAvailability, StoredEmailContentAvailability.Available),
                    token),
            cancellationToken);
    }

    private static async Task ReplaceAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId message,
        IReadOnlyList<EmailCategoryAssignment> assignments,
        string? ownCategory,
        CancellationToken cancellationToken) =>
        Assert.Equal(
            PersistenceCommitResult.Committed,
            await services.CommitAsync(
                async (scope, session, token) => Assert.True(
                    await scope.GetRequiredService<IEmailCategoryStore>().ReplaceAsync(session, message, assignments, ownCategory, token)),
                cancellationToken));

    private static MailAccountQueryableSettings Declaring(params string[] ownCategoryNames) => new(
        IsReadable: true,
        MailSynchronizationMode.Polling,
        ClassifiesSpam: false,
        [],
        [],
        [],
        ownCategoryNames);

    private static Task<bool> RecordSettingsAsync(
        OrchestratedMailFathomServices services,
        Guid account,
        MailAccountQueryableSettings settings,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailAccountRecordStore>()
                .RecordSettingsAsync(account, readFrom: 1, _ => settings, token),
            cancellationToken);

    private static async Task SeedAccountsAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken,
        params Guid[] accounts) =>
        Assert.Equal(
            PersistenceCommitResult.Committed,
            await services.CommitAsync(
                async (_, session, token) =>
                {
                    var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, token);

                    context.MailAccountRecords.AddRange(accounts.Select(Row));

                    await context.SaveChangesAsync(token);
                },
                cancellationToken));

    private static MailAccountRecordEntity Row(Guid id) => new()
    {
        Id = id,
        DisplayName = $"account-{id:N}",
        Document = "{}",
        Version = 1,
        CreatedAt = RecordedAt,
        UpdatedAt = RecordedAt,
    };

    /// <summary>Stores one message under the account and records a withdrawn category, a declared one, and a default on it.</summary>
    private static Task<StoredEmailId> StoreCategorizedMessageAsync(
        OrchestratedMailFathomServices services,
        Guid account,
        CancellationToken cancellationToken) =>
        StoreCategorizedMessageAsync(services, account, FolderAlias, MessageUid, cancellationToken);

    /// <summary>Stores one message in the named folder under its own UID and records the same three categories on it.</summary>
    private static async Task<StoredEmailId> StoreCategorizedMessageAsync(
        OrchestratedMailFathomServices services,
        Guid account,
        string folderAlias,
        uint messageUid,
        CancellationToken cancellationToken)
    {
        var accountId = MailAccountId.Create(account.ToString("D"));
        var binding = await OrchestratedFolderBinding.CommitAsync(services, accountId, folderAlias, folderAlias, cancellationToken);
        var occurrence = SyntheticEmail.OccurrenceIn(accountId, binding, messageUid);
        var message = await StoredSyntheticEmail.MetadataOnlyAsync(services, occurrence, folderAlias, cancellationToken);

        Assert.Equal(
            PersistenceCommitResult.Committed,
            await services.CommitAsync(
                async (_, session, token) =>
                {
                    var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, token);

                    context.EmailCategories.AddRange(
                        Record(message, EmailCategoryKind.Own, Withdrawn),
                        Record(message, EmailCategoryKind.Own, StillDeclared),
                        Record(message, EmailCategoryKind.Default, SwitchedOffDefault));

                    await context.SaveChangesAsync(token);
                },
                cancellationToken));

        return message;
    }

    private static EmailCategoryEntity Record(StoredEmailId message, EmailCategoryKind kind, string category) => new()
    {
        StoredEmailId = message.Value,
        Kind = kind,
        Category = category,
        Probability = 0.9,
        Source = EmailCategorySource.Model,
        Origin = "provider/build",
        QuestionRevision = "abc123",
    };

    /// <summary>Reads the categories a message still holds, ordinally so the assertion does not depend on the database's collation.</summary>
    private static async Task<string[]> ReadCategoriesAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId message,
        CancellationToken cancellationToken)
    {
        var categories = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .EmailCategories
                .AsNoTracking()
                .Where(record => record.StoredEmailId == message.Value)
                .Select(record => record.Category)
                .ToListAsync(token),
            cancellationToken);

        return [.. categories.Order(StringComparer.Ordinal)];
    }

    /// <summary>Deletes the accounts a test seeded.</summary>
    /// <remarks>Uncancellable because it runs in a <c>finally</c>, where the test's own token may already be cancelled.</remarks>
    private static async Task RemoveAccountsAsync(OrchestratedMailFathomServices services, params Guid[] accounts) =>
        await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .MailAccountRecords
                .Where(account => accounts.Contains(account.Id))
                .ExecuteDeleteAsync(token),
            CancellationToken.None);
}
