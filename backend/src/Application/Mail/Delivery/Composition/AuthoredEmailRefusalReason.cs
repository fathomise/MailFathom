// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.Delivery.Composition;

/// <summary>States what stopped a message from being composed.</summary>
/// <remarks>
/// Composing is decided entirely from what the author wrote, the account's configuration, and what the submission
/// server advertised, so every way it can end badly is one of these — which is what lets a caller act on the answer
/// rather than translate an exception. A failure this does not name is a defect: a composer that cannot build a message
/// it accepted has a fault of its own and raises rather than refuses.
/// </remarks>
public enum AuthoredEmailRefusalReason
{
    /// <summary>The sending account configures no address to send from.</summary>
    SenderUnconfigured = 0,

    /// <summary>An author-supplied field carries a line break, which would smuggle a header nobody wrote.</summary>
    HeaderInjected = 1,

    /// <summary>An author-supplied field carries a value no message can be composed from, such as an address naming no mailbox.</summary>
    FieldUnusable = 2,

    /// <summary>An address outside ASCII was addressed to a server that carries none.</summary>
    InternationalizationUnsupported = 3,

    /// <summary>The message exceeds a bound the deployment configured or the submission server advertised.</summary>
    BoundExceeded = 4,

    /// <summary>A picture is named by text the composer never mints, or two pictures share one name.</summary>
    PictureNameUnusable = 5,

    /// <summary>The HTML alternative refers by <c>cid:</c> to a picture the message does not carry.</summary>
    PictureReferenceUnknown = 6,

    /// <summary>A picture the message carries is referred to nowhere in the HTML alternative.</summary>
    PictureUnreferenced = 7,

    /// <summary>A picture's octets open as none of PNG, JPEG, and GIF.</summary>
    PictureTypeNotAccepted = 8,

    /// <summary>One picture holds more octets than a reopened draft draws inline.</summary>
    PictureTooLarge = 9,

    /// <summary>The message's pictures together hold more octets than one message carries.</summary>
    PicturesTooLarge = 10,

    /// <summary>The message carries more pictures than one message may.</summary>
    TooManyPictures = 11,
}
