// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Emails.ReadSource;

/// <summary>Reads one stored email's raw source — every header and every MIME part, as received — for the person whose mail it is.</summary>
/// <remarks>
/// <para>
/// It serves the local copy and nothing else. The use case holds no mailbox port, so a read cannot fetch a message and
/// cannot set the remote <c>\Seen</c> flag, and it answers the same way in every custody phase: the stored copy is the
/// message in each of them, and a message whose copy was never stored has no source here to give.
/// </para>
/// <para>
/// Every refusal about what is there is one refusal. A message this user does not hold, one no deployment ever held, a
/// copy deliberately not stored, and a copy that is missing or damaged all answer
/// <see cref="EmailSourceUnavailable" />; the last two also record the repair request a read of the
/// message records, because the finding is about the copy rather than about which read met it.
/// </para>
/// <para>
/// A source is withheld wherever any account this person reads is screened, because that is the posture every read of
/// their mail is redacted under, and a source served beside a redacted body would hand out what the redaction withholds.
/// The check follows the reachability check, so a message this person cannot read is unavailable rather than withheld.
/// </para>
/// <para>
/// A source larger than <see cref="MaximumServedOctets" /> is served cut, with the size of the whole beside it, so a
/// reader is told where it stops rather than handed an answer no screen could draw.
/// </para>
/// </remarks>
public sealed class EmailSourceReader
{
    /// <summary>The most of one message's source a read serves, in octets.</summary>
    /// <remarks>
    /// A source is read by a person, and past a mebibyte what is left is encoded attachments nobody reads as text. The
    /// bound is what keeps a forty-megabyte message from being buffered into one answer.
    /// </remarks>
    public const int MaximumServedOctets = 1_048_576;

    private readonly IStoredEmailSummaryReader summaryReader;
    private readonly IEmailContentStore contentStore;
    private readonly IEmailContentRepairRequestStore repairRequestStore;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the use case.</summary>
    /// <param name="summaryReader">Reads one stored email's summary by its identity.</param>
    /// <param name="contentStore">Reads the raw MIME stored for an email, with what was recorded about it.</param>
    /// <param name="repairRequestStore">Records durably that a local copy has to be fetched or read again.</param>
    /// <param name="scopeResolver">Answers whether the reader may reach the mailbox an email was stored from.</param>
    /// <param name="egressGuard">Answers whether anything is scanned for in the mail this person reads.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public EmailSourceReader(
        IStoredEmailSummaryReader summaryReader,
        IEmailContentStore contentStore,
        IEmailContentRepairRequestStore repairRequestStore,
        MailboxScopeResolver scopeResolver,
        SensitiveContentEgressGuard egressGuard,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(summaryReader);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(repairRequestStore);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(egressGuard);
        ArgumentNullException.ThrowIfNull(authorization);

        this.summaryReader = summaryReader;
        this.contentStore = contentStore;
        this.repairRequestStore = repairRequestStore;
        this.scopeResolver = scopeResolver;
        this.egressGuard = egressGuard;
        this.authorization = authorization;
    }

    /// <summary>Reads one email's source for a caller that holds the mailbox read grant.</summary>
    /// <param name="storedEmailId">The email, as a read of it published it.</param>
    /// <param name="cancellationToken">Cancels the read when the reader disconnects.</param>
    /// <returns>The source, or why there is none.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller is not granted <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <remarks>
    /// The grant is asked for here rather than only at the route, so an entrypoint added later cannot reach a source by
    /// forgetting a filter. It is the grant reading the message is published under, because the source is the message a
    /// reader already holds rather than a wider disclosure.
    /// </remarks>
    public async Task<EmailSourceReadOutcome> ReadForReaderAsync(
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        var summary = await this.summaryReader.FindAsync(storedEmailId, cancellationToken);

        if (summary is null
            || !await this.scopeResolver.IsReadableAsync(MailboxReaderKind.Person, summary, cancellationToken)
            || summary.ContentAvailability is not StoredEmailContentAvailability.Available)
        {
            return new EmailSourceUnavailable();
        }

        var posture = await this.egressGuard.ReadPostureAcrossAccountsOfAsync(this.scopeResolver.User, cancellationToken);

        if (posture.Posture.IsActive)
        {
            return new EmailSourceWithheld();
        }

        var content = await this.contentStore.FindStoredContentAsync(storedEmailId, cancellationToken);

        if (content is null)
        {
            return await this.RefuseAndRequestRepairAsync(storedEmailId, EmailContentDefect.Missing, cancellationToken);
        }

        if (content.FindIntegrityDefect() is { } defect)
        {
            return await this.RefuseAndRequestRepairAsync(storedEmailId, defect, cancellationToken);
        }

        await this.repairRequestStore.NoteIfServedFromRetainedCopyAsync(content, storedEmailId, cancellationToken);

        var whole = content.RawMime;

        return new EmailSourceServed(whole[..Math.Min(whole.Length, MaximumServedOctets)], whole.Length);
    }

    private async Task<EmailSourceReadOutcome> RefuseAndRequestRepairAsync(
        StoredEmailId storedEmailId,
        EmailContentDefect defect,
        CancellationToken cancellationToken)
    {
        await this.repairRequestStore.RecordAsync(new EmailContentRepairRequest(storedEmailId, defect), cancellationToken);

        return new EmailSourceUnavailable();
    }
}
