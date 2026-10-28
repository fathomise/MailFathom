// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Emails.Signatures;
using MailFathom.Domain.Folders;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves everything a reading pane draws around a message, and none of what it draws inside one.</summary>
/// <remarks>
/// <para>
/// It is the other half of the pane. The body route serves the message as words and as a document tree, and this serves
/// what a screen puts above and beside them: the headers the message displays, what this deployment established about
/// the author it displays, the files it carries, and which forms of its body exist to be asked for. The two are separate
/// requests because they are separately expensive and separately cacheable by a client — a pane draws the header block
/// as soon as this answers, whatever the body costs.
/// </para>
/// <para>
/// No octet of a file is here, at any size and in any encoding. What an attachment carries is a route of its own naming
/// the position this response gave it, which is what keeps opening a message the same cost whether the sender attached
/// a note or a video.
/// </para>
/// <para>
/// The pictures the message displays inside its own body are not published here either, and are not a route: the body
/// route resolves each <c>cid:</c> reference against the message's own parts while it reduces the body, so a sender's
/// own images arrive drawn rather than as references a pane would have to fetch — and a remote address is removed
/// there rather than turned into something this surface would resolve.
/// </para>
/// <para>
/// It speaks to no mail server, so a request from a browser cannot wait on IMAP and cannot set the remote <c>\Seen</c>
/// flag. Everything it answers with is read from the local copy through the same use case the tool surface reads a
/// message with, under the same grant.
/// </para>
/// </remarks>
internal static class ClientMailMessageEndpoint
{
    /// <summary>The route reporting one message, relative to the client prefix.</summary>
    internal const string MailMessageRoute = "/messages/{storedEmailId:guid}";

    /// <summary>Maps the route into the client group, so it inherits the group's requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientMailMessage(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(MailMessageRoute, ReadMessageAsync)
            .RequirePermission(MailFathomPermission.MailRead);
    }

    /// <summary>Serves one of the acting user's messages, or reports that there is no such message.</summary>
    /// <param name="storedEmailId">The message to read, as a list row or a conversation published it.</param>
    /// <param name="content">Reads the message from the local copy, for a caller the read's own grant admits.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the message, <c>404</c> where this user has no such message, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.read</c>.</returns>
    /// <remarks>
    /// A message this user does not hold and one no deployment ever held answer identically, so nothing here tells a
    /// caller that somebody else's mail exists. A local copy that is damaged or missing answers the same way as well,
    /// having recorded the repair request the use case records for it.
    /// </remarks>
    internal static async Task<Results<Ok<ClientMailMessageResponse>, NotFound>> ReadMessageAsync(
        [FromRoute] Guid storedEmailId,
        [FromServices] EmailContentReader content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (storedEmailId == Guid.Empty)
        {
            return TypedResults.NotFound();
        }

        var result = await content.ReadContentAsync(RequestFor(storedEmailId), cancellationToken);

        return result.Emails[0].Content is { } message
            ? TypedResults.Ok(ClientMailMessageResponse.For(message))
            : TypedResults.NotFound();
    }

    /// <summary>Composes the read this route makes, which asks for a description and for no representation at all.</summary>
    /// <param name="storedEmailId">The message to read.</param>
    /// <returns>The request the use case is asked with.</returns>
    /// <remarks>
    /// Named rather than inlined for the reason the body route's is: what it declines is the point. It asks for no
    /// markup, no reduced document, and no minted link — the pictures and the words belong to the body route, and a
    /// capability nobody asked for is a bearer credential this response would be handing out. The parse still happens,
    /// because the headers and the file descriptions are read from the stored message rather than from the row.
    /// </remarks>
    internal static GetEmailContentRequest RequestFor(Guid storedEmailId) =>
        GetEmailContentRequest.Create([StoredEmailId.Create(storedEmailId)]) with { Reader = MailboxReaderKind.Person };
}

