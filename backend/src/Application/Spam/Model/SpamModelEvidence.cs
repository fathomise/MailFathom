// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Spam.Model;

/// <summary>Everything the model stage reads about one message, and nothing more.</summary>
/// <remarks>
/// <para>
/// The minimised input ADR 0041 decides: the sender and a <c>Reply-To</c> that differs from it, the subject, the
/// plain-text body cut to the deployment's bound, the host each link goes to beside the host its words name, each
/// attachment's declared type and name, and the facts the deterministic stage read that a message cannot forge — each
/// sender-authentication outcome, and whether the sender is one this deployment trusts. The <c>To</c> and <c>Cc</c>
/// lists, the link paths and queries, attachment content, and the deterministic verdict itself are not here, so no
/// adapter can send them.
/// </para>
/// <para>
/// It is somebody's mail and leaves the deployment once the egress guard has read it, so it is never logged.
/// </para>
/// </remarks>
public sealed record SpamModelEvidence
{
    /// <summary>The most links one message's evidence carries, past which the rest are dropped.</summary>
    /// <remarks>A message with more links than this is a newsletter or a payload, and what decides it is in the first of them.</remarks>
    public const int MaximumLinks = 32;

    /// <summary>The most attachments one message's evidence describes.</summary>
    public const int MaximumAttachments = 16;

    /// <summary>The longest attachment file name the evidence keeps, past which it is cut.</summary>
    public const int MaximumFileNameLength = 128;

    /// <summary>Gets the address of the author the message displays, or <see langword="null" /> where it carried no usable one.</summary>
    public required string? SenderAddress { get; init; }

    /// <summary>Gets the name the message displays beside its author's address, or <see langword="null" /> where it displays none.</summary>
    public required string? SenderDisplayName { get; init; }

    /// <summary>Gets the reply address, present only where it differs from <see cref="SenderAddress" />.</summary>
    public required string? DifferingReplyToAddress { get; init; }

    /// <summary>Gets the subject, or <see langword="null" /> where the message carried none.</summary>
    public required string? Subject { get; init; }

    /// <summary>Gets the plain-text body, cut to the deployment's bound.</summary>
    public required string Body { get; init; }

    /// <summary>Gets whether <see cref="Body" /> was cut.</summary>
    public required bool IsBodyCut { get; init; }

    /// <summary>Gets where each link goes, at most <see cref="MaximumLinks" /> of them.</summary>
    public required IReadOnlyList<SpamModelLink> Links { get; init; }

    /// <summary>Gets each attachment's declared type and name, at most <see cref="MaximumAttachments" /> of them.</summary>
    public required IReadOnlyList<SpamModelAttachment> Attachments { get; init; }

    /// <summary>Gets each sender-authentication outcome the receiving server recorded, as the deterministic stage read it.</summary>
    public required IReadOnlyList<SpamModelAuthenticationOutcome> SenderAuthentication { get; init; }

    /// <summary>Gets whether this deployment recognizes the message's authenticated author.</summary>
    public required bool IsSenderTrusted { get; init; }
}

/// <summary>Where one link goes, and what its own words claimed about that.</summary>
/// <param name="TargetHost">The host the link goes to, in its ASCII form.</param>
/// <param name="DisplayedHost">The host the link's own words name, or <see langword="null" /> where they name none.</param>
/// <param name="DisplayedHostDiffers">Whether the words name a different host from the one the link goes to.</param>
/// <remarks>The path and the query are not here: they often carry a token identifying the recipient.</remarks>
public sealed record SpamModelLink(string TargetHost, string? DisplayedHost, bool DisplayedHostDiffers);

/// <summary>What one attachment declared itself to be.</summary>
/// <param name="MediaType">The content type the message declared.</param>
/// <param name="FileName">The file name the message declared, cut to <see cref="SpamModelEvidence.MaximumFileNameLength" />, or <see langword="null" /> where it declared none.</param>
public sealed record SpamModelAttachment(string MediaType, string? FileName);

/// <summary>One sender-authentication outcome the receiving server recorded.</summary>
/// <param name="Method">The method, such as <c>spf</c>, <c>dkim</c>, or <c>dmarc</c>.</param>
/// <param name="Result">The result the server recorded, such as <c>pass</c> or <c>fail</c>.</param>
/// <param name="IsForwarded">Whether the result was preserved across a forwarding hop rather than recorded by the receiving server.</param>
/// <remarks>The property detail the server wrote beside a result is not here, because it can name a sending address.</remarks>
public sealed record SpamModelAuthenticationOutcome(string Method, string Result, bool IsForwarded);
