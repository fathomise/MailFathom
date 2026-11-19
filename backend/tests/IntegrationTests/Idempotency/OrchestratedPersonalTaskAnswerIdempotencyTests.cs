// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Tasks;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Tasks;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Idempotency;

/// <summary>Proves concurrent replies to one open wait close it once, decided by the write's own condition.</summary>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedPersonalTaskAnswerIdempotencyTests(MailFathomOrchestrationFixture orchestration)
{
    private const int ConcurrentAnswers = 6;

    private static readonly TimeSpan Warsaw = TimeSpan.FromHours(1);

    private static readonly DateTimeOffset WrittenAt = new(2031, 1, 2, 8, 0, 0, Warsaw);

    [Fact]
    public async Task AnswerAsync_ManyRepliesToOneOpenWait_ClosesItOnceWithOneStoredAnswer()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var waiting = PersonalTask.Compose(
            PersonalTaskId.Create(Guid.CreateVersion7()),
            services.ServedUser,
            "Hear back about the tender",
            dueOn: null,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null,
            TaskCommitment.Owed with
            {
                Direction = PersonalTaskDirection.Awaited,
                Anchor = TaskAnchor.Thread(EmailThreadId.Create(Guid.NewGuid()), "Tender"),
            },
            WrittenAt);
        var replies = Enumerable.Range(0, ConcurrentAnswers)
            .Select(ordinal => new TaskAnswer(
                StoredEmailId.Create(Guid.NewGuid()),
                new DateTimeOffset(2031, 5, 4, 9, ordinal, 0, Warsaw)))
            .ToArray();
        await services.InScopeAsync(
            async (scope, token) =>
            {
                await scope.GetRequiredService<IPersonalTaskStore>().AddAsync(waiting, token);

                return true;
            },
            cancellationToken);

        // Act
        var attempts = await ConcurrentIdempotency.RunAsync(
            $"{nameof(IPersonalTaskStore)}.{nameof(IPersonalTaskStore.AnswerAsync)}",
            ConcurrentAnswers,
            (ordinal, token) => services.InScopeAsync(
                (scope, inner) => scope.GetRequiredService<IPersonalTaskStore>()
                    .AnswerAsync(services.ServedUser, waiting.Id, replies[ordinal], inner),
                token),
            cancellationToken);
        var read = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IPersonalTaskStore>().FindAsync(services.ServedUser, waiting.Id, token),
            cancellationToken);

        // Assert
        attempts.AssertSingleEffect(attempts.Results.Count(static outcome => outcome == PersonalTaskChangeOutcome.Applied));
        Assert.Empty(attempts.Failures);
        Assert.Equal(
            ConcurrentAnswers - 1,
            attempts.Results.Count(static outcome => outcome == PersonalTaskChangeOutcome.NotFound));
        Assert.NotNull(read);
        Assert.True(read.IsCompleted);
        Assert.Contains(read.Answer, replies);
    }
}
