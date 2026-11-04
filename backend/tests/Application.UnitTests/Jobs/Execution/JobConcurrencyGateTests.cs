// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using Xunit;

namespace MailFathom.Application.UnitTests.Jobs.Execution;

public sealed class JobConcurrencyGateTests
{
    private const int MaxQueueDepthPerType = 100;

    /// <summary>The ceiling is what the instance may spend at once, so the job beyond it waits rather than starting.</summary>
    [Fact]
    public async Task AcquireAsync_AsManyJobsAsTheProcessCeiling_LeavesTheNextOneWaiting()
    {
        // Arrange
        using var gate = GateFor(maxConcurrentJobs: 2, maxConcurrentJobsPerType: 2);

        var first = await gate.AcquireAsync(JobType.ClassifyEmailSpam, CancellationToken.None);
        var second = await gate.AcquireAsync(JobType.ClassifyEmailSpam, CancellationToken.None);

        // Act
        var beyondTheCeiling = gate.AcquireAsync(JobType.ClassifyEmailSpam, CancellationToken.None);

        // Assert
        Assert.False(beyondTheCeiling.IsCompleted);

        second.Dispose();

        var admitted = await beyondTheCeiling;

        Assert.NotNull(admitted);

        admitted.Dispose();
        first.Dispose();
    }

    /// <summary>
    /// The per-type ceiling is a bound of its own rather than a share of the process ceiling, so it stops a job while
    /// the instance still has room — which is the room another type's work runs in.
    /// </summary>
    [Fact]
    public async Task AcquireAsync_AsManyJobsOfOneTypeAsItsCeiling_LeavesTheNextOneWaitingWhileTheProcessHasRoom()
    {
        // Arrange
        using var gate = GateFor(maxConcurrentJobs: 3, maxConcurrentJobsPerType: 1);

        var held = await gate.AcquireAsync(JobType.ClassifyEmailSpam, CancellationToken.None);

        // Act
        var beyondTheTypeCeiling = gate.AcquireAsync(JobType.ClassifyEmailSpam, CancellationToken.None);

        // Assert
        Assert.False(beyondTheTypeCeiling.IsCompleted);

        held.Dispose();

        var admitted = await beyondTheTypeCeiling;

        Assert.NotNull(admitted);

        admitted.Dispose();
    }

    /// <summary>
    /// A type sitting at its own ceiling is no reason for another type to wait, because the job queued behind that
    /// ceiling has not taken any of the room the process still has.
    /// </summary>
    [Fact]
    public async Task AcquireAsync_OneTypeAtItsCeilingWithAJobWaiting_AdmitsAnotherTypeIntoTheProcessCeiling()
    {
        // Arrange
        using var gate = GateFor(maxConcurrentJobs: 2, maxConcurrentJobsPerType: 1);

        var heldOfTheFloodedType = await gate.AcquireAsync(JobType.RederiveStoredMail, CancellationToken.None);
        var waitingOnItsTypeCeiling = gate.AcquireAsync(JobType.RederiveStoredMail, CancellationToken.None);

        // Act
        var ofAnotherType = gate.AcquireAsync(JobType.DispatchHeldSend, CancellationToken.None);

        // Assert
        Assert.False(waitingOnItsTypeCeiling.IsCompleted);
        Assert.True(ofAnotherType.IsCompletedSuccessfully);

        (await ofAnotherType).Dispose();
        heldOfTheFloodedType.Dispose();
        (await waitingOnItsTypeCeiling).Dispose();
    }

