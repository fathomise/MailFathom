// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.GetEmailContent;

/// <summary>Scans what a message's author wrote — its subject, its display names, and its body — before a read publishes it.</summary>
/// <remarks>
/// <para>
/// It is the one place a rendered message is scanned field by field, so a stored message read through
/// <see cref="EmailContentReader" /> and an outgoing one read from the outbox are redacted by the same rules and cannot
/// come to disagree about what the same text says.
/// </para>
/// <para>
/// It opens no report of its own: the caller has already asked whether anything is scanned on its flow and begun one
/// guarded operation for the whole read, which is what a reader waits for.
/// </para>
/// </remarks>
/// <param name="egressGuard">Scans one value at a time.</param>
/// <param name="point">The point every value scanned here crosses, which is what a finding is reported against.</param>
internal sealed class EmailContentEgressGuarding(
    SensitiveContentEgressGuard egressGuard,
    SensitiveContentEgressPoint point)
{
    /// <summary>How many of one message's display names a scanned read analyzes before it publishes addresses alone.</summary>
    /// <remarks>
    /// Set where a real message stops and a list expansion begins: correspondence a person reads names a handful of
    /// people, a thread across two departments names a few dozen, and an addressee list beyond that is a distribution
    /// somebody expanded rather than a set of names anybody reads. Both sides of the bound cost something, and the
    /// cheaper one is losing a display name past the fortieth participant of one message.
    /// </remarks>
    private const int MaximumScannedDisplayNames = 40;

    /// <summary>How many of a reduced document's texts one read scans before the rest are withheld.</summary>
    /// <remarks>
    /// Set where a message stops and a generated layout begins: a reduction emits one text per run of body words, per
    /// preformatted block, and per picture's description, and an ordinary message — newsletters included — reduces to
    /// a few hundred. Past that the body is one a template composed a span at a time, and the cheaper side of the
    /// bound is losing its tail rather than spending thousands of scanner round trips on one reading pane.
    /// </remarks>
    private const int MaximumScannedDocumentTexts = 500;

    private readonly SensitiveContentEgressGuard egressGuard = egressGuard;

    private readonly SensitiveContentEgressPoint point = point;

    /// <summary>Scans the two things a header block carries that a message's author wrote.</summary>
    public async Task<EmailContentHeaders> GuardAsync(
        EmailContentHeaders headers,
        CancellationToken cancellationToken)
    {
        return headers with
        {
            Subject = await this.egressGuard.GuardOptionalAsync(
                this.point,
                headers.Subject,
                cancellationToken),
            Participants = await this.GuardAsync(headers.Participants, cancellationToken),
        };
    }

    /// <summary>Scans the display name each participant carries, leaving the address it sits in front of alone.</summary>
    /// <remarks>
    /// <para>
    /// The count is bounded here rather than left to the message, because a scan is a round trip on the deployment that
    /// runs the personal-data analyzer in a container: a parse publishes up to
    /// <see cref="EmailParticipant.MaximumPerRole" /> addresses for each header role, so a list expansion would
    /// otherwise turn one read into thousands of sequential requests taking the process-wide scan permits from every
    /// listing and answering run behind it. Past <see cref="MaximumScannedDisplayNames" /> the address is published with
    /// no display name at all, which withholds rather than serves a name nothing scanned. A participant the sender wrote
    /// no name for costs nothing and counts towards nothing.
    /// </para>
    /// <para>
    /// A participant whose guarded name cannot be put back is dropped rather than published unguarded, which is the
    /// answer this use case already gives an address it cannot use. The address itself is unchanged and was accepted
    /// when the participant was built, so nothing a scanner returns reaches that branch.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<EmailParticipant>> GuardAsync(
        IReadOnlyList<EmailParticipant> participants,
        CancellationToken cancellationToken)
    {
        var guarded = new List<EmailParticipant>(participants.Count);
        var named = 0;

        foreach (var participant in participants)
        {
            if (participant.Address.DisplayName is not { } displayName)
            {
                guarded.Add(participant);

                continue;
            }

            var guardedName = named++ < MaximumScannedDisplayNames
                ? await this.egressGuard.GuardAsync(
                    this.point,
                    displayName,
                    cancellationToken)
                : null;

            if (EmailAddress.TryCreate(guardedName, participant.Address.Address, out var address))
            {
                guarded.Add(participant with { Address = address });
            }
        }

        return guarded;
    }

    /// <summary>Scans each representation of a body that could be read.</summary>
    /// <remarks>
    /// A body nothing could read carries no text in either representation, so an encrypted message and one whose
    /// content was never stored reach a scanner no more often than they reach a parser.
    /// </remarks>
    public async Task<EmailContentBody> GuardAsync(EmailContentBody body, CancellationToken cancellationToken)
    {
        if (body.Availability is not EmailBodyAvailability.Readable)
        {
            return body;
        }

        return EmailContentBody.Readable(
            await this.GuardAsync(body.PlainText, cancellationToken),
            body.SanitizedHtml is { } sanitizedHtml
                ? await this.GuardAsync(sanitizedHtml, cancellationToken)
                : null,
            body.Document is { } document
                ? await this.GuardAsync(document, cancellationToken)
                : null,
            body.SelfContainedHtml is { } selfContainedHtml
                ? await this.GuardAsync(selfContainedHtml, cancellationToken)
                : null,
            body.Forms);
    }

    /// <summary>Scans every text the reduced document holds, and writes the document back with what came out.</summary>
    /// <remarks>
    /// <para>
    /// The document is a representation of the same body as the other two, so a read that redacted the plain text and
    /// published the tree unscanned would hand a reader through one representation exactly what it withheld through the
    /// other. It is guarded here rather than while it is built, because building it is an adapter's work and deciding
    /// what may leave the deployment is the use case's.
    /// </para>
    /// <para>
    /// The texts are guarded as a flat list and put back positionally rather than joined into one pass, for the reason
    /// every value here is scanned on its own: a finding straddling the join between two runs would redact across words
    /// that have nothing to do with each other.
    /// </para>
    /// <para>
    /// The count is bounded for the same reason the display names above are, and the bound bites harder here: a scan is
    /// a round trip on the deployment running the personal-data analyzer in a container, a body of short styled spans
    /// reduces to thousands of runs, and this projection is now asked for on every opening of a reading pane. Past
    /// <see cref="MaximumScannedDocumentTexts" /> a text is withheld rather than published unscanned, and the document
    /// says it was truncated — which is the statement the pane already draws.
    /// </para>
    /// </remarks>
    private async Task<MailDocument> GuardAsync(MailDocument document, CancellationToken cancellationToken)
    {
        var texts = MailDocumentTexts.Collect(document);
        if (texts.Count == 0)
        {
            return document;
        }

        var scanned = texts.Count <= MaximumScannedDocumentTexts
            ? texts
            : [.. texts.Take(MaximumScannedDocumentTexts)];

        var guarded = await this.egressGuard.GuardAllAsync(
            this.point,
            scanned,
            cancellationToken);

        var rewritten = MailDocumentTexts.Rewrite(
            document,
            texts.Count == scanned.Count
                ? guarded
                : [.. guarded, .. Enumerable.Repeat(string.Empty, texts.Count - scanned.Count)]);

        return texts.Count == scanned.Count ? rewritten : rewritten with { Truncated = true };
    }

    /// <summary>Scans one representation and states the analyzed ceiling as the bound it is.</summary>
    /// <remarks>
    /// <para>
    /// The scan runs over the text this read would have returned, so every character a caller receives is one a scanner
    /// saw. What the placeholders then do to the length is left alone: replacing a short credential with a longer
    /// marker can carry the text past the bound that cut it, exactly as re-serializing sanitized markup can, and the
    /// truncation metadata is stated rather than derived from the length for that reason.
    /// </para>
    /// <para>
    /// Text beyond the analyzed ceiling is dropped rather than handed on, so the representation says so — and says it
    /// over whichever bound had cut it already, because the ceiling is where the returned text now ends. It is the one
    /// of the three a caller cannot act on: naming fewer emails returns no more of this message, and only raising
    /// <c>SensitiveContent:MaximumAnalyzedCharacters</c> does.
    /// </para>
    /// <para>
    /// That cut is the one place the balanced-markup property of the sanitized representation stops holding. The
    /// renderer keeps markup balanced by shrinking its source and sanitizing again, and a ceiling applied here cuts what
    /// the sanitizer had already serialized, so a representation carrying this truncation can end inside an element. It
    /// is preferred to the alternative: re-serializing would mean handing back text the scan never analyzed.
    /// </para>
    /// <para>
    /// The source length is untouched by all of this. It states what the message held, which redaction does not change.
    /// </para>
    /// </remarks>
    private async Task<EmailBodyRepresentation> GuardAsync(
        EmailBodyRepresentation representation,
        CancellationToken cancellationToken)
    {
        if (representation.Text.Length == 0)
        {
            return representation;
        }

        var guarded = await this.egressGuard.GuardWithOmissionAsync(
            this.point,
            representation.Text,
            cancellationToken);

        return representation with
        {
            Text = guarded.Text,
            Truncation = guarded.WasCutAtAnalyzedCeiling
                ? EmailBodyTruncation.SensitiveContentScanCeiling
                : representation.Truncation,
        };
    }
}
