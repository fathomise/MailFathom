// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Persistence;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Sets;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Rules.Editing;

/// <summary>Writes one mail account's rules: create, replace, enable or disable, move, remove, and import a whole set.</summary>
/// <remarks>
/// <para>
/// Every write is one transaction that takes the set's row lock, refuses the write when the set's version is not the one
/// the caller stated it read, builds the whole set as it would stand afterwards, validates that candidate against the
/// set it replaces, and only then writes the rules, moves the version by one, and appends the archive row naming the
/// revision it produced, the revision it replaced, the actor, and the surface. A refused candidate changes no row, moves no version, and
/// appends nothing.
/// </para>
/// <para>
/// The lock serializes the writes to one account's rules across every replica and nothing else; a pass reading the set
/// takes no lock at all. It is never held across a network call: validation reads the account's folder mappings and
/// permissions in process and compiles the conditions, and nothing more.
/// </para>
/// <para>
/// A write naming an existing rule resolves its <see cref="MailRuleReference" /> against the locked set, after the
/// version check, so a name always means the rule the caller saw under it at the version they stated.
/// </para>
/// <para>
/// The administrative surface is the one that writes here, under <see cref="MailFathomPermission.AdminConfigurationWrite" />
/// over the account — the permission that already writes the account's record and its <c>RuleActions</c>.
/// </para>
/// </remarks>
public sealed class MailRuleSetEditor
{
    private readonly IMailRuleSetStore store;
    private readonly MailRuleSetValidator validator;
    private readonly MailRuleConditionBounds bounds;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly AccessAuthorization authorization;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the editor.</summary>
    /// <param name="store">Keeps each account's rules.</param>
    /// <param name="validator">Judges a candidate set as a whole.</param>
    /// <param name="bounds">The condition bounds in force.</param>
    /// <param name="commitPolicy">Runs the write as one transaction.</param>
    /// <param name="authorization">Answers which principal reached this use case and over which accounts.</param>
    /// <param name="timeProvider">Supplies the instant a rule is created and a write commits at.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public MailRuleSetEditor(
        IMailRuleSetStore store,
        MailRuleSetValidator validator,
        MailRuleConditionBounds bounds,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        AccessAuthorization authorization,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.store = store;
        this.validator = validator;
        this.bounds = bounds;
        this.commitPolicy = commitPolicy;
        this.authorization = authorization;
        this.timeProvider = timeProvider;
    }

    /// <summary>Creates a rule at the end of the account's set, disabled.</summary>
    /// <param name="write">The account and the version the caller read.</param>
    /// <param name="draft">What the rule declares.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the write did, naming the identifier the rule was minted with.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller may not write the account's configuration.</exception>
    /// <remarks>A created rule is always disabled, so it can be previewed before it acts on anybody's mail; enabling it is a write of its own.</remarks>
    public Task<MailRuleSetChangeResult> CreateAsync(
        MailRuleSetWrite write,
        MailRuleDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var created = draft.Declare(MailRuleId.Mint(this.timeProvider.GetUtcNow()), enabled: false);

        return this.WriteAsync(
            write,
            stored => new Proposal(created, Candidate.Of([.. stored.Declarations, created])),
            cancellationToken);
    }

    /// <summary>Replaces what one rule declares, keeping its identity, its position, and whether it runs.</summary>
    /// <param name="write">The account and the version the caller read.</param>
    /// <param name="rule">The rule to replace.</param>
    /// <param name="draft">What the rule declares from now on; renaming a rule is a replacement that changes only the name.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the write did.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller may not write the account's configuration.</exception>
    public Task<MailRuleSetChangeResult> ReplaceAsync(
        MailRuleSetWrite write,
        MailRuleReference rule,
        MailRuleDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return this.WriteAsync(
            write,
            rule,
            (stored, current) =>
            {
                var replaced = draft.Declare(current.Id, current.Enabled);

                return new Proposal(replaced, Candidate.Of([.. stored.Declarations.Select(declaration => declaration.Id == current.Id ? replaced : declaration)]));
            },
            cancellationToken);
    }

    /// <summary>Enables or disables one rule.</summary>
    /// <param name="write">The account and the version the caller read.</param>
    /// <param name="rule">The rule.</param>
    /// <param name="enabled">Whether the rule runs from the next pass on.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the write did.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller may not write the account's configuration.</exception>
    public Task<MailRuleSetChangeResult> SetEnabledAsync(
        MailRuleSetWrite write,
        MailRuleReference rule,
        bool enabled,
        CancellationToken cancellationToken) =>
        this.WriteAsync(
            write,
            rule,
            (stored, current) => new Proposal(
                current,
                Candidate.Of([.. stored.Declarations.Select(declaration => declaration.Id == current.Id ? declaration with { Enabled = enabled } : declaration)])),
            cancellationToken);

    /// <summary>Moves one rule to another position in the order a pass evaluates the set in.</summary>
    /// <param name="write">The account and the version the caller read.</param>
    /// <param name="rule">The rule.</param>
    /// <param name="position">Its new position, from <c>0</c> for the first rule evaluated; one past the last is refused.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the write did, refused when the position is outside the set.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller may not write the account's configuration.</exception>
    public Task<MailRuleSetChangeResult> MoveAsync(
        MailRuleSetWrite write,
        MailRuleReference rule,
        int position,
        CancellationToken cancellationToken) =>
        this.WriteAsync(
            write,
            rule,
            (stored, current) => new Proposal(current, Move(stored.Declarations, current, position)),
            cancellationToken);

    /// <summary>Removes one rule; the rules below it move up one position, and its schedule is withdrawn.</summary>
    /// <param name="write">The account and the version the caller read.</param>
    /// <param name="rule">The rule.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the write did.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller may not write the account's configuration.</exception>
    /// <remarks>
    /// Nothing keeps a tombstone: the archive is the only place that keeps what the rule was, for as long as its rows are
    /// retained, and executions already recorded keep naming the revision and the identifier they ran under.
    /// </remarks>
    public Task<MailRuleSetChangeResult> RemoveAsync(
        MailRuleSetWrite write,
        MailRuleReference rule,
        CancellationToken cancellationToken) =>
        this.WriteAsync(
            write,
            rule,
            (stored, current) => new Proposal(
                current,
                Candidate.Of([.. stored.Declarations.Where(declaration => declaration.Id != current.Id)])),
            cancellationToken);

    /// <summary>Brings a whole set of rules into the account, replacing its rules or merging into them, as one write.</summary>
    /// <param name="write">The account and the version the caller read.</param>
    /// <param name="import">The rules and the mode.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the import did to each rule, or why it was refused.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller may not write the account's configuration.</exception>
    /// <remarks>
    /// <para>
    /// An imported rule is matched to the account's rule of the same name, without regard to case. A matched rule keeps its
    /// identifier and whether it runs, as an edit keeps them; an unmatched one is created, disabled, as every create is. A
    /// replacement then removes every rule the import does not name, and orders the set as the import does; a merge keeps
    /// them, replaces each matched rule where it stands, and adds the rest at the end in the imported order.
    /// </para>
    /// <para>
    /// The whole resulting set is one candidate, judged as every write's is, so one defective rule refuses the import and
    /// nothing of it is written. An import naming one rule twice is refused before anything is matched, because which of
    /// the two was meant is the file's to say.
    /// </para>
    /// </remarks>
    public async Task<MailRuleSetImportResult> ImportAsync(
        MailRuleSetWrite write,
        MailRuleSetImport import,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(import);

        await this.authorization.RequirePermissionOverAsync(
            MailFathomPermission.AdminConfigurationWrite,
            write.Account,
            cancellationToken);

        var actor = this.authorization.PrincipalUser;

        return await this.commitPolicy.CommitAsync(
            async (session, attemptCancellationToken) =>
            {
                var stored = await this.store.LockAsync(session, write.Account, attemptCancellationToken);

                if (stored.Version != write.ExpectedVersion)
                {
                    return ImportUnchanged(MailRuleSetChangeOutcome.VersionConflict, stored, [], []);
                }

                var plan = this.PlanImport(write.Account, stored, import);

                if (plan.Errors.Count > 0)
                {
                    return ImportUnchanged(MailRuleSetChangeOutcome.Refused, stored, plan.Rules, plan.Errors);
                }

                var committed = this.Archive(write, stored, plan.Candidate, actor);

                await this.store.CommitAsync(session, committed, attemptCancellationToken);

                return new MailRuleSetImportResult(
                    MailRuleSetChangeOutcome.Committed,
                    committed.Version,
                    committed.Revision,
                    plan.Rules,
                    []);
            },
            cancellationToken);
    }

    /// <summary>Reports what an import would do to the account's rules, judging it exactly as the import would, and writes nothing.</summary>
    /// <param name="write">The account and the version the caller read; a preview against a stale version is a conflict, as the import would be.</param>
    /// <param name="import">The rules and the mode.</param>
    /// <param name="cancellationToken">Cancels the preview.</param>
    /// <returns>What the import would do to each rule, or why it would be refused.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller may not read the account's rules.</exception>
    /// <remarks>
    /// Under the read grant, because a preview tells the caller nothing beyond the account's stored rules and the verdict on
    /// a candidate they wrote, and it reads no mail. It takes no lock and opens no transaction.
    /// </remarks>
    public async Task<MailRuleSetImportResult> PreviewImportAsync(
        MailRuleSetWrite write,
        MailRuleSetImport import,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(import);

        await this.authorization.RequirePermissionOverAsync(
            MailFathomPermission.AdminRead,
            write.Account,
            cancellationToken);

        var stored = await this.store.ReadAsync(write.Account, cancellationToken);

        if (stored.Version != write.ExpectedVersion)
        {
            return ImportUnchanged(MailRuleSetChangeOutcome.VersionConflict, stored, [], []);
        }

        var plan = this.PlanImport(write.Account, stored, import);

        return ImportUnchanged(
            plan.Errors.Count > 0 ? MailRuleSetChangeOutcome.Refused : MailRuleSetChangeOutcome.Previewed,
            stored,
            plan.Rules,
            plan.Errors);
    }

    private static MailRuleSetImportResult ImportUnchanged(
        MailRuleSetChangeOutcome outcome,
        StoredMailRuleSet stored,
        IReadOnlyList<MailRuleImportDifference> rules,
        IReadOnlyList<string> errors) =>
        new(outcome, stored.Version, stored.Revision, rules, errors);

    private static string ImportLabel(int index) => $"#{index + 1}";

    private static IEnumerable<string> FindRepeatedImportNames(IReadOnlyList<MailRuleDraft> rules) => rules
        .Select((draft, index) => (Name: draft.Name?.Trim() ?? string.Empty, Label: ImportLabel(index)))
        .Where(entry => entry.Name.Length > 0)
        .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
        .Where(group => group.Count() > 1)
        .Select(group => $"Rules {string.Join(" and ", group.Select(entry => entry.Label))} of the import carry one name, and a name is unique within an account without regard to case.");

    /// <summary>Reports where a rule sits among the rules that are in both sets, which a removal or an addition alone does not change.</summary>
    private static Dictionary<MailRuleId, int> PositionsAmong(IEnumerable<MailRuleDeclaration> rules, HashSet<MailRuleId> kept) => rules
        .Where(rule => kept.Contains(rule.Id))
        .Select((rule, position) => (rule.Id, position))
        .ToDictionary(entry => entry.Id, entry => entry.position);

    /// <summary>Matches the import against the stored set, builds the candidate it produces, and judges it.</summary>
    private ImportPlan PlanImport(MailAccountId account, StoredMailRuleSet stored, MailRuleSetImport import)
    {
        var repeated = FindRepeatedImportNames(import.Rules).ToArray();

        if (repeated.Length > 0)
        {
            return new ImportPlan([], [], repeated);
        }

        var now = this.timeProvider.GetUtcNow();
        var storedByName = stored.Declarations.ToDictionary(rule => rule.Name?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        var imported = import.Rules
            .Select(draft => storedByName.GetValueOrDefault(draft.Name?.Trim() ?? string.Empty) is { } current
                ? new ImportedRule(draft.Declare(current.Id, current.Enabled), current)
                : new ImportedRule(draft.Declare(MailRuleId.Mint(now), enabled: false), Current: null))
            .ToArray();
        var replacements = imported
            .Where(rule => rule.Current is not null)
            .ToDictionary(rule => rule.Current!.Id, rule => rule.Declaration);

        IReadOnlyList<MailRuleDeclaration> candidate = import.Mode == MailRuleSetImportMode.Replace
            ? [.. imported.Select(rule => rule.Declaration)]
            :
            [
                .. stored.Declarations.Select(rule => replacements.GetValueOrDefault(rule.Id) ?? rule),
                .. imported.Where(rule => rule.Current is null).Select(rule => rule.Declaration),
            ];

        var kept = replacements.Keys.ToHashSet();
        var storedPositions = PositionsAmong(stored.Declarations, kept);
        var candidatePositions = PositionsAmong(candidate, kept);
        var removed = import.Mode == MailRuleSetImportMode.Replace
            ? stored.Declarations.Where(rule => !kept.Contains(rule.Id))
            : [];

        IReadOnlyList<MailRuleImportDifference> differences =
        [
            .. imported.Select(rule => new MailRuleImportDifference(
                rule.Current switch
                {
                    null => MailRuleImportEffect.Added,
                    { } current when !MailRuleSetValidator.HasSameContent(rule.Declaration, current) => MailRuleImportEffect.Changed,
                    { } current when storedPositions[current.Id] != candidatePositions[current.Id] => MailRuleImportEffect.Moved,
                    _ => MailRuleImportEffect.Unchanged,
                },
                rule.Declaration.Id,
                rule.Declaration.Name,
                rule.Declaration.Enabled)),
            .. removed.Select(rule => new MailRuleImportDifference(MailRuleImportEffect.Removed, rule.Id, rule.Name, rule.Enabled)),
        ];

        var labels = imported.Select((rule, index) => (Id: rule.Declaration.Id.ToString(), Label: ImportLabel(index))).ToArray();
        IReadOnlyList<string> errors =
        [
            .. this.validator
                .FindErrors(account, stored.Declarations, candidate, this.bounds)
                .Select(error => labels.Aggregate(error, (text, label) => text.Replace(label.Id, label.Label, StringComparison.Ordinal))),
        ];

        return new ImportPlan(candidate, differences, errors);
    }

    /// <summary>Builds the order a move produces, refusing a position outside the set.</summary>
    private static Candidate Move(
        IReadOnlyList<MailRuleDeclaration> declarations,
        MailRuleDeclaration moved,
        int position)
    {
        if (position < 0 || position >= declarations.Count)
        {
            return Candidate.Refused(
                $"Rule {moved.Id} cannot be moved to position {position}; the set's positions run from 0 to {declarations.Count - 1}.");
        }

        var reordered = declarations.Where(declaration => declaration.Id != moved.Id).ToList();

        reordered.Insert(position, moved);

        return Candidate.Of(reordered);
    }

    private static MailRuleSetChangeResult Unchanged(
        MailRuleSetChangeOutcome outcome,
        StoredMailRuleSet stored,
        MailRuleDeclaration? rule,
        IReadOnlyList<string> errors) =>
        new(outcome, stored.Version, stored.Revision, rule?.Id ?? default, rule?.Name ?? string.Empty, errors);

    private Task<MailRuleSetChangeResult> WriteAsync(
        MailRuleSetWrite write,
        MailRuleReference rule,
        Func<StoredMailRuleSet, MailRuleDeclaration, Proposal> change,
        CancellationToken cancellationToken) =>
        this.WriteAsync(
            write,
            stored => rule.FindIn(stored.Declarations) is { } current
                ? change(stored, current)
                : new Proposal(Subject: null, Candidate.RuleNotFound),
            cancellationToken);

    private async Task<MailRuleSetChangeResult> WriteAsync(
        MailRuleSetWrite write,
        Func<StoredMailRuleSet, Proposal> change,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);

        await this.authorization.RequirePermissionOverAsync(
            MailFathomPermission.AdminConfigurationWrite,
            write.Account,
            cancellationToken);

        var actor = this.authorization.PrincipalUser;

        return await this.commitPolicy.CommitAsync(
            async (session, attemptCancellationToken) =>
            {
                var stored = await this.store.LockAsync(session, write.Account, attemptCancellationToken);

                if (stored.Version != write.ExpectedVersion)
                {
                    return Unchanged(MailRuleSetChangeOutcome.VersionConflict, stored, rule: null, []);
                }

                var (subject, proposed) = change(stored);

                if (proposed.Declarations is not { } candidate)
                {
                    return proposed.Refusal is { } refusal
                        ? Unchanged(MailRuleSetChangeOutcome.Refused, stored, subject, [refusal])
                        : Unchanged(MailRuleSetChangeOutcome.RuleNotFound, stored, rule: null, []);
                }

                var errors = this.validator.FindErrors(write.Account, stored.Declarations, candidate, this.bounds);

                if (errors.Count > 0)
                {
                    return Unchanged(MailRuleSetChangeOutcome.Refused, stored, subject, errors);
                }

                var committed = this.Archive(write, stored, candidate, actor);

                await this.store.CommitAsync(session, committed, attemptCancellationToken);

                return new MailRuleSetChangeResult(
                    MailRuleSetChangeOutcome.Committed,
                    committed.Version,
                    committed.Revision,
                    subject!.Id,
                    subject.Name,
                    []);
            },
            cancellationToken);
    }

    /// <summary>Describes the committed write: the set as it stands afterwards, one version on, and who wrote it.</summary>
    private MailRuleSetArchiveEntry Archive(
        MailRuleSetWrite write,
        StoredMailRuleSet stored,
        IReadOnlyList<MailRuleDeclaration> candidate,
        UserId? actor) => new(
        write.Account,
        stored.Version + 1,
        MailRuleSetRevision.Create(candidate),
        stored.Revision,
        actor,
        MailRuleChangeSurface.Administration,
        this.timeProvider.GetUtcNow(),
        candidate);

    /// <summary>One imported rule as it would be stored, and the account's rule of the same name it replaces, if any.</summary>
    private sealed record ImportedRule(MailRuleDeclaration Declaration, MailRuleDeclaration? Current);

    /// <summary>The set an import produces, what it does to each rule, and why it is refused, empty when it is not.</summary>
    private sealed record ImportPlan(
        IReadOnlyList<MailRuleDeclaration> Candidate,
        IReadOnlyList<MailRuleImportDifference> Rules,
        IReadOnlyList<string> Errors);

    /// <summary>The rule a write is about, as it stands after the write, and the whole set the write proposes.</summary>
    private readonly record struct Proposal(MailRuleDeclaration? Subject, Candidate Candidate);

    /// <summary>The whole set a write proposes, or why it proposes none: the rule it names is not there, or a refusal.</summary>
    private readonly record struct Candidate(IReadOnlyList<MailRuleDeclaration>? Declarations, string? Refusal)
    {
        public static Candidate RuleNotFound { get; } = new(Declarations: null, Refusal: null);

        public static Candidate Refused(string refusal) => new(Declarations: null, refusal);

        public static Candidate Of(IReadOnlyList<MailRuleDeclaration> declarations) => new(declarations, Refusal: null);
    }
}
