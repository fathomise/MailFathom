// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Rules.Editing;

/// <summary>What an import into an account's rules did, or would do when it was a preview.</summary>
/// <param name="Outcome">
/// How the import ended: <see cref="MailRuleSetChangeOutcome.Committed" />, <see cref="MailRuleSetChangeOutcome.Previewed" />
/// for a preview that would commit, <see cref="MailRuleSetChangeOutcome.Refused" />, or
/// <see cref="MailRuleSetChangeOutcome.VersionConflict" />.
/// </param>
/// <param name="Version">The set's version after the import when it committed, and the version it stands at otherwise.</param>
/// <param name="Revision">The set's revision, in the same sense.</param>
/// <param name="Rules">
/// What the import does to each rule: every imported rule in the order it was imported in, then every rule a replacement
/// removes. Empty when the version was stale or the import names one rule twice.
/// </param>
/// <param name="Errors">
/// Why the import was refused, empty unless it was. An imported rule is named by its place in the import, from <c>#1</c>,
/// rather than by an identifier nothing stored.
/// </param>
public sealed record MailRuleSetImportResult(
    MailRuleSetChangeOutcome Outcome,
    long Version,
    MailRuleSetRevision Revision,
    IReadOnlyList<MailRuleImportDifference> Rules,
    IReadOnlyList<string> Errors);