    /// <summary>
    /// Every job blocked on its own type's ceiling holds none of the process ceiling, so the whole of what the process
    /// has left goes to other types — and no more than that, since the process ceiling still binds them.
    /// </summary>
    [Fact]
    public async Task AcquireAsync_JobsWaitingOnTheirTypeCeiling_HoldNoneOfTheProcessCeiling()
    {
        // Arrange
        using var gate = GateFor(maxConcurrentJobs: 3, maxConcurrentJobsPerType: 1);

        var heldOfTheFloodedType = await gate.AcquireAsync(JobType.RederiveStoredMail, CancellationToken.None);
        Task<IDisposable>[] waitingOnTheirTypeCeiling =
        [
            gate.AcquireAsync(JobType.RederiveStoredMail, CancellationToken.None),
            gate.AcquireAsync(JobType.RederiveStoredMail, CancellationToken.None),
        ];

        // Act
        Task<IDisposable>[] ofOtherTypes =
        [
            gate.AcquireAsync(JobType.DispatchHeldSend, CancellationToken.None),
            gate.AcquireAsync(JobType.RunScheduledMailRules, CancellationToken.None),
        ];
        var beyondTheProcessCeiling = gate.AcquireAsync(JobType.ClassifyStoredEmailSpam, CancellationToken.None);

        // Assert
        Assert.All(waitingOnTheirTypeCeiling, waiting => Assert.False(waiting.IsCompleted));
        Assert.All(ofOtherTypes, admitted => Assert.True(admitted.IsCompletedSuccessfully));
        Assert.False(beyondTheProcessCeiling.IsCompleted);

        foreach (var admitted in await Task.WhenAll(ofOtherTypes))
        {
            admitted.Dispose();
        }

        (await beyondTheProcessCeiling).Dispose();
        heldOfTheFloodedType.Dispose();

        await GiveBackAsEachIsAdmittedAsync(waitingOnTheirTypeCeiling);
    }

    /// <summary>Capacity given back twice would let one more job past the ceiling than the deployment allows.</summary>
    [Fact]
    public async Task AcquireAsync_CapacityGivenBackTwice_StillAdmitsNoMoreThanTheCeiling()
    {
        // Arrange
        using var gate = GateFor(maxConcurrentJobs: 1, maxConcurrentJobsPerType: 1);

        var held = await gate.AcquireAsync(JobType.ClassifyEmailSpam, CancellationToken.None);

        // Act
        held.Dispose();
        held.Dispose();

        // Assert
        var readmitted = await gate.AcquireAsync(JobType.ClassifyEmailSpam, CancellationToken.None);
        var beyondTheCeiling = gate.AcquireAsync(JobType.ClassifyEmailSpam, CancellationToken.None);

        Assert.False(beyondTheCeiling.IsCompleted);

        readmitted.Dispose();

        var admitted = await beyondTheCeiling;

        admitted.Dispose();
    }

    /// <summary>The unspecified struct default names no type and has no slots, so asking for it is a defect rather than a wait.</summary>
    [Fact]
    public async Task AcquireAsync_AnUnspecifiedJobType_IsRefused()
    {
        // Arrange
        using var gate = GateFor(maxConcurrentJobs: 1, maxConcurrentJobsPerType: 1);

        // Act
        var refusal = await Record.ExceptionAsync(() => gate.AcquireAsync(default, CancellationToken.None));

        // Assert
        Assert.IsType<ArgumentException>(refusal);
    }

    /// <summary>A wait somebody abandoned leaves the ceilings where it found them, so an abandoned job costs no capacity.</summary>
    [Fact]
    public async Task AcquireAsync_AWaitAbandonedBeforeItWasAdmitted_LeavesTheCeilingUntouched()
    {
        // Arrange
        using var gate = GateFor(maxConcurrentJobs: 1, maxConcurrentJobsPerType: 1);
        using var abandonment = new CancellationTokenSource();

        var held = await gate.AcquireAsync(JobType.ClassifyEmailSpam, CancellationToken.None);
        var abandoned = gate.AcquireAsync(JobType.ClassifyEmailSpam, abandonment.Token);

        // Act
        await abandonment.CancelAsync();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);

        held.Dispose();

        var admitted = await gate.AcquireAsync(JobType.ClassifyEmailSpam, CancellationToken.None);

        Assert.NotNull(admitted);

        admitted.Dispose();
    }

    /// <summary>Gives back each waiting acquisition as it is admitted, in whatever order the gate admits them.</summary>
    private static async Task GiveBackAsEachIsAdmittedAsync(IEnumerable<Task<IDisposable>> acquisitions)
    {
        var pending = acquisitions.ToList();

        while (pending.Count > 0)
        {
            var admitted = await Task.WhenAny(pending);

            pending.Remove(admitted);
            (await admitted).Dispose();
        }
    }

    private static JobConcurrencyGate GateFor(int maxConcurrentJobs, int maxConcurrentJobsPerType) =>
        new(JobCapacitySettings.Create(maxConcurrentJobs, maxConcurrentJobsPerType, MaxQueueDepthPerType));
}
