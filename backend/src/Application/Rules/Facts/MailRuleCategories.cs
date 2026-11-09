// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Collections.Frozen;
using MailFathom.Application.Categories;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Rules.Facts;

/// <summary>Reads what a batch of a rule pass needs to know about categories, for a walk whose rules name the fact.</summary>
/// <param name="offered">Narrows what is recorded to what each account shows.</param>
/// <param name="store">Answers which messages a category may still arrive for.</param>
/// <param name="settingsReader">Answers whether the account sorts its mail into categories.</param>
/// <param name="judge">Answers whether a decision model serves categorization at all.</param>
/// <remarks>
/// A message is waited for only where something would still answer. An account that does not categorize and a deployment
/// with no decision model never will, so a rule naming the fact there reads an empty set at once rather than holding the
/// mail for the length of the wait.
/// </remarks>
public sealed class MailRuleCategories(
    OfferedMailCategories offered,
    IEmailCategoryStore store,
    IEmailCategorizationSettingsReader settingsReader,
    IEmailCategoryJudge judge)
{
    /// <summary>Reads one batch's categories and which of its messages are still waited for.</summary>
    /// <param name="account">The account the batch belongs to.</param>
    /// <param name="emails">The batch.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The reading.</returns>
    public async Task<MailRuleCategoryReading> ReadAsync(
        MailAccountId account,
        IReadOnlyCollection<StoredEmailId> emails,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(emails);

        var shown = await offered.ReadShownAsync([.. emails.Select(email => (email, account))], cancellationToken);
        var awaited = settingsReader.SettingsFor(account).IsEnabled && judge.IsActive
            ? await store.ReadAwaitedAsync(account, emails, cancellationToken)
            : FrozenSet<StoredEmailId>.Empty;

        return new MailRuleCategoryReading(shown, awaited);
    }
}
