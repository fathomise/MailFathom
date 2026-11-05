// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Rules.Actions;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Rules.Preview;

/// <summary>How a preview ended.</summary>
public enum MailRulePreviewOutcome
{
    /// <summary>The rule was evaluated over the page of mail the result describes.</summary>
    Previewed = 0,

    /// <summary>The candidate failed validation, so nothing was evaluated.</summary>
    Refused = 1,

    /// <summary>The stored rule the request named is not one the account has.</summary>
    RuleNotFound = 2,
}

/// <summary>Which stored messages a rule would match, what each would be asked for, and how far into the mail the page reached.</summary>
/// <param name="Outcome">How the preview ended.</param>
/// <param name="Matches">The messages the rule matched, in identity order, at most <see cref="MailRulePreview.MaximumReportedMatches" />.</param>
/// <param name="Requested">What a match would ask the mailbox for, which is the same for every message the rule matches.</param>
/// <param name="ExaminedEmailCount">How many messages the page evaluated the rule against.</param>
/// <param name="FailedEmailCount">How many of them the condition produced no answer for.</param>
/// <param name="ContinueAfter">The message the next page continues after, or <see langword="null" /> when the mail is exhausted.</param>
/// <param name="Errors">Why the candidate was refused, empty unless <paramref name="Outcome" /> is <see cref="MailRulePreviewOutcome.Refused" />.</param>
public sealed record MailRulePreviewResult(
    MailRulePreviewOutcome Outcome,
    IReadOnlyList<StoredEmailId> Matches,
    IReadOnlyList<MailRuleAction> Requested,
    int ExaminedEmailCount,
    int FailedEmailCount,
    StoredEmailId? ContinueAfter,
    IReadOnlyList<string> Errors)
{
    /// <summary>A preview that evaluated nothing.</summary>
    /// <param name="outcome">Why.</param>
    /// <param name="errors">The defects of a refused candidate.</param>
    /// <returns>The result.</returns>
    internal static MailRulePreviewResult Unevaluated(MailRulePreviewOutcome outcome, IReadOnlyList<string> errors) =>
        new(outcome, [], [], 0, 0, ContinueAfter: null, errors);
}