/// <summary>One message as the client endpoint serves it, without its body and without any file it carries.</summary>
/// <param name="StoredEmailId">The message, as the request named it.</param>
/// <param name="Account">The configured account the message was read from.</param>
/// <param name="Folder">The identity of the folder the message was read from, as the folders route names it.</param>
/// <param name="ThreadId">The conversation the message belongs to, or <see langword="null" /> where nothing has placed it in one.</param>
/// <param name="SizeOctets">The size the mail server reported for the whole message, which no sum over the parts reproduces.</param>
/// <param name="Headers">What the message displays above its body.</param>
/// <param name="Body">Whether there is a body to draw, and which forms of it the sender wrote.</param>
/// <param name="Sender">What this deployment established about the author the message displays.</param>
/// <param name="Signature">What verifying the message's own S/MIME or OpenPGP signature found, beside the verdict and never folded into it.</param>
/// <param name="Attachments">One entry per file the message carries, described and carrying none of what it holds.</param>
/// <param name="Carried">The counts for everything the message carries besides its body, or <see langword="null" /> where nothing has ever read its parts.</param>
/// <param name="Unread">Whether the mail server last showed the message as unseen.</param>
/// <param name="Flagged">Whether the mail server last showed the message as flagged.</param>
/// <param name="Answered">Whether the mail server last showed the message as answered.</param>
/// <param name="Spam">What spam classification concluded about the message, or <see langword="null" /> where nothing has classified it.</param>
/// <remarks>
/// All of it is mail content and personal data, so none of it reaches a log, a span attribute, or a telemetry event,
/// here or anywhere it is carried afterwards.
/// </remarks>
internal sealed record ClientMailMessageResponse(
    Guid StoredEmailId,
    string Account,
    string Folder,
    Guid? ThreadId,
    long SizeOctets,
    ClientMailMessageHeadersResponse Headers,
    ClientMailMessageBodyResponse Body,
    ClientMailSenderVerdictResponse Sender,
    ClientMailSignatureResponse Signature,
    IReadOnlyList<ClientMailAttachmentResponse> Attachments,
    ClientMailCarriedResponse? Carried,
    bool Unread,
    bool Flagged,
    bool Answered,
    ClientMailSpamVerdictResponse? Spam)
{
    /// <summary>Describes one message for the wire.</summary>
    /// <param name="message">The message the use case read.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message" /> is <see langword="null" />.</exception>
    internal static ClientMailMessageResponse For(ReadEmailContent message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new ClientMailMessageResponse(
            message.StoredEmailId.Value,
            message.AccountId.Value,
            message.Folder.Id.ToString(),
            message.Thread?.ThreadId.Value,
            message.SizeOctets,
            ClientMailMessageHeadersResponse.For(message.Headers),
            ClientMailMessageBodyResponse.For(message.Body),
            ClientMailSenderVerdictResponse.For(message.SenderVerification, message.SenderAuthenticationEvidence),
            ClientMailSignatureResponse.For(message.SenderVerification.Signature),
            [.. message.Attachments.Select((attachment, position) =>
                ClientMailAttachmentResponse.For(attachment, position))],
            message.AttachmentSummary is { } carried ? ClientMailCarriedResponse.For(carried) : null,
            !message.RemoteFlags.IsSeen,
            message.RemoteFlags.IsFlagged,
            message.RemoteFlags.IsAnswered,
            ClientMailSpamVerdictResponse.For(message.Spam));
    }
}

/// <summary>The headers one message displays above its body.</summary>
/// <param name="Subject">The decoded subject, or <see langword="null" /> where the message carried none.</param>
/// <param name="SentAt">When the sender says the message was sent, or <see langword="null" /> where it wrote no usable date.</param>
/// <param name="ReceivedAt">When the last receiving hop recorded the message, or <see langword="null" /> where no header carried a usable date.</param>
/// <param name="Participants">Every usable address the message wrote, each paired with the header it appeared in.</param>
/// <param name="MessageId">The message's own identifier without its angle brackets, or <see langword="null" /> where it carried none.</param>
/// <param name="InReplyTo">The identifier of the message this one answers, or <see langword="null" /> where it answers none.</param>
/// <param name="References">The identifiers placing the message in a conversation, in the order the header listed them.</param>
/// <remarks>
/// These are parsed from the stored message rather than read off the columns a list row is served from, which is why a
/// display name, a <c>Bcc</c> the message carried for its own recipient, and the threading identifiers are here and not
/// on a row. A message whose content the size limit kept out of storage carries the narrower set a row can answer for,
/// which is what its body availability says.
/// </remarks>
internal sealed record ClientMailMessageHeadersResponse(
    string? Subject,
    DateTimeOffset? SentAt,
    DateTimeOffset? ReceivedAt,
    IReadOnlyList<ClientMailParticipantResponse> Participants,
    string? MessageId,
    string? InReplyTo,
    IReadOnlyList<string> References)
{
    /// <summary>Describes one message's headers for the wire.</summary>
    /// <param name="headers">The headers the read produced.</param>
    /// <returns>The response body.</returns>
    internal static ClientMailMessageHeadersResponse For(EmailContentHeaders headers) => new(
        headers.Subject,
        headers.SentAt,
        headers.ReceivedAt,
        [.. headers.Participants.Select(ClientMailParticipantResponse.For)],
        headers.ThreadReferences.MessageId,
        headers.ThreadReferences.InReplyTo,
        headers.ThreadReferences.References);
}

