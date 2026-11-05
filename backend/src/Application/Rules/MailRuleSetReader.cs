// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Sets;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Rules;

/// <summary>Reports one mail account's stored rules to an administrator who may read that account.</summary>
/// <remarks>
/// A pass reads <see cref="IMailRuleSetSource" /> and holds what it was given, which is not what this is for. This is the
/// administrator's reading: every rule as it is stored, disabled ones included, the set's version a write states it
/// read, its revision, and whether the set is held because an enabled rule no longer compiles. It carries no mail, so it
/// asks for the read grant over the account; what the rules concluded about somebody's messages is
/// <see cref="History.MailRuleHistory" /> and a different one.
/// </remarks>
public sealed class MailRuleSetReader
{
    private readonly IMailRuleSetStore store;
    private readonly IMailRuleConditionCompiler compiler;
    private readonly MailRuleConditionBounds bounds;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the reading over the stored rules.</summary>
    /// <param name="store">Keeps each account's rules.</param>
    /// <param name="compiler">Reads each condition, which is how a held set is told apart.</param>
    /// <param name="bounds">The condition bounds in force.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailRuleSetReader(
        IMailRuleSetStore store,
        IMailRuleConditionCompiler compiler,
        MailRuleConditionBounds bounds,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(authorization);

        this.store = store;
        this.compiler = compiler;
        this.bounds = bounds;
        this.authorization = authorization;
    }

    /// <summary>Reads one account's rules as they are stored now.</summary>
    /// <param name="account">The account, which must be one this deployment serves.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The stored set and the enabled rules that hold it, if any.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller is not granted <see cref="MailFathomPermission.AdminRead" /> over the account.</exception>
    public async Task<MailRuleSetReading> ReadAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        await this.authorization.RequirePermissionOverAsync(MailFathomPermission.AdminRead, account, cancellationToken);

        var stored = await this.store.ReadAsync(account, cancellationToken);
        var compiled = StoredMailRuleSetSource.Compile(stored.Declarations, stored.Revision, this.compiler, this.bounds);

        return new MailRuleSetReading(stored, compiled.UncompiledRules);
    }
}
