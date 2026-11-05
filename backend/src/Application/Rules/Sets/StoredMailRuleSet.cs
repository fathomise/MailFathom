// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Rules.Sets;

/// <summary>One mail account's rules as they are stored: every rule in position order, and the set's version.</summary>
/// <param name="Account">The account the rules belong to, which is their whole scope.</param>
/// <param name="Version">How many writes have committed to the set, <c>0</c> for an account nobody has written a rule for.</param>
/// <param name="Declarations">Every rule in the order a pass evaluates them, disabled ones included.</param>
/// <remarks>
/// The version and the revision are two different things. The version counts writes and is what a write states it read,
/// so a concurrent one is refused; the revision names what the enabled rules mean and is what a run is bound to.
/// </remarks>
public sealed record StoredMailRuleSet(
    MailAccountId Account,
    long Version,
    IReadOnlyList<MailRuleDeclaration> Declarations)
{
    /// <summary>Gets the revision the stored rules are known by.</summary>
    public MailRuleSetRevision Revision => MailRuleSetRevision.Create(this.Declarations);

    /// <summary>Creates the set of an account nobody has written a rule for.</summary>
    /// <param name="account">The account.</param>
    /// <returns>The empty set at version <c>0</c>.</returns>
    public static StoredMailRuleSet Empty(MailAccountId account) => new(account, 0, []);

    /// <summary>Finds a rule of the set by its identifier.</summary>
    /// <param name="rule">The identifier to look for.</param>
    /// <returns>The rule, or <see langword="null" /> when the set carries none under it.</returns>
    public MailRuleDeclaration? Find(MailRuleId rule) =>
        this.Declarations.FirstOrDefault(declaration => declaration.Id == rule);
}
