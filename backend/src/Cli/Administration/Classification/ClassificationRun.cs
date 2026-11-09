// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Classification;

/// <summary>What a deployment is asked when a whole-mailbox classification run is wanted.</summary>
/// <param name="Account">The account to classify, as the deployment's configuration names it.</param>
/// <param name="Folders">The folder aliases to walk, or <see langword="null" /> for every folder either function covers.</param>
/// <param name="Apply">Whether the run may change the mailbox, which is a dry run when it is absent or false.</param>
/// <param name="Rescore">Whether mail already decided under the terms in force is asked about again rather than passed over.</param>
/// <param name="Category">The one own category to apply, or <see langword="null" /> for a run over everything.</param>
internal sealed record ClassificationRunRequest(
    [property: JsonPropertyName("account")] string Account,
    [property: JsonPropertyName("folders")] IReadOnlyList<string>? Folders,
    [property: JsonPropertyName("apply")] bool Apply,
    [property: JsonPropertyName("rescore")] bool Rescore,
    [property: JsonPropertyName("category")] string? Category);

/// <summary>What asking for a run did.</summary>
/// <param name="Started">Whether this request is what put the run in front of the account.</param>
/// <param name="Run">The run the account now has outstanding, which is the one already going when nothing started.</param>
/// <remarks>
/// A request that started nothing is an answer rather than a refusal — and the run it reports is walking under the terms
/// it was asked for, not under the ones this request carried.
/// </remarks>
internal sealed record ClassificationRunStart(
    [property: JsonPropertyName("started")] bool Started,
    [property: JsonPropertyName("run")] ClassificationRun? Run);

/// <summary>Where an account's classification run stands.</summary>
/// <param name="Account">The account the answer is about.</param>
/// <param name="Run">The run, or <see langword="null" /> where the account has never been asked for one.</param>
internal sealed record ClassificationRunState(
    [property: JsonPropertyName("account")] string? Account,
    [property: JsonPropertyName("run")] ClassificationRun? Run);

