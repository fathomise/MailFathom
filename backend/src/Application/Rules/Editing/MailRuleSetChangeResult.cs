// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Rules.Editing;

/// <summary>How a write to an account's rules ended.</summary>
public enum MailRuleSetChangeOutcome
{
    /// <summary>The write committed, moved the version by one, and appended one archive row.</summary>
    Committed = 0,

    /// <summary>The set's version was not the one the write stated it read; nothing changed, and the caller reads the set again.</summary>
    VersionConflict = 1,

    /// <summary>The candidate set failed validation; nothing changed.</summary>
    Refused = 2,

    /// <summary>The write named a rule the account's set does not carry; nothing changed.</summary>
    RuleNotFound = 3,
}

/// <summary>What a write to an account's rules did, and the set's version and revision afterwards.</summary>
/// <param name="Outcome">How the write ended.</param>
/// <param name="Version">The set's version after the write: the new one when it committed, the current one when it did not.</param>
/// <param name="Revision">The set's revision after the write, in the same sense.</param>
/// <param name="Rule">The rule the write was about, which for a create is the identifier it was minted with.</param>
/// <param name="Errors">Why the candidate was refused, empty unless <paramref name="Outcome" /> is <see cref="MailRuleSetChangeOutcome.Refused" />.</param>
/// <remarks>
/// A result rather than an exception, because the immediate caller is a surface that answers each outcome differently —
/// a version conflict is a <c>409</c> the caller resolves by reading again, a refusal is a list of defects to fix — and
/// none of them is exceptional for a list a person edits by hand.
/// </remarks>
public sealed record MailRuleSetChangeResult(
    MailRuleSetChangeOutcome Outcome,
    long Version,
    MailRuleSetRevision Revision,
    MailRuleId Rule,
    IReadOnlyList<string> Errors);
