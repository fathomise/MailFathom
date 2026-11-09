// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Categories;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Categories;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Synchronization;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.Infrastructure.Persistence.Users.AccountSettings;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    private static async Task<StoredEmailId> StoreCategorizedMessageAsync(
        OrchestratedMailFathomServices services,
        Guid account,
        CancellationToken cancellationToken)
    {
        var accountId = MailAccountId.Create(account.ToString("D"));
        var binding = await OrchestratedFolderBinding.CommitAsync(services, accountId, FolderAlias, FolderAlias, cancellationToken);
        var occurrence = SyntheticEmail.OccurrenceIn(accountId, binding, MessageUid);
        var message = await StoredSyntheticEmail.MetadataOnlyAsync(services, occurrence, FolderAlias, cancellationToken);

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