/// <summary>One address a message wrote, and the header it wrote it in.</summary>
/// <param name="Role">The header the address appeared in, as the role's own name.</param>
/// <param name="Address">The normalized address.</param>
/// <param name="DisplayName">The name written beside the address, or <see langword="null" /> where none was written.</param>
internal sealed record ClientMailParticipantResponse(string Role, string Address, string? DisplayName)
{
    /// <summary>Describes one participant for the wire.</summary>
    /// <param name="participant">The participant the read produced.</param>
    /// <returns>The response body.</returns>
    internal static ClientMailParticipantResponse For(EmailParticipant participant) => new(
        participant.Role.ToString(),
        participant.Address.Address,
        participant.Address.DisplayName);
}

/// <summary>Whether one message has a body to draw, and which forms of it the sender wrote.</summary>
/// <param name="Availability">Whether the body could be read at all, or why it could not, as the state's own name.</param>
/// <param name="PlainText">Whether the sender wrote a plain-text part of their own.</param>
/// <param name="Html">Whether the sender wrote an HTML part, which is what the body route's document is reduced from.</param>
/// <remarks>
/// It says what the message carried rather than what a request would return, which is the part a client cannot work out
/// for itself: the body route answers with words for every readable message, deriving them from the markup where the
/// sender wrote no text part, so a pane that wanted to know whether there is a richer rendering to draw would be reading
/// a returned representation for a fact it does not carry. Both forms are absent for a body nothing could read, and the
/// availability beside them says which of the reasons applied.
/// </remarks>
internal sealed record ClientMailMessageBodyResponse(string Availability, bool PlainText, bool Html)
{
    /// <summary>Describes one message's body for the wire, without any of it.</summary>
    /// <param name="body">The body the read produced.</param>
    /// <returns>The response body.</returns>
    internal static ClientMailMessageBodyResponse For(EmailContentBody body) => new(
        body.Availability.ToString(),
        body.Forms.PlainText,
        body.Forms.Html);
}

