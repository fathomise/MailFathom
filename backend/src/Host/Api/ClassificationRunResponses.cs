// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Classification;

namespace MailFathom.Host.Api;

/// <summary>What a caller asks a whole-mailbox classification run for.</summary>
/// <param name="Account">The account to classify, as this deployment's configuration names it.</param>
/// <param name="Folders">The folder aliases to walk, or <see langword="null" /> for every folder either function covers.</param>
/// <param name="Apply">Whether the run may change the mailbox, which defaults to a dry run when the caller says nothing.</param>
/// <param name="Rescore">Whether mail already decided under the terms in force is asked about again rather than skipped.</param>
/// <param name="Category">The one own category to apply, or <see langword="null" /> for a run over everything.</param>
/// <remarks>
/// Every optional answer defaults to the cautious one. The dry run above all: with filing switched on, a run over an
/// inbox is the largest single thing this feature can do to somebody's mail, so acting is something a caller says rather
/// than something they fail to switch off.
/// </remarks>
internal sealed record ClassificationRunRequestBody(
    string? Account,
    IReadOnlyList<string>? Folders,
    bool? Apply,
    bool? Rescore,
    string? Category);

/// <summary>What asking for a run did, as the administrative endpoint serves it.</summary>
/// <param name="Started">Whether this request is what put the run in front of the account.</param>
/// <param name="Run">The run the account now has outstanding, which is the one already under way when nothing started.</param>
/// <remarks>
/// The two are reported together because a caller that asked twice needs both: the run is the answer either way, and
/// whether this request started it is what tells "I have just begun a walk" from "one was already going" — which also
/// tells the caller that the terms they sent are not the terms the outstanding run is walking under.
/// </remarks>
internal sealed record ClassificationRunStartResponse(bool Started, ClassificationRunResponse Run);

/// <summary>Where an account's classification run stands, as the administrative endpoint serves it.</summary>
/// <param name="Account">The account the answer is about.</param>
/// <param name="Run">The run, or <see langword="null" /> where the account has never been asked for one.</param>
internal sealed record ClassificationRunStateResponse(string Account, ClassificationRunResponse? Run);

/// <summary>One whole-mailbox classification run, as the administrative endpoint serves it.</summary>
/// <param name="RequestedAt">When the run was asked for.</param>
/// <param name="SpamFolders">The folders whose mail it classifies as spam, empty where it covers no spam.</param>
/// <param name="CategoryFolders">The folders whose mail it asks about categories, empty where it covers no categories.</param>
/// <param name="Category">The one own category it applies, absent for a run over everything.</param>
/// <param name="Posture">Whether the run writes down what its verdicts ask of the mailbox, or only works it out.</param>
/// <param name="Rescores">Whether the run asks again about mail already decided under the terms in force.</param>
/// <param name="Profile">The classification profile the run is bound to, absent until the first pass picks it up and for a run covering no spam.</param>
/// <param name="ClassifiedEmailCount">How many occurrences the run recorded a verdict for.</param>
/// <param name="SpamEmailCount">How many of the occurrences it reached carry a spam verdict.</param>
/// <param name="UndeterminedEmailCount">How many of them carry a verdict that concluded nothing either way.</param>
/// <param name="SkippedEmailCount">How many it passed over as already decided under its profile.</param>
/// <param name="UnclassifiableEmailCount">How many it could reach no verdict about.</param>
/// <param name="ActedEmailCount">How many it asked the mailbox to change, or would have asked for in a dry run.</param>
/// <param name="CategorizedEmailCount">How many a model answered about and the run recorded categories for.</param>
/// <param name="CategoriesCurrentEmailCount">How many it passed over because their categories carry the current question revisions.</param>
/// <param name="CategoriesUnansweredEmailCount">How many it asked about and got no answer for.</param>
/// <param name="CategoriesClearedEmailCount">How many lost their categories because they are spam.</param>
/// <param name="EndedAt">When the run stopped being outstanding, absent while it still is.</param>
/// <param name="Ending">How it ended, absent for exactly as long as <paramref name="EndedAt" /> is.</param>
/// <remarks>
/// Counts, instants, and MailFathom's own names, and no message is named. What a caller watching a dry run is deciding
/// on is <paramref name="ActedEmailCount" />: it is the mail the switches reach, stated before any of it reaches a mail
/// server.
/// </remarks>
internal sealed record ClassificationRunResponse(
    DateTimeOffset RequestedAt,
    IReadOnlyList<string> SpamFolders,
    IReadOnlyList<string> CategoryFolders,
    string? Category,
    string Posture,
    bool Rescores,
    string? Profile,
    int ClassifiedEmailCount,
    int SpamEmailCount,
    int UndeterminedEmailCount,
    int SkippedEmailCount,
    int UnclassifiableEmailCount,
    int ActedEmailCount,
    int CategorizedEmailCount,
    int CategoriesCurrentEmailCount,
    int CategoriesUnansweredEmailCount,
    int CategoriesClearedEmailCount,
    DateTimeOffset? EndedAt,
    string? Ending)
{
    /// <summary>Describes one run for the wire.</summary>
    /// <param name="run">The run as it stands.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="run" /> is <see langword="null" />.</exception>
    internal static ClassificationRunResponse For(ClassificationRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new ClassificationRunResponse(
            run.RequestedAt,
            [.. run.Terms.SpamFolderAliases.Select(static alias => alias.Value)],
            [.. run.Terms.CategoryFolderAliases.Select(static alias => alias.Value)],
            run.Terms.OwnCategory,
            run.Terms.Posture.ToString(),
            run.Terms.Rescores,
            run.Profile.IsSpecified ? run.Profile.Value : null,
            run.ClassifiedEmailCount,
            run.SpamEmailCount,
            run.UndeterminedEmailCount,
            run.SkippedEmailCount,
            run.UnclassifiableEmailCount,
            run.ActedEmailCount,
            run.CategorizedEmailCount,
            run.CategoriesCurrentEmailCount,
            run.CategoriesUnansweredEmailCount,
            run.CategoriesClearedEmailCount,
            run.EndedAt,
            run.Ending?.ToString());
    }
}
