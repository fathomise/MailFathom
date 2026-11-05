// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Rules.Conditions;

namespace MailFathom.Application.Rules;

/// <summary>One mail account's enabled rules, in the order they are evaluated, under one revision.</summary>
/// <remarks>
/// <para>
/// The order is the contract. Two rules matching one email must produce the same outcome on every run and on every
/// replica, so which of them is reached first is a property of the stored set rather than of anything observed while it
/// runs, and nothing here sorts, groups, or reorders what it was given.
/// </para>
/// <para>
/// Immutable, because a pass reads the account's set once when it starts and uses that instance for its duration. A
/// rule set changed while a pass is running finishes against the revision the pass began with, and the next pass reads
/// the new one.
/// </para>
/// <para>
/// The bounds travel with the set for that same reason: two of the three were already spent proving the conditions
/// usable, and the third bounds every evaluation the pass is about to make.
/// </para>
/// <para>
/// A set is <see cref="IsHeld">held</see> when one of its enabled rules no longer compiles under the bounds in force —
/// a bound lowered since the rule was written, or a fact a release withdrew. A held set runs none of its rules, because
/// an ordered, stop-when-matched set with one rule taken out is a set nobody wrote.
/// </para>
/// </remarks>
public sealed class MailRuleSet
{
    private MailRuleSet(
        IReadOnlyList<MailRule> rules,
        MailRuleSetRevision revision,
        MailRuleConditionBounds bounds,
        IReadOnlyList<MailRuleId> uncompiledRules)
    {
        this.Rules = rules;
        this.Revision = revision;
        this.Bounds = bounds;
        this.UncompiledRules = uncompiledRules;
    }

    /// <summary>Gets the enabled rules in evaluation order, empty for a held set.</summary>
    public IReadOnlyList<MailRule> Rules { get; }

    /// <summary>Gets the identity of this revision of the rule set.</summary>
    public MailRuleSetRevision Revision { get; }

    /// <summary>Gets the bounds the rules were read under and are evaluated under.</summary>
    public MailRuleConditionBounds Bounds { get; }

    /// <summary>Gets the enabled rules whose condition no longer compiles, which is what holds the set.</summary>
    public IReadOnlyList<MailRuleId> UncompiledRules { get; }

    /// <summary>Gets whether the set is held rather than run, because an enabled rule no longer compiles.</summary>
    public bool IsHeld => this.UncompiledRules.Count > 0;

    /// <summary>Gets whether the set enables any rule at all.</summary>
    public bool IsEmpty => this.Rules.Count == 0;

    /// <summary>Creates a rule set from rules that have already been proven usable.</summary>
    /// <param name="rules">The enabled rules, in evaluation order.</param>
    /// <param name="revision">The identity derived from the declarations the rules were compiled from.</param>
    /// <param name="bounds">The bounds the rules were read under.</param>
    /// <returns>The rule set a pass runs against.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when a rule is <see langword="null" />, two rules share a name or an identifier, or the revision is unspecified.</exception>
    /// <remarks>
    /// Names are unique because a person reads a rule by its name, and two rules answering to one name would leave a
    /// record of a match a person cannot tell apart. Comparison ignores case, so a set cannot carry two rules a person
    /// would read as one.
    /// </remarks>
    public static MailRuleSet Create(
        IReadOnlyList<MailRule> rules,
        MailRuleSetRevision revision,
        MailRuleConditionBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(bounds);

        if (rules.Any(rule => rule is null))
        {
            throw new ArgumentException("A rule set cannot carry a rule that is null.", nameof(rules));
        }

        if (rules.Select(rule => rule.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rules.Count)
        {
            throw new ArgumentException("A rule set cannot carry two rules under one name.", nameof(rules));
        }

        if (rules.Select(rule => rule.Id).Distinct().Count() != rules.Count)
        {
            throw new ArgumentException("A rule set cannot carry two rules under one identifier.", nameof(rules));
        }

        return new MailRuleSet([.. rules], SpecifiedRevision(revision), bounds, []);
    }

    /// <summary>Creates the held form of a set, which runs no rule until a write repairs the rules that stopped compiling.</summary>
    /// <param name="revision">The identity of the stored set.</param>
    /// <param name="bounds">The bounds the set was read under.</param>
    /// <param name="uncompiledRules">The enabled rules whose condition does not compile, at least one.</param>
    /// <returns>The held set.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when no rule is named or the revision is unspecified.</exception>
    public static MailRuleSet Held(
        MailRuleSetRevision revision,
        MailRuleConditionBounds bounds,
        IReadOnlyList<MailRuleId> uncompiledRules)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(uncompiledRules);

        if (uncompiledRules.Count == 0)
        {
            throw new ArgumentException("A held rule set names at least one rule that does not compile.", nameof(uncompiledRules));
        }

        return new MailRuleSet([], SpecifiedRevision(revision), bounds, [.. uncompiledRules]);
    }

    private static MailRuleSetRevision SpecifiedRevision(MailRuleSetRevision revision) => revision.IsSpecified
        ? revision
        : throw new ArgumentException("A rule set must carry a derived revision identity.", nameof(revision));
}
