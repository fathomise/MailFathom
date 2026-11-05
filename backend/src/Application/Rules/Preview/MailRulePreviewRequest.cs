// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Rules.Editing;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Rules.Preview;

/// <summary>Which rule to preview over which account's stored mail, and where in that mail the preview continues from.</summary>
/// <remarks>Exactly one of <see cref="Candidate" /> and <see cref="StoredRule" /> names the rule.</remarks>
public sealed record MailRulePreviewRequest
{
    private MailRulePreviewRequest(MailAccountId account, MailRuleDraft? candidate, MailRuleId? storedRule, StoredEmailId? after)
    {
        this.Account = account;
        this.Candidate = candidate;
        this.StoredRule = storedRule;
        this.After = after;
    }

    /// <summary>Gets the account whose stored mail the rule is evaluated over, which must be one this deployment serves.</summary>
    public MailAccountId Account { get; }

    /// <summary>Gets the rule a person is writing and has not committed, or <see langword="null" /> when a stored rule is previewed.</summary>
    public MailRuleDraft? Candidate { get; }

    /// <summary>Gets the stored rule previewed, or <see langword="null" /> when a candidate is.</summary>
    public MailRuleId? StoredRule { get; }

    /// <summary>Gets the stored email the preview continues after, or <see langword="null" /> to start at the first.</summary>
    public StoredEmailId? After { get; }

    /// <summary>Previews a rule a person is writing.</summary>
    /// <param name="account">The account.</param>
    /// <param name="candidate">What the rule declares.</param>
    /// <param name="after">Where a previous page of the preview stopped, or <see langword="null" />.</param>
    /// <returns>The request.</returns>
    public static MailRulePreviewRequest OfCandidate(MailAccountId account, MailRuleDraft candidate, StoredEmailId? after = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        return new MailRulePreviewRequest(account, candidate, storedRule: null, after);
    }

    /// <summary>Previews a rule the account already stores, enabled or not.</summary>
    /// <param name="account">The account.</param>
    /// <param name="rule">The stored rule.</param>
    /// <param name="after">Where a previous page of the preview stopped, or <see langword="null" />.</param>
    /// <returns>The request.</returns>
    public static MailRulePreviewRequest OfStoredRule(MailAccountId account, MailRuleId rule, StoredEmailId? after = null) =>
        new(account, candidate: null, rule, after);
}
