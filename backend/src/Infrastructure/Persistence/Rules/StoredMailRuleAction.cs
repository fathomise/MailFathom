// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Rules.Actions;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;

namespace MailFathom.Infrastructure.Persistence.Rules;

/// <summary>One action of a stored rule, by the mutation it asks for and the one parameter that mutation takes.</summary>
/// <param name="Mutation">The mutation's published name.</param>
/// <param name="Destination">The folder a relocation or a copy files into, by its alias or as <c>role:&lt;role&gt;</c>.</param>
/// <param name="DesiredState">Whether a flag action sets or clears its flag.</param>
/// <param name="Keywords">The keywords a keyword action names.</param>
internal sealed record StoredMailRuleAction(
    string Mutation,
    string? Destination,
    bool? DesiredState,
    IReadOnlyList<string>? Keywords)
{
    /// <summary>Renders an action into its stored form.</summary>
    /// <param name="action">The action.</param>
    /// <returns>The stored form.</returns>
    public static StoredMailRuleAction From(MailRuleAction action) => new(
        action.Mutation.Name,
        action.Destination?.ToString(),
        action.DesiredSeenState ?? action.DesiredFlaggedState,
        action.Keywords is { } keywords ? [.. keywords.Values] : null);

    /// <summary>Reads the stored form back into the action it was rendered from.</summary>
    /// <returns>The action.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the stored form is not one this store wrote.</exception>
    public MailRuleAction ToAction()
    {
        if (!MailboxMutation.TryParseName(this.Mutation, out var mutation))
        {
            throw new InvalidOperationException("A stored rule names an action this build does not know.");
        }

        return mutation switch
        {
            _ when mutation == MailboxMutation.Relocate => MailRuleAction.Relocate(this.RequireDestination()),
            _ when mutation == MailboxMutation.Copy => MailRuleAction.Copy(this.RequireDestination()),
            _ when mutation == MailboxMutation.Delete => MailRuleAction.Delete(),
            _ when mutation == MailboxMutation.SetSeen => MailRuleAction.SetSeen(this.RequireState()),
            _ when mutation == MailboxMutation.SetFlagged => MailRuleAction.SetFlagged(this.RequireState()),
            _ when mutation == MailboxMutation.AddKeywords => MailRuleAction.AddKeywords(this.RequireKeywords()),
            _ when mutation == MailboxMutation.RemoveKeywords => MailRuleAction.RemoveKeywords(this.RequireKeywords()),
            _ when mutation == MailboxMutation.SetKeywords => MailRuleAction.SetKeywords(this.RequireKeywords()),
            _ => throw new InvalidOperationException("A stored rule names an action a rule cannot take."),
        };
    }

    private MailFolderReference RequireDestination() =>
        MailFolderReference.TryCreate(this.Destination, out var destination)
            ? destination
            : throw new InvalidOperationException("A stored rule action names no folder it can file into.");

    private bool RequireState() =>
        this.DesiredState ?? throw new InvalidOperationException("A stored rule flag action names no state.");

    private AuthoredMailKeywords RequireKeywords() =>
        AuthoredMailKeywords.Create(this.Keywords ?? throw new InvalidOperationException("A stored rule keyword action names no keywords."));
}