/// <summary>What this deployment established about the author one message displays.</summary>
/// <param name="AuthorAuthentication">What the receiving mail server established about the displayed author, as the outcome's own name.</param>
/// <param name="DeploymentTrust">Whether this deployment recognizes that author, as the level's own name.</param>
/// <param name="AuthenticatedDomain">The domain that actually authenticated, or <see langword="null" /> where none did.</param>
/// <param name="AuthenticationSource">Who reached the authentication verdict — <c>ReceivingServer</c> or <c>LocalVerification</c> — as the source's own name.</param>
/// <param name="DkimDomain">The domain whose DKIM signature passed, or <see langword="null" /> where none did.</param>
/// <param name="SpfDomain">The domain whose SPF check passed, or <see langword="null" /> where none did or none was evaluated.</param>
/// <param name="Dmarc">The DMARC result the verdict was reached with, as the outcome's own name.</param>
/// <param name="TrustSource">What recognized the author — <c>OwnAccountDomain</c>, <c>OrganizationMember</c>, <c>OwnAccountAddress</c>, <c>OrganizationContact</c>, or <c>PersonalContact</c> — or <see langword="null" /> where nothing did.</param>
/// <param name="TrustOrganization">The current display name of the organization whose signature recognized the author, present only where <paramref name="TrustSource" /> is <c>OrganizationMember</c> and that organization still exists.</param>
/// <remarks>
/// <para>
/// The two outcomes are published side by side and are never collapsed into one value, because a screen drawing a single
/// badge from them would have to invent the rule that combines them. One is a fact a receiving server established about
/// the message, and the other is this deployment's own classification of the author it established; an authenticated
/// author nobody has named is the ordinary state of legitimate mail and carries the same trust value as one whose
/// authentication failed outright.
/// </para>
/// <para>
/// The domain travels with them so that a reading pane can name who actually sent a message rather than repeating the
/// <c>From</c> value the message displays, which is the one thing an impersonation gets wrong. It is stated and never
/// judged: whether it differs from the displayed domain says nothing on its own — a provider that signs as itself while
/// the author's own domain passes SPF is authenticated exactly as it appears — so what a reader acts on stays
/// <paramref name="AuthorAuthentication" />, and a client comparing the two would be evaluating a policy this deployment
/// deliberately does not.
/// </para>
/// <para>
/// It is read back as it was stored rather than derived here. Nothing on this path re-reads a header, resolves DNS, or
/// evaluates a policy, so what a reader is shown is what extraction concluded about the authenticated author — never a
/// reading of the <c>From</c> header the message displays. The domain is personal data like the rest of this response.
/// </para>
/// <para>
/// The evidence beside them is what a details popup lists step by step, and it is stated rather than combined: who
/// checked the message, the DKIM and SPF domains that passed, the DMARC result, and what recognized a trusted author.
/// None of it changes what a reader acts on, which stays the two outcomes, and a verdict MailFathom reached itself names
/// no SPF domain and reports no DMARC result because its own check reads DKIM alone. Trust is the source in force when
/// the message was extracted, so a contact written down since reaches mail extracted after it. The organization
/// a member was recognized by is the one exception to reading everything as stored: the verdict recorded which
/// organization it was, and its name is read as it is now, so a rename reaches stored mail and a deletion leaves the
/// source standing without a name.
/// </para>
/// </remarks>
internal sealed record ClientMailSenderVerdictResponse(
    string AuthorAuthentication,
    string DeploymentTrust,
    string? AuthenticatedDomain,
    string AuthenticationSource,
    string? DkimDomain,
    string? SpfDomain,
    string Dmarc,
    string? TrustSource,
    string? TrustOrganization)
{
    /// <summary>Describes one message's sender verdict for the wire.</summary>
    /// <param name="verification">The verdict the read carried.</param>
    /// <param name="evidence">What that verdict was reached from, of which the displayed author's domain is not published.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static ClientMailSenderVerdictResponse For(
        SenderVerification verification,
        SenderAuthenticationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(verification);
        ArgumentNullException.ThrowIfNull(evidence);

        return new ClientMailSenderVerdictResponse(
            verification.AuthorAuthentication.ToString(),
            verification.DeploymentTrust.ToString(),
            evidence.AuthenticatedDomain?.Value,
            evidence.Source.ToString(),
            evidence.DkimDomain?.Value,
            evidence.SpfDomain?.Value,
            evidence.Dmarc.ToString(),
            verification.DeploymentTrustGrantedBy is SenderTrustSource.None
                ? null
                : verification.DeploymentTrustGrantedBy.ToString(),
            verification.DeploymentTrustGrantedBy is SenderTrustSource.OrganizationMember
                ? verification.RecognizingOrganizationName
                : null);
    }
}

