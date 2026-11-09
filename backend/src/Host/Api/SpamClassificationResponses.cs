// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Spam.History;

namespace MailFathom.Host.Api;

/// <summary>One page of what classification concluded about an account's mail, as the endpoint serves it.</summary>
/// <param name="Classifications">The classifications, newest first.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end.</param>
internal sealed record SpamClassificationPageResponse(
    IReadOnlyList<SpamClassificationResponse> Classifications,
    string? NextCursor)
{
    /// <summary>Describes one page for the wire.</summary>
    /// <param name="page">The page read back.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="page" /> is <see langword="null" />.</exception>
    internal static SpamClassificationPageResponse For(SpamClassificationHistoryPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new SpamClassificationPageResponse(
            [.. page.Entries.Select(SpamClassificationResponse.For)],
            page.NextCursor?.Encode());
    }
}

/// <summary>What classification concluded about one message, as the administrative endpoint serves it.</summary>
/// <param name="Email">The stable local identity of the occurrence, which is the same one every other read names it by.</param>
/// <param name="Folder">MailFathom's own name for the folder the occurrence is in.</param>
/// <param name="Verdict">What was concluded.</param>
/// <param name="DecidedBy">Which stage reached the verdict.</param>
/// <param name="SpamSubcategory">What kind of spam the message is, absent where no stage named one.</param>
/// <param name="SpamSubcategoryNamedBy">Which stage named the subcategory, absent exactly when <paramref name="SpamSubcategory" /> is.</param>
/// <param name="PhishingRequest">What a phishing message asks for, absent where the model named nothing.</param>
/// <param name="ImpersonatedParty">The kind of party the message pretends to come from, absent where the model named nothing.</param>
/// <param name="Score">The score reached, absent when no stage produced a number.</param>
/// <param name="Threshold">The score it was judged against, absent exactly when <paramref name="Score" /> is.</param>
/// <param name="Profile">The terms the verdict was reached under, absent on a record written before it named one.</param>
/// <param name="Signals">The names of the facts the verdict rests on, in the order the stages produced them.</param>
/// <param name="EvaluatedAt">When the classification was evaluated.</param>
/// <param name="RequestedMutations">The changes the verdict asked the mailbox for, empty where it asked for none.</param>
/// <remarks>
/// A signal appears by name and never by value, because the observation beside it is text a mail server wrote and can
/// carry a sending domain. A requested change is named and pointed at rather than described: what became of it is the
/// mutation trail's own answer, read through the audit route beside this one.
/// </remarks>
internal sealed record SpamClassificationResponse(
    Guid Email,
    string Folder,
    string Verdict,
    string DecidedBy,
    string? SpamSubcategory,
    string? SpamSubcategoryNamedBy,
    string? PhishingRequest,
    string? ImpersonatedParty,
    double? Score,
    double? Threshold,
    string? Profile,
    IReadOnlyList<string> Signals,
    DateTimeOffset EvaluatedAt,
    IReadOnlyList<SpamRequestedMutationResponse> RequestedMutations)
{
    /// <summary>Describes one classification for the wire.</summary>
    /// <param name="entry">The classification as it was read back.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry" /> is <see langword="null" />.</exception>
    internal static SpamClassificationResponse For(SpamClassificationHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new SpamClassificationResponse(
            entry.EmailId.Value,
            entry.FolderAlias.Value,
            entry.Verdict.ToString(),
            entry.DecidedBy.ToString(),
            entry.Subcategory?.ToString(),
            entry.SubcategoryNamedBy?.ToString(),
            entry.PhishingRequest?.ToString(),
            entry.ImpersonatedParty?.ToString(),
            entry.Assessment?.Score,
            entry.Assessment?.Threshold,
            entry.Profile.IsSpecified ? entry.Profile.Value : null,
            entry.SignalNames,
            entry.EvaluatedAt,
            [
                .. entry.RequestedMutations.Select(static requested => new SpamRequestedMutationResponse(
                    requested.RecordId.Value,
                    requested.Mutation.Name)),
            ]);
    }
}

/// <summary>One change a verdict asked the mailbox for, as the administrative endpoint serves it.</summary>
/// <param name="Record">The durable mutation record the account's convergence pass carries.</param>
/// <param name="Mutation">What was asked for.</param>
internal sealed record SpamRequestedMutationResponse(Guid Record, string Mutation);
