// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Rules.Actions;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Rules.Sets;

/// <summary>Reads one account's stored rules and compiles its enabled ones, once per version, revision, and bounds.</summary>
/// <remarks>
/// A set whose enabled rules do not all compile is returned held rather than run in part: an ordered, stop-when-matched
/// set with one rule removed is a set nobody wrote, and it can file mail a rule above it was written to stop. Each rule
/// is compiled under its identifier rather than its name, so a refusal names the rule without carrying anything a person
/// typed.
/// </remarks>
public sealed class StoredMailRuleSetSource : IMailRuleSetSource
{
    private readonly IMailRuleSetStore store;
    private readonly IMailRuleConditionCompiler compiler;
    private readonly MailRuleConditionBounds bounds;
    private readonly MailRuleSetCompilationCache cache;

    /// <summary>Initializes the source over the stored rules.</summary>
    /// <param name="store">Keeps each account's rules.</param>
    /// <param name="compiler">Reads each condition against the fact surface.</param>
    /// <param name="bounds">The condition bounds in force for the scope reading the set.</param>
    /// <param name="cache">Remembers each account's compiled set by version.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public StoredMailRuleSetSource(
        IMailRuleSetStore store,
        IMailRuleConditionCompiler compiler,
        MailRuleConditionBounds bounds,
        MailRuleSetCompilationCache cache)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(cache);

        this.store = store;
        this.compiler = compiler;
        this.bounds = bounds;
        this.cache = cache;
    }

    /// <inheritdoc />
    public async Task<MailRuleSet> ReadAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        var stored = await this.store.ReadAsync(account, cancellationToken);

        var revision = stored.Revision;

        if (this.cache.TryFind(account, stored.Version, revision, this.bounds, out var remembered))
        {
            return remembered;
        }

        var compiled = Compile(stored.Declarations, revision, this.compiler, this.bounds);

        this.cache.Remember(account, stored.Version, revision, this.bounds, compiled);

        return compiled;
    }

    /// <summary>Compiles the enabled rules of a set, holding the set when any of them does not compile.</summary>
    /// <param name="declarations">Every rule of the set in position order.</param>
    /// <param name="revision">The revision the declarations are known by.</param>
    /// <param name="compiler">Reads each condition against the fact surface.</param>
    /// <param name="bounds">The bounds the conditions are read under.</param>
    /// <returns>The compiled set, or its held form.</returns>
    internal static MailRuleSet Compile(
        IReadOnlyList<MailRuleDeclaration> declarations,
        MailRuleSetRevision revision,
        IMailRuleConditionCompiler compiler,
        MailRuleConditionBounds bounds)
    {
        var compilations = declarations
            .Where(declaration => declaration.Enabled)
            .Select(declaration => (Declaration: declaration, Compilation: compiler.Compile(declaration.Id.ToString(), declaration.ConditionText, bounds)))
            .ToArray();

        MailRuleId[] uncompiled = [.. compilations.Where(pair => !pair.Compilation.IsCompiled).Select(pair => pair.Declaration.Id)];

        if (uncompiled.Length > 0)
        {
            return MailRuleSet.Held(revision, bounds, uncompiled);
        }

        MailRule[] rules =
        [
            .. compilations.Select(pair => MailRule.Create(
                pair.Declaration.Id,
                pair.Declaration.Name,
                pair.Compilation.Condition!,
                MailRuleActionSet.Create(pair.Declaration.Actions),
                pair.Declaration.StopWhenMatched,
                pair.Declaration.Triggers,
                pair.Declaration.Schedule)),
        ];

        return MailRuleSet.Create(rules, revision, bounds);
    }
}
