// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.Delivery.Footers;

/// <summary>One image a footer carries, uploaded with it and embedded in every message that carries the footer.</summary>
/// <param name="Name">The name the footer's markup refers to the image by, as <c>cid:</c> followed by the name.</param>
/// <param name="MediaType">The image's media type, one of <see cref="MailFooterContent.AcceptedImageMediaTypes" />.</param>
/// <param name="Content">The image's octets.</param>
/// <remarks>
/// An image is never linked. A footer image is part of every message its author sends, so a remote one would tell its
/// host who opened which message; an embedded one is fetched from nowhere.
/// </remarks>
public sealed record MailFooterImage(string Name, string MediaType, ReadOnlyMemory<byte> Content);
