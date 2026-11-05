// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Rules.Sets;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Rules.History;

/// <summary>Reads what one account's rules concluded about its mail, and what the rules said when they concluded it.</summary>
/// <remarks>
/// <para>
/// The store is written by every evaluation pass and aged by retention. This is the reading, separated so that the grant
/// is asked where the history is served: what a rule concluded about a message is derived from that message, which is
/// why it is published under the audit grant rather than under the one covering reports of the deployment's own state.
/// </para>
/// <para>
/// A recorded execution names the revision it ran under, and once a rule can be edited or removed while the deployment
/// runs, what that revision <em>said</em> has to outlive the edit. The archive keeps it, so a run stays explainable after
/// its rule was edited or removed, for as long as the archive row is retained beside the history.
/// </para>
/// <para>
/// Which rules an account has now is a different question and a different grant — <see cref="MailRuleSetReader" />
/// answers it — because a rule set says what this deployment will do and a history says what it did to somebody's mail.
/// </para>
/// </remarks>
public sealed class MailRuleHistory
{
    private readonly IMailRuleExecutionStore executions;
    private readonly IMailRuleSetStore ruleSets;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the reading over the recorded executions and the archive of every committed rule set.</summary>
    /// <param name="executions">Keeps what each evaluation concluded.</param>
    /// <param name="ruleSets">Keeps each account's rules and the archive of what they were.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailRuleHistory(
        IMailRuleExecutionStore executions,
        IMailRuleSetStore ruleSets,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(executions);
        ArgumentNullException.ThrowIfNull(ruleSets);
        ArgumentNullException.ThrowIfNull(authorization);

        this.executions = executions;
        this.ruleSets = ruleSets;
        this.authorization = authorization;
    }

    /// <summary>Reads one page of an account's rule history.</summary>
    /// <param name="query">The account, the filters, and where the page continues from.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, and the cursor the following one is asked with.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminAuditRead" /> at a scope covering the account.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    public async Task<MailRuleExecutionPage> ReadPageAsync(
        MailRuleExecutionQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        await this.authorization.RequirePermissionOverAsync(
            MailFathomPermission.AdminAuditRead,
            query.Account,
            cancellationToken);

        return await this.executions.ReadPageAsync(query, cancellationToken);
    }

    /// <summary>Resolves the revision a recorded execution ran under to the rules that revision declared.</summary>
    /// <param name="execution">The execution to explain.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The newest archive row of the execution's account carrying its revision and committed at or before it was
    /// evaluated, or <see langword="null" /> when the revision was never archived — an execution recorded before rules
    /// were stored per account — or the row has since been pruned.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="execution" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller is not granted <see cref="MailFathomPermission.AdminAuditRead" /> over the execution's account.</exception>
    public async Task<MailRuleSetArchiveEntry?> ResolveRevisionAsync(
        MailRuleExecution execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);

        await this.authorization.RequirePermissionOverAsync(
            MailFathomPermission.AdminAuditRead,
            execution.Account,
            cancellationToken);

        return await this.ruleSets.FindArchivedAsync(
            execution.Account,
            execution.Revision,
            execution.EvaluatedAt,
            cancellationToken);
    }
}
