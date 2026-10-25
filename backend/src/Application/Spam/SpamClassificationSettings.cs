// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;

namespace MailFathom.Application.Spam;

/// <summary>What an operator decided about spam classification.</summary>
/// <remarks>
/// Every switch is off in <see cref="Disabled" />, which is what a deployment that configured nothing runs with: no
/// classification is attempted and no path is added for an operator who asked for none of it.
/// </remarks>
public sealed record SpamClassificationSettings
{
    private SpamClassificationSettings(bool isEnabled, IReadOnlyList<MailFolderAlias> scannedFolderAliases)
    {
        this.IsEnabled = isEnabled;
        this.ScannedFolderAliases = scannedFolderAliases;
    }

    /// <summary>Gets the settings a deployment that configured nothing runs with.</summary>
    public static SpamClassificationSettings Disabled { get; } = new(isEnabled: false, []);

    /// <summary>Gets whether classification runs at all.</summary>
    public bool IsEnabled { get; }

    /// <summary>Gets the folder aliases classification runs over, in a normalized order.</summary>
    /// <remarks>
    /// Empty means no folder is classified, which is only reachable while classification is off: a deployment that
    /// switches it on and names no folder is resolved to its accounts' inbox aliases before the settings are built, so
    /// the scope defaults to the inbox rather than to everything. Classifying every folder by default would spend the
    /// work on sent mail, on drafts, and on the archive, where a verdict answers no question anybody asked.
    /// </remarks>
    public IReadOnlyList<MailFolderAlias> ScannedFolderAliases { get; }

    /// <summary>Gets the identity of the terms a verdict reached under these settings was decided by.</summary>
    /// <remarks>
    /// What it covers and what it deliberately leaves out is <see cref="SpamClassificationProfile" />'s own contract.
    /// </remarks>
    public SpamClassificationProfile Profile { get; } = SpamClassificationProfile.Create();

    /// <summary>Builds the settings an operator's answers describe.</summary>
    /// <param name="isEnabled">Whether classification runs.</param>
    /// <param name="scannedFolderAliases">The folder aliases to classify, already resolved to the inbox where the operator named none.</param>
    /// <returns>The settings, with the alias list deduplicated and ordered.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scannedFolderAliases" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// How long a message may wait on a verdict is not among these, because it is not one user's to decide: it bounds
    /// how long the index may be held back by a verdict that has not arrived, which is a cost the process bears.
    /// <see cref="SpamClassificationScope.MaximumClassificationWait" /> is where the deployment states it.
    /// </remarks>
    public static SpamClassificationSettings Create(bool isEnabled, IEnumerable<MailFolderAlias> scannedFolderAliases)
    {
        ArgumentNullException.ThrowIfNull(scannedFolderAliases);

        return new SpamClassificationSettings(
            isEnabled,
            [
                .. scannedFolderAliases
                    .DistinctBy(static alias => alias.Value, StringComparer.Ordinal)
                    .OrderBy(static alias => alias.Value, StringComparer.Ordinal),
            ]);
    }

    /// <summary>Reports whether classification runs over one folder.</summary>
    /// <param name="folderAlias">MailFathom's own name for the folder.</param>
    /// <returns><see langword="true" /> when the configured scope names that alias.</returns>
    public bool Covers(MailFolderAlias folderAlias) => this.ScannedFolderAliases.Contains(folderAlias);
}
