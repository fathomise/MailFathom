// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Delivery.Receipts;

/// <summary>Carries what a stored message recognized as a report said, and what it said it is about.</summary>
/// <param name="Kind">Which of the two reports the message is.</param>
/// <param name="Recipients">What the report said about each recipient it named, in the order it named them.</param>
/// <remarks>
/// <para>
/// A report is a message anybody could have written: nothing authenticates the server or the mail client that claims to
/// have written it. So what it says is recorded as information, and is applied only to a message of the receiving
/// account's own that it names — never to anything else.
/// </para>
/// <para>
/// The two identifiers below are the report's own claims about which message it is about, read in that order before the
/// report's <c>In-Reply-To</c> and <c>References</c>. Either may be absent: a server ignores an envelope identifier it
/// was never given, and a delivery report carries no <c>Original-Message-ID</c> field at all.
/// </para>
/// </remarks>
public sealed record ReceiptReport(ReceiptKind Kind, IReadOnlyList<ReceiptReportRecipient> Recipients)
{
    /// <summary>The most recipients one report is read for; the rest of a longer one are not recorded.</summary>
    public const int MaximumRecipients = 100;

    /// <summary>Gets the <c>Original-Envelope-Id</c> a delivery report carried, or <see langword="null" /> where it carried none.</summary>
    public string? OriginalEnvelopeId { get; init; }

    /// <summary>Gets the <c>Original-Message-ID</c> a read receipt carried, without its angle brackets, or <see langword="null" /> where it carried none.</summary>
    public string? OriginalMessageId { get; init; }
}
