// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Classification;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>The one whole-mailbox classification run an account may have, keyed by the account exactly as the table is.</summary>
internal sealed class InMemoryClassificationRunStore : IClassificationRunStore
{
    private readonly Dictionary<MailAccountId, ClassificationRun> runs = [];
    private readonly List<ClassificationRun> saves = [];

    /// <summary>Gets every state a run was saved in, which is what proves a batch committed the position it reached.</summary>
    internal IReadOnlyList<ClassificationRun> Saves => this.saves;

    /// <summary>Gets the run recorded for an account, whether or not it is still outstanding.</summary>
    /// <param name="account">The account to read, named as the user and the identifier together.</param>
    /// <returns>The run, or <see langword="null" /> when the account has never had one.</returns>
    internal ClassificationRun? Find(MailAccountId account) => this.runs.GetValueOrDefault(account);

    /// <summary>Puts a run in front of an account without going through the request path.</summary>
    /// <param name="run">The run to record.</param>
    internal void Arrange(ClassificationRun run) => this.runs[run.Account] = run;

    /// <inheritdoc />
    public Task<ClassificationRun?> FindOutstandingAsync(
        MailAccountId account,
        CancellationToken cancellationToken) =>
        Task.FromResult(this.runs.GetValueOrDefault(account) is { IsOutstanding: true } outstanding
            ? outstanding
            : null);

    /// <inheritdoc />
    public Task<ClassificationRun?> FindLatestAsync(
        MailAccountId account,
        CancellationToken cancellationToken) =>
        Task.FromResult(this.runs.GetValueOrDefault(account));

    /// <inheritdoc />
    public Task SaveAsync(IPersistenceSession session, ClassificationRun run, CancellationToken cancellationToken)
    {
        this.runs[run.Account] = run;
        this.saves.Add(run);

        return Task.CompletedTask;
    }
}
