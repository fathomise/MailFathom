// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Emails.Signatures;

namespace MailFathom.Application.Emails.Summaries;

/// <summary>The conclusions a stored email carries about the author it displays, read back as they were stored, beside the current name of the organization that recognized the author.</summary>
/// <remarks>
/// <para>
/// The first two are never collapsed into one value. <see cref="AuthorAuthentication" /> is what the receiving mail server
/// established about the displayed author, and <see cref="DeploymentTrust" /> is whether this deployment recognizes the
/// author it established — a fact about the message beside a decision about a list. An authenticated author nobody has
/// named is the ordinary state of legitimate mail and carries the same trust value as an author whose authentication
/// failed, so a reader that had only one of the two could not tell those apart.
/// </para>
/// <para>
/// <see cref="Signature" /> is a third fact beside them rather than an input to either: what verifying the message's own
/// S/MIME or OpenPGP signature found. It proves who signed and that the signed content is unchanged, never that the
/// message is safe, and a verified signature by somebody other than the displayed author says so in its own step.
/// </para>
/// <para>
/// It is read rather than reached. Nothing on a read path evaluates a policy, resolves DNS, or re-reads a header to
/// produce it: the values are the columns extraction wrote, and a message extraction never reached carries
/// <see cref="NotEstablished" /> because that is what its row holds. The one exception is
/// <see cref="RecognizingOrganizationName" />, which is read from the organization the row refers to rather than
/// written by extraction, so a rename reaches mail already stored.
/// </para>
/// </remarks>
public sealed record SenderVerification
{
    /// <summary>Gets the conclusions a row carries where nothing has established an author and no policy has judged one.</summary>
    /// <remarks>
    /// It is the stored default rather than an absence, which is what mail stored before the columns existed reads as
    /// until a re-derivation reaches it. Publishing it as it stands is deliberate: inventing a third state for
    /// "unfilled" would say something about the message that nothing established.
    /// </remarks>
    public static SenderVerification NotEstablished { get; } = new()
    {
        AuthorAuthentication = AuthorAuthenticationOutcome.NotEstablished,
        DeploymentTrust = SenderTrustLevel.Unknown,
    };

    /// <summary>Gets what the receiving mail server established about the author the message displays.</summary>
    public required AuthorAuthenticationOutcome AuthorAuthentication { get; init; }

    /// <summary>Gets whether this deployment recognizes the author, which is its own classification and not a check.</summary>
    public required SenderTrustLevel DeploymentTrust { get; init; }

    /// <summary>Gets what recognized the author, which is <see cref="SenderTrustSource.None" /> wherever nothing did.</summary>
    /// <remarks>
    /// It is the source in force when the message was extracted: a contact written down since reaches mail extracted
    /// after it, so this names what held then, and removing the contact it names resets it to not evaluated.
    /// </remarks>
    public SenderTrustSource DeploymentTrustGrantedBy { get; init; } = SenderTrustSource.None;

    /// <summary>Gets what tied a trusted message to the address its source recognized, which is <see cref="TrustedAuthorEvidence.None" /> wherever nothing was asked of the message.</summary>
    /// <remarks>
    /// The domain's own checks, or the author's own verified S/MIME or OpenPGP signature where those did not hold. A
    /// verdict the organization's signature reached asks for neither, so it carries none.
    /// </remarks>
    public TrustedAuthorEvidence DeploymentTrustAuthorEvidence { get; init; } = TrustedAuthorEvidence.None;

    /// <summary>Gets the contact that recognized the author, or <see langword="null" /> where no contact did.</summary>
    /// <remarks>
    /// Set only where <see cref="DeploymentTrustGrantedBy" /> names a contact source. It is a reference rather than the
    /// record, found in the books as they stood when this message was read rather than stored with it, so it never
    /// names a contact that was gone by then. Which book it is in is the reader's to ask; nothing of the record itself
    /// travels here.
    /// </remarks>
    public ContactId? RecognizingContactId { get; init; }

    /// <summary>Gets the current display name of the organization whose signature recognized the author, or <see langword="null" /> where none did or it has since been deleted.</summary>
    /// <remarks>
    /// The one value here that is not read back as it was stored. The verdict records the organization rather than its
    /// name, so this is that organization's name as it reads now: a rename reaches mail already stored, and a deletion
    /// leaves <see cref="DeploymentTrustGrantedBy" /> naming the organization source with no name beside it. It is
    /// personal data like the rest of the verdict.
    /// </remarks>
    public string? RecognizingOrganizationName { get; init; }

    /// <summary>Gets what verifying the message's own signature found, which is not signed for every message that carries none.</summary>
    /// <remarks>Mail stored before signatures were verified reads as not signed until a re-derivation reaches it.</remarks>
    public EmailSignature Signature { get; init; } = EmailSignature.NotSigned;
}
