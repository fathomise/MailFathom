// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Folders;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>Configures how long one account's stored messages and tombstones are kept.</summary>
/// <remarks>
/// Off unless somebody asks, so an upgrade removes nothing. The two halves have separate switches and ages because a
/// tombstone is a grace period for mail already gone from the mailbox, while a visible message is mail somebody may
/// still read.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class MailAccountRetentionOptions
{
    private const string Key = "Retention";

    /// <summary>The youngest age a visible message may be retired at, so a typing mistake in a forced policy cannot empty every account.</summary>
    internal static readonly TimeSpan MinimumMessageAge = TimeSpan.FromDays(30);

    /// <summary>The youngest age a tombstone may be erased at.</summary>
    internal static readonly TimeSpan MinimumTombstoneAge = TimeSpan.FromDays(1);

    /// <summary>The most folders one account may exclude, which leaves room for a list one policy states for many accounts.</summary>
    internal const int MaximumExcludedFolders = 256;

    /// <summary>The longest entry an exclusion may hold, which is the longest folder alias the database stores.</summary>
    internal const int MaximumExcludedFolderLength = 128;

    /// <summary>Gets or sets the retention of the mail a person sees.</summary>
    public MailRetentionPassOptions Messages { get; set; } = new() { After = TimeSpan.FromDays(730) };

    /// <summary>Gets or sets the retention of the rows kept as tombstones, with their raw MIME and everything derived from it.</summary>
    public MailRetentionPassOptions Tombstones { get; set; } = new() { After = TimeSpan.FromDays(30) };

    /// <summary>Gets or sets the folders neither half reaches, each a folder alias or, for a folder a person made on a held account, its local folder identifier.</summary>
    /// <remarks>
    /// An entry naming no folder of the account excludes nothing and is not refused here, because a policy states one
    /// list for many accounts and no one account holds every folder on it.
    /// </remarks>
    public List<string> ExcludedFolders { get; set; } = [];

    /// <summary>Reports every value here the setting refuses, as the record and the policy are both held to it.</summary>
    /// <returns>One sentence per refused value, naming its key, and naming an entry by its position because an alias can name a person or a matter.</returns>
    internal IReadOnlyList<string> FindRefusals()
    {
        string?[] passRefusals =
        [
            FindPassRefusal($"{Key}:{nameof(this.Messages)}", this.Messages, MinimumMessageAge),
            FindPassRefusal($"{Key}:{nameof(this.Tombstones)}", this.Tombstones, MinimumTombstoneAge),
        ];

        return [.. passRefusals.OfType<string>(), .. this.FindExcludedFolderRefusals()];
    }

    private static string? FindPassRefusal(string key, MailRetentionPassOptions pass, TimeSpan floor)
    {
        if (pass is null)
        {
            return $"{key} must be a block.";
        }

        return pass.After < floor ? $"{key}:{nameof(pass.After)} must be at least {floor}." : null;
    }

    private static bool IsUsableExcludedFolder(string? entry) =>
        entry is { Length: <= MaximumExcludedFolderLength } && MailFolderAlias.TryCreate(entry, out _);

    private IEnumerable<string> FindExcludedFolderRefusals()
    {
        var key = $"{Key}:{nameof(this.ExcludedFolders)}";

        if (this.ExcludedFolders is null)
        {
            return [$"{key} must be a list."];
        }

        var entryRefusals = this.ExcludedFolders
            .Select(static (entry, position) => (Usable: IsUsableExcludedFolder(entry), Position: position))
            .Where(static entry => !entry.Usable)
            .Select(entry => $"{key} entry {entry.Position} must be a folder alias or a local folder identifier of at most {MaximumExcludedFolderLength} characters, with no control character.");

        return this.ExcludedFolders.Count > MaximumExcludedFolders
            ? entryRefusals.Prepend($"{key} holds more than {MaximumExcludedFolders} entries.")
            : entryRefusals;
    }
}
