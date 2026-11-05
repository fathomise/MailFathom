// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.Mail.Delivery.Footers;

namespace MailFathom.Application.Mail.Delivery.Composition;

/// <summary>Names one picture an author placed inside the message, with the octets it is made of.</summary>
/// <param name="Name">The name the HTML alternative refers to the picture by, as <c>cid:</c> followed by the name.</param>
/// <param name="Content">The octets of the picture.</param>
/// <remarks>
/// <para>
/// A picture travels as a part of the <c>multipart/related</c> the HTML alternative is the root of, never as a
/// <c>data:</c> URI inside the markup, which the body bound measures and several mail clients refuse. The name is the
/// author's own handle for it, minted by the composer, and it never becomes a header: the composition mints the content
/// identifier the part goes out under and rewrites the markup's reference to it, exactly as a footer's images are
/// embedded, so a footer appended later shares the same <c>multipart/related</c> without a collision.
/// </para>
/// <para>
/// There is no declared media type. What the picture is gets read from the signature its octets open with, so nothing
/// an author wrote decides the type a recipient's client is told, and a picture that opens as none of the accepted
/// three is refused rather than sent under a guess.
/// </para>
/// <para>
/// The bounds are this deployment's own rather than the operator's, and the composer's paste-time refusal only mirrors
/// them: a picture the send accepts is one a reopened draft still draws, and all of one message's pictures together,
/// with the largest footer beside them, stay inside what one reduced document carries.
/// </para>
/// </remarks>
public sealed record AuthoredEmailPicture(string Name, ReadOnlyMemory<byte> Content)
{
    /// <summary>The most pictures one message carries.</summary>
    public const int MaximumCount = 16;

    /// <summary>The most octets all of one message's pictures may hold together.</summary>
    /// <remarks>
    /// With a footer's largest — <see cref="MailFooterContent.MaximumImageCount" /> images of
    /// <see cref="MailFooterContent.MaximumImageBytes" /> — the sent copy stays under
    /// <see cref="MailDocumentBounds.MaximumInlineImageOctetsPerDocument" />, so the author's own sent message draws
    /// everything it carries.
    /// </remarks>
    public const int MaximumTotalOctets = (2 * 1024 * 1024) + (512 * 1024);

    /// <summary>Gets the most octets one picture may hold, which is the most a reopened draft draws inline.</summary>
    public static int MaximumOctets => MailDocumentBounds.Default.MaximumInlineImageOctets;
}
