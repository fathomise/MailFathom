// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Spam.Actions;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Classification;

/// <summary>What an operator asked a whole-mailbox classification run to do.</summary>
/// <remarks>
/// <para>
/// Fixed when it is asked for rather than read again while it walks. A run that spans hours of account runs must mean the
/// same thing at its end as at its start, so an operator who edits configuration mid-walk changes the next run and not
/// this one.
/// </para>
/// <para>
/// Each of the two functions carries its own list of folders, and which functions a run covers is whether its list holds
/// anything. A folder only one of them reaches gets only that half, so one list per function is the whole of that answer.
/// </para>
/// <para>
/// Every default is the cautious one: the folders each function already covers, no asking again of what has been decided
/// under the terms in force, and a dry run.
/// </para>
/// </remarks>
public sealed record ClassificationRunTerms
{
    private ClassificationRunTerms(
        IReadOnlyList<MailFolderAlias> spamFolderAliases,
        IReadOnlyList<MailFolderAlias> categoryFolderAliases,
        SpamActionPosture posture,
        bool rescores,
        string? ownCategory)
    {
        this.SpamFolderAliases = spamFolderAliases;
        this.CategoryFolderAliases = categoryFolderAliases;
        this.Posture = posture;
        this.Rescores = rescores;
        this.OwnCategory = ownCategory;
        this.FolderAliases = Normalized(spamFolderAliases.Concat(categoryFolderAliases));
    }

    /// <summary>Gets the folders the run walks, which is every folder either function covers.</summary>
    public IReadOnlyList<MailFolderAlias> FolderAliases { get; }

    /// <summary>Gets the folders whose mail the run classifies as spam, in a normalized order.</summary>
    /// <remarks>
    /// Named rather than left implicit, because the run outlives the configuration it was asked under: reading the scope
    /// again on each pass would let a folder added halfway through a mailbox be walked from wherever the run had got to
    /// rather than from its beginning, and a reader of the record could not say which mail the run had covered.
    /// </remarks>
    public IReadOnlyList<MailFolderAlias> SpamFolderAliases { get; }

    /// <summary>Gets the folders whose mail the run asks about categories, in a normalized order.</summary>
    /// <remarks>Only ever folders that were visible to AI when the run was asked for; visibility is read again per batch, so a folder withdrawn since is not reached either.</remarks>
    public IReadOnlyList<MailFolderAlias> CategoryFolderAliases { get; }

    /// <summary>Gets whether the run writes down what its verdicts ask of the mailbox, or only works it out.</summary>
    public SpamActionPosture Posture { get; }

    /// <summary>Gets whether mail already decided under the terms in force is asked about again rather than passed over.</summary>
    /// <remarks>
    /// Off, the run passes over a message whose verdict names the run's profile and whose categories carry the current
    /// question revisions, and still acts on the verdict, which is what makes switching filing on a run that files rather
    /// than a run that re-reads a mailbox. On, every message in scope is asked about afresh — the one form of this run that
    /// reads every message however recently it was decided.
    /// </remarks>
    public bool Rescores { get; }

    /// <summary>Gets the name of the one own category the run applies, or <see langword="null" /> for a run over everything.</summary>
    /// <remarks>Spelt as the account's record spells it, so it is what that category's records are named by.</remarks>
    public string? OwnCategory { get; }

    /// <summary>Gets whether the run classifies spam.</summary>
    public bool CoversSpam => this.SpamFolderAliases.Count > 0;

    /// <summary>Gets whether the run asks about categories.</summary>
    public bool CoversCategories => this.CategoryFolderAliases.Count > 0;

    /// <summary>Builds the terms an operator's answers describe.</summary>
    /// <param name="spamFolderAliases">The folders whose mail is classified as spam, empty where the run does not.</param>
    /// <param name="categoryFolderAliases">The folders whose mail is asked about categories, empty where the run does not.</param>
    /// <param name="posture">Whether the run acts on the mailbox.</param>
    /// <param name="rescores">Whether mail already decided under the terms in force is asked about again.</param>
    /// <param name="ownCategory">The one own category the run applies, or <see langword="null" /> for everything.</param>
    /// <returns>The terms, with each alias list deduplicated and ordered.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a folder list is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="posture" /> is not a defined member.</exception>
    /// <exception cref="ArgumentException">Thrown when one own category is asked for beside spam, rescoring, or acting.</exception>
    public static ClassificationRunTerms Create(
        IEnumerable<MailFolderAlias> spamFolderAliases,
        IEnumerable<MailFolderAlias> categoryFolderAliases,
        SpamActionPosture posture,
        bool rescores,
        string? ownCategory = null)
    {
        ArgumentNullException.ThrowIfNull(spamFolderAliases);
        ArgumentNullException.ThrowIfNull(categoryFolderAliases);

        if (!Enum.IsDefined(posture))
        {
            throw new ArgumentOutOfRangeException(
                nameof(posture),
                posture,
                "A run either writes the changes its verdicts ask for down or works them out and writes nothing.");
        }

        var spamFolders = Normalized(spamFolderAliases);

        if (ownCategory is not null && (spamFolders.Length > 0 || rescores || posture is SpamActionPosture.Acting))
        {
            throw new ArgumentException(
                "One own category is applied alone: it asks nothing about spam, always asks, and takes no action on the mailbox.",
                nameof(ownCategory));
        }

        return new ClassificationRunTerms(spamFolders, Normalized(categoryFolderAliases), posture, rescores, ownCategory);
    }

    private static MailFolderAlias[] Normalized(IEnumerable<MailFolderAlias> aliases) =>
    [
        .. aliases
            .DistinctBy(static alias => alias.Value, StringComparer.Ordinal)
            .OrderBy(static alias => alias.Value, StringComparer.Ordinal),
    ];
}
