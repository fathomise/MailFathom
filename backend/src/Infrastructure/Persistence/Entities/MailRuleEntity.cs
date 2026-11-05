// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One rule of one mail account, as a person wrote it.</summary>
/// <remarks>
/// Sensitive with the mail it selects: a condition can carry an address or a subject a person typed, and a destination
/// names a folder whose name can carry a name. It goes when its account goes, through the cascade from the set row.
/// </remarks>
internal sealed class MailRuleEntity
{
    /// <summary>Gets or sets the identifier the rule was minted with.</summary>
    public required Guid Id { get; set; }

    /// <summary>Gets or sets the account the rule belongs to.</summary>
    public required string MailboxAccountId { get; set; }

    /// <summary>Gets or sets the rule's place in the order a pass evaluates the set in, a dense sequence from <c>0</c>.</summary>
    public int Position { get; set; }

    /// <summary>Gets or sets the name a person reads the rule by.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets the name folded to one case, which the account's uniqueness of names is held on.</summary>
    public required string NormalizedName { get; set; }

    /// <summary>Gets or sets whether the rule takes part in a pass.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the condition as its author wrote it.</summary>
    public required string Condition { get; set; }

    /// <summary>Gets or sets what a match does, as the JSON document <see cref="Rules.StoredMailRuleDeclaration" /> describes.</summary>
    public required string Actions { get; set; }

    /// <summary>Gets or sets whether a match ends the pass.</summary>
    public bool StopWhenMatched { get; set; }

    /// <summary>Gets or sets the names of the automatic triggers the rule takes part in.</summary>
    public required string[] Triggers { get; set; }

    /// <summary>Gets or sets the schedule a walk of the rule happens on, as a declaration the recurrence reads back.</summary>
    public string? Schedule { get; set; }
}
