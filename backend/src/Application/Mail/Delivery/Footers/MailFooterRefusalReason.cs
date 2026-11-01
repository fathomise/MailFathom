// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.Delivery.Footers;

/// <summary>What was wrong with a footer or a template that was not saved.</summary>
public enum MailFooterRefusalReason
{
    /// <summary>The markup is longer than <see cref="MailFooterContent.MaximumHtmlLength" />.</summary>
    MarkupTooLong = 0,

    /// <summary>More images than <see cref="MailFooterContent.MaximumImageCount" />.</summary>
    TooManyImages = 1,

    /// <summary>An image is empty or larger than <see cref="MailFooterContent.MaximumImageBytes" />.</summary>
    ImageSizeNotAccepted = 2,

    /// <summary>An image is not of one of <see cref="MailFooterContent.AcceptedImageMediaTypes" />, or its octets do not open as the type it declares.</summary>
    ImageTypeNotAccepted = 3,

    /// <summary>An image's name is not of the form <see cref="MailFooterContent.IsImageName" /> accepts.</summary>
    ImageNameNotAccepted = 4,

    /// <summary>Two images carry one name.</summary>
    ImageNameRepeated = 5,

    /// <summary>The markup refers to an image by an address or a <c>data:</c> URI rather than to an uploaded one.</summary>
    ImageNotUploaded = 6,

    /// <summary>The markup refers by <c>cid:</c> to a name no uploaded image carries.</summary>
    ImageUnknown = 7,

    /// <summary>A template names a variable <see cref="MailFooterTemplate" /> does not fill.</summary>
    VariableUnknown = 8,
}