/// <summary>One whole-mailbox classification run, and how far the account's runs have carried it.</summary>
/// <param name="RequestedAt">When the run was asked for.</param>
/// <param name="SpamFolders">The folders whose mail the run classifies as spam.</param>
/// <param name="CategoryFolders">The folders whose mail the run asks about categories.</param>
/// <param name="Category">The one own category it applies, absent for a run over everything.</param>
/// <param name="Posture">Whether the run changes the mailbox or only works out what it would change.</param>
/// <param name="Rescores">Whether it asks about mail already decided under the terms in force again.</param>
/// <param name="Profile">The classification profile the run is bound to, absent until the first pass picks it up.</param>
/// <param name="ClassifiedEmailCount">How many messages the run scored.</param>
/// <param name="SpamEmailCount">How many of the messages it reached are junk.</param>
/// <param name="UndeterminedEmailCount">How many of them nothing decided either way about.</param>
/// <param name="SkippedEmailCount">How many it passed over as already decided under its profile.</param>
/// <param name="UnclassifiableEmailCount">How many it could reach no verdict about.</param>
/// <param name="ActedEmailCount">How many it acted on, or would act on where it is a dry run.</param>
/// <param name="CategorizedEmailCount">How many a model answered about.</param>
/// <param name="CategoriesCurrentEmailCount">How many it passed over because they answer the current questions.</param>
/// <param name="CategoriesUnansweredEmailCount">How many it asked about and got no answer for.</param>
/// <param name="CategoriesClearedEmailCount">How many lost their categories because they are spam.</param>
/// <param name="EndedAt">When the run stopped being outstanding, absent while it still is.</param>
/// <param name="Ending">How it ended, absent for exactly as long as <paramref name="EndedAt" /> is.</param>
internal sealed record ClassificationRun(
    [property: JsonPropertyName("requestedAt")] DateTimeOffset RequestedAt,
    [property: JsonPropertyName("spamFolders")] IReadOnlyList<string> SpamFolders,
    [property: JsonPropertyName("categoryFolders")] IReadOnlyList<string> CategoryFolders,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("posture")] string? Posture,
    [property: JsonPropertyName("rescores")] bool Rescores,
    [property: JsonPropertyName("profile")] string? Profile,
    [property: JsonPropertyName("classifiedEmailCount")] int ClassifiedEmailCount,
    [property: JsonPropertyName("spamEmailCount")] int SpamEmailCount,
    [property: JsonPropertyName("undeterminedEmailCount")] int UndeterminedEmailCount,
    [property: JsonPropertyName("skippedEmailCount")] int SkippedEmailCount,
    [property: JsonPropertyName("unclassifiableEmailCount")] int UnclassifiableEmailCount,
    [property: JsonPropertyName("actedEmailCount")] int ActedEmailCount,
    [property: JsonPropertyName("categorizedEmailCount")] int CategorizedEmailCount,
    [property: JsonPropertyName("categoriesCurrentEmailCount")] int CategoriesCurrentEmailCount,
    [property: JsonPropertyName("categoriesUnansweredEmailCount")] int CategoriesUnansweredEmailCount,
    [property: JsonPropertyName("categoriesClearedEmailCount")] int CategoriesClearedEmailCount,
    [property: JsonPropertyName("endedAt")] DateTimeOffset? EndedAt,
    [property: JsonPropertyName("ending")] string? Ending)
{
    /// <summary>Gets whether the run leaves the mailbox alone whatever its verdicts conclude.</summary>
    internal bool IsDryRun => !string.Equals(this.Posture, "Acting", StringComparison.OrdinalIgnoreCase);

    /// <summary>Describes where the run stands in one line an operator reads.</summary>
    /// <returns>Whether it is still going or how it ended, and when.</returns>
    /// <remarks>
    /// A run in flight reports no estimate of what is left. The pass takes as much of the mailbox as one account run's
    /// budget reaches and the rest waits for the next run, so how far there is to go depends on how often that account
    /// synchronizes — a figure invented here would be a prediction rather than a reading.
    /// </remarks>
    internal string DescribeState() => this switch
    {
        { EndedAt: { } endedAt, Ending: { Length: > 0 } ending } =>
            $"{ending} at {endedAt.ToString("u", CultureInfo.InvariantCulture)}",
        { EndedAt: { } endedAt } => $"ended at {endedAt.ToString("u", CultureInfo.InvariantCulture)}",
        _ => "under way",
    };

    /// <summary>Describes what the run has read so far, in counts and nothing derived from a message.</summary>
    /// <returns>The counts, grouped invariantly for the reason every other figure this tool prints is.</returns>
    internal string DescribeProgress() => string.Create(
        CultureInfo.InvariantCulture,
        $"{this.ClassifiedEmailCount} scored, {this.SkippedEmailCount} already decided, {this.UnclassifiableEmailCount} unreadable");

    /// <summary>Describes what the run did about categories, in counts and nothing derived from a message.</summary>
    /// <returns>The counts, grouped invariantly for the reason every other figure this tool prints is.</returns>
    internal string DescribeCategories() => string.Create(
        CultureInfo.InvariantCulture,
        $"{this.CategorizedEmailCount} asked, {this.CategoriesCurrentEmailCount} already current, {this.CategoriesUnansweredEmailCount} unanswered, {this.CategoriesClearedEmailCount} cleared as spam");

    /// <summary>Describes what the run found, and what it did or would do about it.</summary>
    /// <returns>The junk it found and the mail the switches reach, worded for the posture the run walks under.</returns>
    internal string DescribeOutcome() => string.Create(
        CultureInfo.InvariantCulture,
        $"{this.SpamEmailCount} junk, {this.UndeterminedEmailCount} undetermined, {this.ActedEmailCount} {(this.IsDryRun ? "would be acted on" : "acted on")}");
}
