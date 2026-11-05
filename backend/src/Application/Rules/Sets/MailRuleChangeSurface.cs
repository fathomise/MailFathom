// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Rules.Sets;

/// <summary>Which surface a committed change to an account's rules came through, as its archive row records it.</summary>
/// <remarks>
/// <para>
/// A closed enumeration rather than an enum, because the name is written into an append-only row and read back by every
/// later build: the stored name is the identity, so renaming a member here never rewrites what an archive already holds.
/// </para>
/// <para>
/// The client API adds its own value when it starts writing rules; MCP never will, because no MCP tool reads or changes
/// a rule. During a rolling upgrade an older replica meets the newer value in rows the newer one wrote, so
/// <see cref="Restore" /> keeps a name this build does not declare rather than refusing the row: the archive is still the
/// record of the change and still what a run's revision resolves against.
/// </para>
/// </remarks>
public readonly record struct MailRuleChangeSurface
{
    private MailRuleChangeSurface(string name) => this.Name = name;

    /// <summary>Gets the administrative endpoint, which <c>mfctl</c> reaches.</summary>
    public static MailRuleChangeSurface Administration { get; } = new("Administration");

    /// <summary>Gets the name the archive stores.</summary>
    public string Name { get; }

    /// <summary>Gets whether this build declares the surface, rather than having read it from a row a newer build wrote.</summary>
    public bool IsDeclared => this == Administration;

    /// <summary>Reads a surface back from the name an archive row stores.</summary>
    /// <param name="name">The stored name.</param>
    /// <returns>The declared surface of that name, or a surface carrying a name this build does not declare.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name" /> is blank, which no build writes.</exception>
    public static MailRuleChangeSurface Restore(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return name == Administration.Name ? Administration : new MailRuleChangeSurface(name);
    }

    /// <inheritdoc />
    public override string ToString() => this.Name;
}