/// <summary>What verifying one message's own S/MIME or OpenPGP signature found, step by step.</summary>
/// <param name="Outcome">The verdict the steps add up to — <c>NotSigned</c>, <c>Verified</c>, <c>Failed</c>, or <c>NotVerifiable</c> — as the outcome's own name.</param>
/// <param name="Format">How the message is signed — <c>SecureMimeDetached</c>, <c>SecureMimeOpaque</c>, or <c>OpenPgpMime</c> — or <see langword="null" /> where it is not.</param>
/// <param name="SecureMimeType">The <c>smime-type</c> an opaque S/MIME body declared, or <see langword="null" /> for any other format.</param>
/// <param name="UnverifiableReason">Why the signature is not verifiable here, or <see langword="null" /> where it is not that outcome.</param>
/// <param name="SignedAt">When the signer says they signed, or <see langword="null" /> where the signature does not say.</param>
/// <param name="Steps">Each step's own result, in the order a reader is shown them.</param>
/// <param name="TrustAnchor">What the signer is trusted through — the system store, a configured authority, or a configured key — or <see langword="null" /> where nothing vouched for them.</param>
/// <param name="ChainEnd">The subject of the certificate an S/MIME chain ended at, or <see langword="null" /> where none was built.</param>
/// <param name="ValidityFailure">Which validity or purpose rule the signer's certificate or key broke, or <see langword="null" /> where none.</param>
/// <param name="RevocationMethod">How revocation was established — <c>Ocsp</c>, <c>Crl</c>, or <c>KeyRevocationSignature</c> — or <see langword="null" /> where it was not.</param>
/// <param name="RevocationCheckedAt">When a responder answered the revocation check, or <see langword="null" /> where none did.</param>
/// <param name="RevokedAt">When the signer's certificate or key was revoked, or <see langword="null" /> where it is not.</param>
/// <param name="SignerMatchesAuthor">Whether the signer is the author the message displays, which is a fact of its own rather than part of the outcome.</param>
/// <param name="Signer">Who signed, as their certificate or key names them, or <see langword="null" /> where neither was reached.</param>
/// <remarks>
/// <para>
/// It sits beside the sender verdict and is never folded into it: the verdict is what the receiving server established
/// about the domain the message came from, and this is what the message's own cryptography proves about who signed it
/// and whether the signed content changed since. Neither says the message is safe. A verified signature by somebody
/// other than the displayed author is published as verified with <paramref name="SignerMatchesAuthor" /> false, because
/// the signature itself is sound and the mismatch is the fact a reader needs.
/// </para>
/// <para>
/// It is read back as extraction stored it. Opening a message verifies nothing and reaches no responder, so a revocation
/// answer is the one extraction received and <paramref name="RevocationCheckedAt" /> says when. The signer's identity is
/// personal data like the rest of this response, and the signer's certificate or key itself is not published.
/// </para>
/// </remarks>
internal sealed record ClientMailSignatureResponse(
    string Outcome,
    string? Format,
    string? SecureMimeType,
    string? UnverifiableReason,
    DateTimeOffset? SignedAt,
    ClientMailSignatureStepsResponse Steps,
    string? TrustAnchor,
    string? ChainEnd,
    string? ValidityFailure,
    string? RevocationMethod,
    DateTimeOffset? RevocationCheckedAt,
    DateTimeOffset? RevokedAt,
    bool SignerMatchesAuthor,
    ClientMailSignerResponse? Signer)
{
    /// <summary>Describes one message's signature verification for the wire.</summary>
    /// <param name="signature">The verification the read carried.</param>
    /// <returns>The response body, which is present with <c>NotSigned</c> for a message that carries no signature.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="signature" /> is <see langword="null" />.</exception>
    internal static ClientMailSignatureResponse For(EmailSignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);

        return new ClientMailSignatureResponse(
            signature.Outcome.ToString(),
            signature.Format?.ToString(),
            signature.SecureMimeType?.ToString(),
            signature.UnverifiableReason?.ToString(),
            signature.SignedAt,
            new ClientMailSignatureStepsResponse(
                signature.ContentCheck.ToString(),
                signature.AuthorityCheck.ToString(),
                signature.ValidityCheck.ToString(),
                signature.RevocationCheck.ToString(),
                signature.SignerCheck.ToString()),
            signature.TrustAnchor?.ToString(),
            signature.ChainEnd,
            signature.ValidityFailure?.ToString(),
            signature.RevocationMethod?.ToString(),
            signature.RevocationCheckedAt,
            signature.RevokedAt,
            signature.SignerMatchesAuthor,
            signature.Signer is { } signer ? ClientMailSignerResponse.For(signer) : null);
    }
}

/// <summary>The result of each step a signature verification takes, each as the result's own name.</summary>
/// <param name="Content">Whether the signed content is unchanged since it was signed.</param>
/// <param name="Authority">Whether a trusted certificate authority or a configured key vouches for the signer.</param>
/// <param name="Validity">Whether the signer's certificate or key was valid at signing time and fit for signing mail.</param>
/// <param name="Revocation">Whether the signer's certificate or key is not revoked.</param>
/// <param name="Signer">Whether the signer is the author the message displays.</param>
/// <remarks>
/// Each takes <c>Passed</c>, <c>Failed</c>, <c>NotChecked</c> where the step was reached and could not be completed, or
/// <c>Skipped</c> where an earlier step stopped the verification before it, so a screen draws the steps not reached as
/// well as those that were.
/// </remarks>
internal sealed record ClientMailSignatureStepsResponse(
    string Content,
    string Authority,
    string Validity,
    string Revocation,
    string Signer);

