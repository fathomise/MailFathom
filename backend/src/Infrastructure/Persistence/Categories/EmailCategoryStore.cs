// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Categories;
using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Gating;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Categories;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.Infrastructure.Persistence.Spam;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Categories;

/// <summary>Reads the arriving mail still owed a category, and writes down what a model said about it.</summary>
[RequiresIntegrationCoverage]
internal sealed class EmailCategoryStore(
    MailFathomDbContext dbContext,
    IDeploymentMailFolders deploymentFolders,
    DerivedWorkGate derivedWorkGate)
    : IEmailCategoryStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<EmailAwaitingCategorization>> GetEmailsAwaitingCategorizationAsync(
        MailAccountId account,
        StoredEmailId? resumeAfter,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        var resumeAfterId = resumeAfter?.Value;
        var visibleFolders = await deploymentFolders.ReadAsync(MailFolderSelection.VisibleToAI, [account], cancellationToken);
        var junkFolders = await deploymentFolders.ReadAsync(MailFolderSelection.Junk, [account], cancellationToken);
        var terms = await derivedWorkGate.ReadTermsAsync(cancellationToken);

        var candidates = await Selecting(dbContext.StoredEmails.AsNoTracking(), account.Value, visibleFolders, junkFolders, terms)
            .Where(email => resumeAfterId == null || email.Id > resumeAfterId)
            .OrderBy(email => email.Id)
            .Take(batchSize)
            .Select(email => new CandidateRow(email.Id, email.EmailThreadId, email.ParentStoredEmailId))
            .ToArrayAsync(cancellationToken);

        var openings = await this.ReadOpeningsAsync(account, candidates, visibleFolders, junkFolders, terms, cancellationToken);

        return
        [
            .. candidates.Select(candidate => new EmailAwaitingCategorization(
                StoredEmailId.Create(candidate.Id),
                OpeningFor(candidate, openings))),
        ];
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<StoredEmailId>> ReadAwaitedAsync(
        MailAccountId account,
        IReadOnlyCollection<StoredEmailId> emails,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(emails);

        var identifiers = emails.Select(static email => email.Value).ToArray();
        var visibleFolders = await deploymentFolders.ReadAsync(MailFolderSelection.VisibleToAI, [account], cancellationToken);
        var junkFolders = await deploymentFolders.ReadAsync(MailFolderSelection.Junk, [account], cancellationToken);
        var terms = await derivedWorkGate.ReadTermsAsync(cancellationToken);

        var awaited = await Awaited(dbContext.StoredEmails.AsNoTracking(), account.Value, identifiers, visibleFolders, junkFolders, terms)
            .Select(static email => email.Id)
            .ToArrayAsync(cancellationToken);

        return awaited.Select(StoredEmailId.Create).ToHashSet();
    }

    /// <inheritdoc />
    /// <remarks>
    /// A message holds one set of answers, so the default category already recorded is what says it was asked about. A
    /// second replica inserting beside this one violates that row's index, and the retry reads the winner and stages
    /// nothing, which is the answer the losing replica is owed.
    /// </remarks>
    public async Task<bool> SaveAsync(
        IPersistenceSession session,
        StoredEmailId email,
        IReadOnlyList<EmailCategoryAssignment> assignments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assignments);

        var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var storedEmailId = email.Value;

        if (await context.EmailCategories.AnyAsync(
                record => record.StoredEmailId == storedEmailId && record.Kind == EmailCategoryKind.Default,
                cancellationToken)
            || !await context.StoredEmails.AnyAsync(stored => stored.Id == storedEmailId, cancellationToken))
        {
            return false;
        }

        context.EmailCategories.AddRange(assignments.Select(assignment => new EmailCategoryEntity
        {
            StoredEmailId = storedEmailId,
            Kind = assignment.Kind,
            Category = assignment.Category,
            Probability = assignment.Probability,
            Source = EmailCategorySource.Model,
            Origin = assignment.ModelBuild,
            QuestionRevision = assignment.QuestionRevision,
        }));

        return true;
    }

    /// <summary>Narrows stored mail to the arriving messages a model is still owed a question about.</summary>
    /// <param name="emails">The emails to narrow.</param>
    /// <param name="mailboxAccountId">The account.</param>
    /// <param name="visibleFolders">The folders visible to AI.</param>
    /// <param name="junkFolders">The folders the account maps to the junk role.</param>
    /// <param name="terms">The terms the spam gate decides under.</param>
    /// <returns>The narrowed query, which PostgreSQL evaluates in full.</returns>
    /// <remarks>
    /// <para>
    /// Arriving means no rule pass has evaluated it yet, and that is what keeps a setting from re-reading stored mail. The
    /// copies this deployment filed of its own outgoing mail are left out for the same reason the rule pass leaves them
    /// out: nothing ever evaluates them, so they would look like arrivals for ever.
    /// </para>
    /// <para>
    /// The recorded default category is what takes a message out of the queue. A message with none, because a call failed,
    /// is asked about again only while no rule pass has evaluated it.
    /// </para>
    /// </remarks>
    internal static IQueryable<StoredEmailEntity> Selecting(
        IQueryable<StoredEmailEntity> emails,
        string mailboxAccountId,
        IReadOnlyList<MailFolderIdentity> visibleFolders,
        IReadOnlyList<MailFolderIdentity> junkFolders,
        DerivedWorkAdmissionTerms terms) => ShowableToAModel(
        emails
            .Where(UnsentStoredEmails.IsSentOrReceived)
            .Where(email => email.MailboxAccountId == mailboxAccountId
                && email.ReportKind == null
                && email.ContentAvailability == StoredEmailContentAvailability.Available
                && email.RulesEvaluatedAt == null
                && email.FiledFromOutgoingEmailId == null
                && !email.Categories.Any(record => record.Kind == EmailCategoryKind.Default)),
        visibleFolders,
        junkFolders,
        terms);

    /// <summary>Narrows the named messages to those a rule naming the category fact should still wait for.</summary>
    /// <param name="emails">The emails to narrow.</param>
    /// <param name="mailboxAccountId">The account.</param>
    /// <param name="identifiers">The messages to ask about.</param>
    /// <param name="visibleFolders">The folders visible to AI.</param>
    /// <param name="junkFolders">The folders the account maps to the junk role.</param>
    /// <param name="terms">The terms the spam gate decides under, whose release instant bounds this wait as well.</param>
    /// <returns>The narrowed query, which PostgreSQL evaluates in full.</returns>
    /// <remarks>
    /// The bound is the verdict's own, which is what leaves the settlement's fifteen further minutes for the rule pass and
    /// the move it asks for: past it the message is evaluated with no category rather than held any longer.
    /// </remarks>
    internal static IQueryable<StoredEmailEntity> Awaited(
        IQueryable<StoredEmailEntity> emails,
        string mailboxAccountId,
        Guid[] identifiers,
        IReadOnlyList<MailFolderIdentity> visibleFolders,
        IReadOnlyList<MailFolderIdentity> junkFolders,
        DerivedWorkAdmissionTerms terms) =>
        Selecting(emails.Where(email => identifiers.Contains(email.Id)), mailboxAccountId, visibleFolders, junkFolders, terms)
            .Where(email => email.StoredAt > terms.ReleasedWhenStoredBefore);

    /// <summary>Narrows stored mail to what a model may be shown: nothing junk, nothing erased, and nothing in a folder withheld from AI.</summary>
    /// <remarks>
    /// The gate alone is not enough here. It reaches only the accounts classifying spam, and categorization is switched on
    /// independently of that, so the junk folder and a recorded spam verdict are excluded for every account.
    /// </remarks>
    private static IQueryable<StoredEmailEntity> ShowableToAModel(
        IQueryable<StoredEmailEntity> emails,
        IReadOnlyList<MailFolderIdentity> visibleFolders,
        IReadOnlyList<MailFolderIdentity> junkFolders,
        DerivedWorkAdmissionTerms terms) => DerivedWorkAdmittedEmails.Admitting(
        AccountScopedMailFolders.Excluding(
            AccountScopedMailFolders.Admitting(
                emails
                    .Where(StoredEmailTombstone.IsNotTombstoned)
                    .Where(email => email.SpamClassification == null || email.SpamClassification.Verdict != SpamVerdict.Spam),
                visibleFolders),
            junkFolders),
        terms);

    /// <summary>Narrows stored mail to the identifier of the earliest message of each named conversation.</summary>
    /// <param name="emails">The emails to narrow.</param>
    /// <param name="mailboxAccountId">The account.</param>
    /// <param name="threads">The conversations.</param>
    /// <returns>One identifier per conversation, which PostgreSQL chooses.</returns>
    internal static IQueryable<Guid> FirstOfEachConversation(
        IQueryable<StoredEmailEntity> emails,
        string mailboxAccountId,
        Guid[] threads) => emails
        .Where(UnsentStoredEmails.IsSentOrReceived)
        .Where(email => email.MailboxAccountId == mailboxAccountId
            && email.EmailThreadId != null
            && threads.Contains(email.EmailThreadId.Value))
        .GroupBy(email => email.EmailThreadId)
        .Select(thread => thread
            .OrderBy(email => (email.SentAt ?? email.ReceivedAt) == null)
            .ThenBy(email => email.SentAt ?? email.ReceivedAt)
            .ThenBy(email => email.Id)
            .Select(email => email.Id)
            .First());

    /// <summary>Reads a bounded excerpt of each named message a model may be shown.</summary>
    /// <param name="emails">The emails to read from.</param>
    /// <param name="firstOfEach">The messages.</param>
    /// <param name="visibleFolders">The folders visible to AI.</param>
    /// <param name="junkFolders">The folders the account maps to the junk role.</param>
    /// <param name="terms">The terms the spam gate decides under.</param>
    /// <returns>One row per message that may be shown, cut one character past the bound by PostgreSQL.</returns>
    internal static IQueryable<OpeningRow> Openings(
        IQueryable<StoredEmailEntity> emails,
        Guid[] firstOfEach,
        IReadOnlyList<MailFolderIdentity> visibleFolders,
        IReadOnlyList<MailFolderIdentity> junkFolders,
        DerivedWorkAdmissionTerms terms)
    {
        // One character past the bound says whether the message went on, without reading the rest of it.
        var charactersRead = ConversationOpening.MaximumCharacters + 1;

        return ShowableToAModel(emails.Where(email => firstOfEach.Contains(email.Id)), visibleFolders, junkFolders, terms)
            .Select(email => new OpeningRow(
                email.Id,
                email.EmailThreadId!.Value,
                email.SearchDocument!.BodyText!.Substring(0, charactersRead)));
    }

    private static ConversationOpening? OpeningFor(CandidateRow candidate, Dictionary<Guid, OpeningRow> openings) =>
        candidate is { ParentStoredEmailId: not null, EmailThreadId: { } thread }
        && openings.TryGetValue(thread, out var opening)
        && opening.Id != candidate.Id
        && opening.Text is { Length: > 0 } text
            ? new ConversationOpening(
                MailTextBounds.TruncateAtTextElementBoundary(text, ConversationOpening.MaximumCharacters),
                text.Length > ConversationOpening.MaximumCharacters)
            : null;

    /// <summary>Reads a bounded excerpt of the first message of each conversation a candidate replies in, keyed by conversation.</summary>
    /// <remarks>
    /// The first message is found first and judged second, so a conversation whose first message may not be shown sends
    /// no opening at all rather than whichever later message happens to be showable, which is not what it would claim
    /// to be.
    /// </remarks>
    private async Task<Dictionary<Guid, OpeningRow>> ReadOpeningsAsync(
        MailAccountId account,
        CandidateRow[] candidates,
        IReadOnlyList<MailFolderIdentity> visibleFolders,
        IReadOnlyList<MailFolderIdentity> junkFolders,
        DerivedWorkAdmissionTerms terms,
        CancellationToken cancellationToken)
    {
        Guid[] threads = [.. candidates.Where(static candidate => candidate.ParentStoredEmailId is not null)
            .Select(static candidate => candidate.EmailThreadId)
            .OfType<Guid>()
            .Distinct()];

        if (threads.Length == 0)
        {
            return [];
        }

        var firstOfEach = await FirstOfEachConversation(dbContext.StoredEmails.AsNoTracking(), account.Value, threads)
            .ToArrayAsync(cancellationToken);

        var openings = await Openings(dbContext.StoredEmails.AsNoTracking(), firstOfEach, visibleFolders, junkFolders, terms)
            .ToArrayAsync(cancellationToken);

        return openings.ToDictionary(static opening => opening.ThreadId);
    }

    private sealed record CandidateRow(Guid Id, Guid? EmailThreadId, Guid? ParentStoredEmailId);

    /// <summary>The bounded text of the message that opened a conversation.</summary>
    /// <param name="Id">The message.</param>
    /// <param name="ThreadId">The conversation it opened.</param>
    /// <param name="Text">Its text, cut one character past the bound.</param>
    internal sealed record OpeningRow(Guid Id, Guid ThreadId, string? Text);
}
