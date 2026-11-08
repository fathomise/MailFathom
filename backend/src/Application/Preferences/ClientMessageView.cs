// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Preferences;

/// <summary>Which of the two renderings a message opens on.</summary>
/// <remarks>
/// <para>
/// A closed enumeration rather than a C# <see langword="enum" />, for the reason <see cref="ClientThemeChoice" /> is one:
/// the name travels both ways across the client endpoint and is stored as itself, so a member rename must not change any
/// of the three and an ordinal would mean nothing to the client reading the response.
/// </para>
/// <para>
/// The two members are the reduced document and the sender's own markup that
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0024-rendering-mail-in-the-client-as-a-closed-document-tree.md">ADR 0024</see>
/// names.
/// </para>
/// <para>
/// Being a struct, <see langword="default" /> is reachable and is not a choice. It reports itself through
/// <see cref="IsSpecified" /> and refuses to answer for a name. Nothing undeclared reaches either side of the boundary
/// for a different reason on each: the client endpoint refuses a name <see cref="TryParse" /> does not publish, and the
/// stored document reads one as <see cref="Reduced" />, because a row an earlier build wrote may name a withdrawn
/// rendering.
/// </para>
/// </remarks>
public readonly record struct ClientMessageView
{
    private readonly string? name;

    private ClientMessageView(string name) => this.name = name;

    /// <summary>Gets the choice to draw the closed document tree the service reduced the body to.</summary>
    /// <remarks>What an unset preference reads as, because it is what this client has always drawn and costs no provider call.</remarks>
    public static ClientMessageView Reduced { get; } = new("reduced");

    /// <summary>Gets the choice to draw the sender's own markup inline, with everything that runs or reports removed.</summary>
    public static ClientMessageView EmbeddedHtml { get; } = new("embeddedHtml");

    /// <summary>Gets every choice this build publishes.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<ClientMessageView> All { get; } = [Reduced, EmbeddedHtml];

    /// <summary>Gets whether this value names a published choice rather than the unusable struct default.</summary>
    public bool IsSpecified => this.name is not null;

    /// <summary>Gets the published name, which is what a client reads and what the stored document holds.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a choice.</exception>
    public string Name => this.name
        ?? throw new InvalidOperationException("The value is the default of the struct and does not name a message view.");

    /// <summary>Reports the choice by its published name.</summary>
    /// <param name="name">The name to resolve.</param>
    /// <param name="view">The choice the name publishes, or the struct default when no choice publishes it.</param>
    /// <returns><see langword="true" /> when the name is one this build publishes.</returns>
    public static bool TryParse(string? name, out ClientMessageView view)
    {
        view = All.FirstOrDefault(candidate => string.Equals(candidate.name, name, StringComparison.Ordinal));

        return view.IsSpecified;
    }

    /// <inheritdoc />
    public override string ToString() => this.name ?? "(unspecified)";
}