/// <summary>Who signed one message, as their certificate or OpenPGP key names them.</summary>
/// <param name="Name">The certificate's subject or the key's first user ID, or <see langword="null" /> where neither names one.</param>
/// <param name="Address">The address the certificate or key names, preferring the one matching the displayed author, or <see langword="null" /> where none.</param>
/// <param name="Issuer">The certificate's issuer, or <see langword="null" /> for an OpenPGP key.</param>
/// <param name="ValidFrom">When the certificate or key became valid, or <see langword="null" /> where that is unknown.</param>
/// <param name="ValidUntil">When the certificate or key stops being valid, or <see langword="null" /> where it does not expire or that is unknown.</param>
/// <param name="SerialNumber">The certificate's serial number, or <see langword="null" /> for an OpenPGP key.</param>
/// <param name="Fingerprint">The certificate's or key's fingerprint in hexadecimal, or <see langword="null" /> where it was not reached.</param>
/// <param name="KeyId">The OpenPGP key ID in hexadecimal, or <see langword="null" /> for a certificate.</param>
/// <param name="PublicKeyAlgorithm">The signer's public-key algorithm, or <see langword="null" /> where it was not read.</param>
/// <param name="DigestAlgorithm">The digest the signature was computed over, or <see langword="null" /> where it was not read.</param>
internal sealed record ClientMailSignerResponse(
    string? Name,
    string? Address,
    string? Issuer,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidUntil,
    string? SerialNumber,
    string? Fingerprint,
    string? KeyId,
    string? PublicKeyAlgorithm,
    string? DigestAlgorithm)
{
    /// <summary>Describes one signer for the wire.</summary>
    /// <param name="signer">The signer the verification recorded.</param>
    /// <returns>The response body.</returns>
    internal static ClientMailSignerResponse For(EmailSigner signer) => new(
        signer.Name,
        signer.Address,
        signer.Issuer,
        signer.ValidFrom,
        signer.ValidUntil,
        signer.SerialNumber,
        signer.Fingerprint,
        signer.KeyId,
        signer.PublicKeyAlgorithm,
        signer.DigestAlgorithm);
}

/// <summary>What spam classification concluded about one message, and who concluded it.</summary>
/// <param name="Verdict">What was concluded — <c>Spam</c>, <c>NotSpam</c>, or <c>Undetermined</c> — as the verdict's own name.</param>
/// <param name="DecidedBy">Which stage reached it — <c>Deterministic</c> for what the mail server and the message itself established, <c>Model</c>, or <c>Person</c> — as the stage's own name.</param>
/// <param name="SpamSubcategory">What kind of spam the message is, or <see langword="null" /> where no stage named one.</param>
/// <param name="SpamSubcategoryNamedBy">Which stage named the subcategory, <c>Model</c> or <c>Person</c>, or <see langword="null" /> where there is none.</param>
/// <param name="PhishingRequest">What a phishing message asks its reader for, or <see langword="null" /> where the model named nothing.</param>
/// <param name="ImpersonatedParty">The kind of party a phishing or business-email-compromise message pretends to come from, or <see langword="null" /> where the model named nothing.</param>
/// <remarks>
/// <para>
/// The same object on a list row, on a message inside a conversation, on a search result, and on an opened message,
/// read in the query each of them already makes, so a screen draws a badge, a folded row, or a warning without a
/// second request. It is absent for a message no stage has reached, and present with <c>NotSpam</c> for one a stage
/// cleared — the person's own <c>NotSpam</c> included, which no later stage replaces.
/// </para>
/// <para>
/// The two stages are published apart because they can differ: the model names the subcategory of a verdict the
/// deterministic stage reached. The subcategory carries <c>spam</c> in its name because it is not a mail category and
/// must not be read as one.
/// </para>
/// <para>
/// Every value is a closed one published as its member's name, so nothing here is text a model or a sender wrote — the
/// impersonated party is the kind of party, never a name — and no egress guard stands between it and the client. A
/// screen that wants a sentence composes it from the value.
/// </para>
/// </remarks>
internal sealed record ClientMailSpamVerdictResponse(
    string Verdict,
    string DecidedBy,
    string? SpamSubcategory,
    string? SpamSubcategoryNamedBy,
    string? PhishingRequest,
    string? ImpersonatedParty)
{
    /// <summary>Describes one message's spam verdict for the wire, or nothing where none is recorded.</summary>
    /// <param name="spam">The verdict the read carried, or <see langword="null" /> where nothing has classified the message.</param>
    /// <returns>The response body, or <see langword="null" /> where <paramref name="spam" /> is.</returns>
    internal static ClientMailSpamVerdictResponse? For(EmailSpamVerdict? spam) => spam is null
        ? null
        : new ClientMailSpamVerdictResponse(
            spam.Verdict.ToString(),
            spam.DecidedBy.ToString(),
            spam.Subcategory?.ToString(),
            spam.SubcategoryNamedBy?.ToString(),
            spam.PhishingRequest?.ToString(),
            spam.ImpersonatedParty?.ToString());
}

