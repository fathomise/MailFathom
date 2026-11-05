// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Rules.Sets;

/// <summary>Which surface a committed change to an account's rules came through, as its archive row records it.</summary>
/// <remarks>
/// <para>
/// A closed enumeration rather than an enum, because the name is written into an append-only row and read back by every
/// later build: the stored name is the identity, so renaming a member here never rewrites what an archive already holds.
/// It is never serialized as JSON, so it carries no converter.
/// </para>
/// <para>
/// The client API adds its own value when it starts writing rules; MCP never will, because no MCP tool reads or changes
/// a rule. During a rolling upgrade an older replica meets the newer value in rows the newer one wrote, and
/// <see cref="TryParse" /> answers such a name with the unspecified default rather than refusing the row: the archive is
/// still the record of the change and still what a run's revision resolves against, and a surface nothing here declares
/// is unknown rather than new. The default is reachable as for every struct; the store refuses to archive it.
/// </para>
/// </remarks>
public readonly record struct MailRuleChangeSurface
{
    private readonly string? name;

    private MailRuleChangeSurface(string name) => this.name = name;

    /// <summary>Gets the administrative endpoint, which <c>mfctl</c> reaches.</summary>
    public static MailRuleChangeSurface Administration { get; } = new("Administration");

    /// <summary>Gets every surface this build declares.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<MailRuleChangeSurface> All { get; } = [Administration];

    /// <summary>Gets whether this value names a declared surface rather than the unusable struct default.</summary>
    public bool IsSpecified => this.name is not null;

    /// <summary>Reads a surface back from the name an archive row stores.</summary>
    /// <param name="name">The stored name.</param>
    /// <param name="surface">The declared surface of that name, or the unspecified default when none is.</param>
    /// <returns><see langword="true" /> when this build declares a surface of that name.</returns>
    public static bool TryParse(string? name, out MailRuleChangeSurface surface)
    {
        surface = All.FirstOrDefault(candidate => string.Equals(candidate.name, name, StringComparison.Ordinal));

        return surface.IsSpecified;
    }

    /// <inheritdoc />
    public override string ToString() => this.name ?? "(unspecified)";
}
