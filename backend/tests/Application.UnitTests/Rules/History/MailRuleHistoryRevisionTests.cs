// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.History;
using MailFathom.Application.Rules.Sets;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules.History;

/// <summary>Covers resolving the rule set an execution ran under, which is what lets a reader see the rule as it stood then.</summary>
public sealed class MailRuleHistoryRevisionTests
{
    private static readonly MailAccountId Account = AccessAuthorizations.ScopedAccount;
    private static readonly MailRuleSetRevision Revision = MailRuleSetRevision.Restore("a1b2c3d4e5f6");
    private static readonly DateTimeOffset EvaluatedAt = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    private readonly IMailRuleExecutionStore executions = Substitute.For<IMailRuleExecutionStore>();
    private readonly IMailRuleSetStore ruleSets = Substitute.For<IMailRuleSetStore>();

    public static TheoryData<AssignmentScope> ScopesCoveringTheAccount => [.. AccessAuthorizations.ScopesCoveringTheirTarget];

    public static TheoryData<AssignmentScope> ScopesOutsideTheAccount => [.. AccessAuthorizations.ScopesOutsideTheirTarget];

    /// <summary>A revision can return after a later write, so the archive is asked for the set as it stood when the execution ran.</summary>
    [Fact]
    public async Task ResolveRevisionAsync_AnArchivedRevision_ResolvesTheEntryCommittedByTheTimeTheExecutionRan()
    {
        // Arrange
        var entry = ArchivedAt(EvaluatedAt.AddDays(-2));
        this.ruleSets
            .FindArchivedAsync(Account, Revision, EvaluatedAt, Arg.Any<CancellationToken>())
            .Returns(entry);
        var history = this.HistoryFor(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminAuditRead));

        // Act
        var resolved = await history.ResolveRevisionAsync(Execution(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(entry, resolved);
    }

    /// <summary>The archive ages out under the retention window, so an execution older than its entry resolves to nothing.</summary>
    [Fact]
    public async Task ResolveRevisionAsync_ARevisionTheArchiveNoLongerHolds_ResolvesNothing()
    {
        // Arrange
        this.ruleSets
            .FindArchivedAsync(Account, Revision, EvaluatedAt, Arg.Any<CancellationToken>())
            .Returns((MailRuleSetArchiveEntry?)null);
        var history = this.HistoryFor(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminAuditRead));

        // Act
        var resolved = await history.ResolveRevisionAsync(Execution(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(resolved);
    }

    /// <summary>The rules that ran over somebody's mail are part of the record of it, so the state read grants nothing towards them.</summary>
    [Fact]
    public async Task ResolveRevisionAsync_ACallerGrantedOnlyTheAdministrativeRead_IsRefusedBeforeTheArchiveIsRead()
    {
        // Arrange
        var history = this.HistoryFor(AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            history.ResolveRevisionAsync(Execution(), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminAuditRead, refusal.RequiredPermission);
        await this.ruleSets.DidNotReceive().FindArchivedAsync(
            Arg.Any<MailAccountId>(),
            Arg.Any<MailRuleSetRevision>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [MemberData(nameof(ScopesCoveringTheAccount))]
    public async Task ResolveRevisionAsync_AnAdministratorScopedOverTheAccount_IsServed(AssignmentScope scope)
    {
        // Arrange
        var history = this.HistoryFor(AccessAuthorizations.ForAdministratorScopedAt(scope, MailFathomPermission.AdminAuditRead));

        // Act
        await history.ResolveRevisionAsync(Execution(), TestContext.Current.CancellationToken);

        // Assert
        await this.ruleSets.Received(1).FindArchivedAsync(Account, Revision, EvaluatedAt, Arg.Any<CancellationToken>());
    }

    [Theory]
    [MemberData(nameof(ScopesOutsideTheAccount))]
    public async Task ResolveRevisionAsync_AnAdministratorScopedElsewhere_IsRefusedBeforeTheArchiveIsRead(AssignmentScope scope)
    {
        // Arrange
        var history = this.HistoryFor(AccessAuthorizations.ForAdministratorScopedAt(scope, MailFathomPermission.AdminAuditRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            history.ResolveRevisionAsync(Execution(), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminAuditRead, refusal.RequiredPermission);
        await this.ruleSets.DidNotReceive().FindArchivedAsync(
            Arg.Any<MailAccountId>(),
            Arg.Any<MailRuleSetRevision>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    private static MailRuleExecution Execution() => new()
    {
        Id = MailRuleExecutionId.New(),
        Account = Account,
        StoredEmailId = StoredEmailId.Create(Guid.CreateVersion7()),
        RuleName = "file-invoices",
        Revision = Revision,
        Trigger = MailRuleExecutionTrigger.Arrival,
        Outcome = MailRuleOutcome.Matched,
        ReadFacts = [],
        Actions = [],
        EvaluatedAt = EvaluatedAt,
        Duration = TimeSpan.FromMilliseconds(1),
    };

    private static MailRuleSetArchiveEntry ArchivedAt(DateTimeOffset committedAt) => new(
        Account,
        Version: 4,
        Revision,
        ReplacedRevision: MailRuleSetRevision.Create([]),
        Actor: null,
        MailRuleChangeSurface.Administration,
        committedAt,
        Declarations: []);

    private MailRuleHistory HistoryFor(AccessAuthorization authorization) => new(this.executions, this.ruleSets, authorization);
}