/// <summary>One file a message carries, described and carrying none of what it holds.</summary>
/// <param name="Position">The zero-based place the file holds in this list, which is what the attachment route is asked with.</param>
/// <param name="FileName">The normalized file name, or <see langword="null" /> where the part carried no usable name.</param>
/// <param name="WasFileNameNormalized">Whether normalization had to rewrite what the message wrote.</param>
/// <param name="MediaType">What the part declares itself to be, which is what the sender wrote rather than a reading of the content.</param>
/// <param name="SizeOctets">How many octets the file holds once its transfer encoding is decoded, which is what the download returns.</param>
/// <remarks>
/// <para>
/// The position is the identity because it is the only stable one a message's parts have: MIME gives an attachment no
/// identifier, a <c>Content-ID</c> is optional and sender-chosen, and a file name is neither unique nor required. It is
/// this list's own order, which is the order the message's structure is walked, and the same order the attachment route
/// resolves a position against.
/// </para>
/// <para>
/// A file name is text a sender chose. It arrives normalized to a bare name — never a path, never a traversal segment,
/// never a control character — and <c>wasFileNameNormalized</c> says whether that rewrote anything, which is exactly the
/// case worth drawing carefully.
/// </para>
/// </remarks>
internal sealed record ClientMailAttachmentResponse(
    int Position,
    string? FileName,
    bool WasFileNameNormalized,
    string MediaType,
    long SizeOctets)
{
    /// <summary>Describes one attachment for the wire.</summary>
    /// <param name="attachment">The attachment the read described.</param>
    /// <param name="position">The place it holds in the read's own order.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="attachment" /> is <see langword="null" />.</exception>
    internal static ClientMailAttachmentResponse For(ReadEmailAttachment attachment, int position)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        return new ClientMailAttachmentResponse(
            position,
            attachment.Description.FileName?.Value,
            attachment.Description.FileName?.WasNormalized ?? false,
            attachment.Description.MediaType,
            attachment.Description.DecodedSizeOctets);
    }
}

/// <summary>The counts for everything one message carries besides its body.</summary>
/// <param name="AttachmentCount">How many parts are files a person would open.</param>
/// <param name="TotalSizeOctets">The decoded octets of those files together.</param>
/// <param name="InlineResourceCount">How many parts are resources the body embeds, which the body route draws inside the message rather than listing here.</param>
/// <param name="Encrypted">Whether the message carries encrypted content somewhere.</param>
/// <param name="UnverifiedSignature">Whether it carries a signature part anywhere, which says nothing about whether the message's own signature verified; <c>signature</c> says that.</param>
/// <param name="UnexpandedTnefPart">Whether it carries a TNEF <c>winmail.dat</c> part that was recorded without being expanded.</param>
/// <remarks>
/// The counts are separate values because a message whose only non-body parts are inline resources or a signature
/// carries no attachments: collapsing them would draw a paperclip on every signed message and on every message with a
/// logo in its signature block.
/// </remarks>
internal sealed record ClientMailCarriedResponse(
    int AttachmentCount,
    long TotalSizeOctets,
    int InlineResourceCount,
    bool Encrypted,
    bool UnverifiedSignature,
    bool UnexpandedTnefPart)
{
    /// <summary>Describes what a message carries for the wire.</summary>
    /// <param name="carried">The counts the read produced.</param>
    /// <returns>The response body.</returns>
    internal static ClientMailCarriedResponse For(StoredEmailAttachmentSummary carried) => new(
        carried.AttachmentCount,
        carried.TotalSizeOctets,
        carried.InlineResourceCount,
        carried.IsEncrypted,
        carried.CarriesUnverifiedSignature,
        carried.ContainsUnexpandedTnefPart);
}
