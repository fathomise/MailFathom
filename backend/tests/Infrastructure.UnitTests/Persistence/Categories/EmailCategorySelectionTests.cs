// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Spam.Gating;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Categories;
using MailFathom.Domain.Delivery.Receipts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Categories;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Categories;

/// <summary>Covers which mail a model is asked about, which is the one place that decides what leaves the deployment.</summary>
/// <remarks>The predicate is evaluated by PostgreSQL, so what these establish is which rows it selects rather than what SQL it becomes.</remarks>
public sealed class EmailCategorySelectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);

    private static readonly MailFolderIdentity WorkInbox = new(MailAccountId.Create("work"), MailFolderAlias.Create("INBOX"));

    private static readonly MailFolderIdentity WorkJunk = new(MailAccountId.Create("work"), MailFolderAlias.Create("JUNK"));

    private static DerivedWorkAdmissionTerms ClassificationOff { get; } = new([], [], [], Now);

    private static DerivedWorkAdmissionTerms ClassificationOn { get; } = new(
        [MailAccountId.Create("work")],
        [],
        [WorkInbox],
        Now - TimeSpan.FromMinutes(15));

    [Fact]
    public void Selecting_AnArrivalNoModelHasBeenAskedAbout_SelectsIt()
    {
        // Arrange
        var email = Email();

        // Act & Assert
        Assert.Single(Select(email));
    }

    /// <summary>A setting never re-reads stored mail: a message some rule pass has already evaluated is no longer arriving.</summary>
    [Fact]
    public void Selecting_MailARulePassAlreadyEvaluated_LeavesItOut()
    {
        // Arrange
        var email = Email();
        email.RulesEvaluatedAt = Now;

        // Act & Assert
        Assert.Empty(Select(email));
    }

    [Fact]
    public void Selecting_MailAlreadyHoldingADefaultCategory_LeavesItOut()
    {
        // Arrange
        var email = Email();
        email.Categories.Add(new EmailCategoryEntity
        {
            StoredEmailId = email.Id,
            Kind = EmailCategoryKind.Default,
            Category = DefaultMailCategory.Finance.ToString(),
            Origin = "vendor/decider-1",
            QuestionRevision = "a",
        });

        // Act & Assert
        Assert.Empty(Select(email));
    }

    [Fact]
    public void Selecting_AFolderNotVisibleToAI_LeavesItsMailOut()
    {
        // Arrange
        var email = Email();

        // Act
        var selected = Select(email, visibleFolders: []);

        // Assert
        Assert.Empty(selected);
    }

    /// <summary>The account need not classify spam: categorization is switched on independently, and junk is never asked about either way.</summary>
    [Fact]
    public void Selecting_TheJunkFolderOfAnAccountThatDoesNotClassify_LeavesItsMailOut()
    {
        // Arrange
        var email = Email("JUNK");

        // Act
        var selected = Select(email, visibleFolders: [WorkInbox, WorkJunk], junkFolders: [WorkJunk]);

        // Assert
        Assert.Empty(selected);
    }

    [Fact]
    public void Selecting_MailWithASpamVerdict_LeavesItOutWhetherOrNotTheAccountClassifies()
    {
        // Arrange
        var email = Email();
        email.SpamClassification = Classified(email, SpamVerdict.Spam);

        // Act & Assert
        Assert.Empty(Select(email, terms: ClassificationOff));
        Assert.Empty(Select(email, terms: ClassificationOn));
    }

    [Fact]
    public void Selecting_MailOfAClassifyingAccountStillAwaitingItsVerdict_WaitsForIt()
    {
        // Arrange
        var email = Email();

        // Act & Assert
        Assert.Empty(Select(email, terms: ClassificationOn));
    }

    [Fact]
    public void Selecting_MailOfAClassifyingAccountWithANotSpamVerdict_SelectsIt()
    {
        // Arrange
        var email = Email();
        email.SpamClassification = Classified(email, SpamVerdict.NotSpam);

        // Act & Assert
        Assert.Single(Select(email, terms: ClassificationOn));
    }

    [Fact]
    public void Selecting_ADeliveryReport_LeavesItOut()
    {
        // Arrange
        var email = Email();
        email.ReportKind = ReceiptKind.Delivery;

        // Act & Assert
        Assert.Empty(Select(email));
    }

    [Fact]
    public void Selecting_TheCopyOfTheDeploymentsOwnOutgoingMail_LeavesItOut()
    {
        // Arrange
        var email = Email();
        email.FiledFromOutgoingEmailId = Guid.CreateVersion7();

        // Act & Assert
        Assert.Empty(Select(email));
    }

    [Fact]
    public void Selecting_MailWhoseContentIsNotStored_LeavesItOut()
    {
        // Arrange
        var email = Email();
        email.ContentAvailability = StoredEmailContentAvailability.ExceededSizeLimit;

        // Act & Assert
        Assert.Empty(Select(email));
    }

    [Fact]
    public void Selecting_AnotherAccountsMail_LeavesItOut()
    {
        // Arrange
        var email = Email();
        email.MailboxAccountId = "home";

        // Act & Assert
        Assert.Empty(Select(email));
    }

    /// <summary>
    /// The three custody phases differ only in whether a stored message still names a remote occurrence, which the selection does not
    /// read: a mirrored message has one, a held message's is gone after the drain, and a restoring message may have either.
    /// </summary>
    [Theory]
    [InlineData("Mirrored", true)]
    [InlineData("Held", false)]
    [InlineData("Restoring", false)]
    [InlineData("Restoring", true)]
    public void Selecting_AnArrivalInEveryCustodyPhase_SelectsItOnTheFolderItIsIn(string phase, bool namesARemoteOccurrence)
    {
        // Arrange
        Assert.False(string.IsNullOrEmpty(phase));
        var email = Email();

        if (namesARemoteOccurrence)
        {
            email.UidValidity = 7;
            email.Uid = 42;
        }
        else
        {
            email.UidValidity = null;
            email.Uid = null;
        }

        // Act & Assert
        Assert.Single(Select(email));
    }

    /// <summary>The queue reads as one command, which is what no in-memory selection above can show.</summary>
    [Fact]
    public void Selecting_TheArrivingMail_IsOneCommandPostgreSqlEvaluates()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        // Act
        var command = EmailCategoryStore
            .Selecting(context.StoredEmails.AsNoTracking(), "work", [WorkInbox], [WorkJunk], ClassificationOn)
            .ToQueryString();

        // Assert
        Assert.Contains(nameof(StoredEmailEntity.RulesEvaluatedAt), command, StringComparison.Ordinal);
    }

    /// <summary>The database picks each conversation's first message, so a reply never carries the conversation across the boundary.</summary>
    [Fact]
    public void FirstOfEachConversation_NamedConversations_NarrowsTheCommandToThem()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        // Act
        var command = EmailCategoryStore
            .FirstOfEachConversation(context.StoredEmails.AsNoTracking(), "work", [new Guid("11111111-1111-1111-1111-111111111111")])
            .ToQueryString();

        // Assert
        Assert.Contains($"\"{nameof(StoredEmailEntity.EmailThreadId)}\" = ANY", command, StringComparison.Ordinal);
    }

    /// <summary>A long message is cut by the database, so the rest of somebody's mail is never read to throw away.</summary>
    [Fact]
    public void Openings_AMessageOfAnyLength_AsksPostgreSqlForTheBoundedExcerptOnly()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        // Act
        var command = EmailCategoryStore
            .Openings(context.StoredEmails.AsNoTracking(), [new Guid("11111111-1111-1111-1111-111111111111")], [WorkInbox], [WorkJunk], ClassificationOn)
            .ToQueryString();

        // Assert
        Assert.Contains("substring(", command, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<StoredEmailEntity> Select(
        StoredEmailEntity email,
        IReadOnlyList<MailFolderIdentity>? visibleFolders = null,
        IReadOnlyList<MailFolderIdentity>? junkFolders = null,
        DerivedWorkAdmissionTerms? terms = null) =>
        [
            .. EmailCategoryStore.Selecting(
                new[] { email }.AsQueryable(),
                "work",
                visibleFolders ?? [WorkInbox],
                junkFolders ?? [],
                terms ?? ClassificationOff),
        ];

    private static EmailSpamClassificationEntity Classified(StoredEmailEntity email, SpamVerdict verdict) => new()
    {
        StoredEmailId = email.Id,
        Verdict = verdict,
        DecidedBy = SpamClassificationStage.Deterministic,
        EvaluatedAt = Now,
    };

    private static StoredEmailEntity Email(string alias = "INBOX") => new()
    {
        MailboxAccountId = "work",
        MailFolderBinding = new MailFolderBindingEntity
        {
            MailboxAccountId = "work",
            Alias = alias,
            RemotePath = alias,
            MailboxAccount = new MailboxAccountEntity { Id = "work" },
        },
        Folder = new MailFolderEntity
        {
            MailboxAccountId = "work",
            Name = alias,
            NameKey = alias,
            Alias = alias,
        },
        StoredAt = Now,
        ContentAvailability = StoredEmailContentAvailability.Available,
    };
}
