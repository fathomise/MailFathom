// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Spam;

/// <summary>Names what a phishing message asks its reader to hand over.</summary>
/// <remarks>
/// A closed value rather than text, because the model that names it chooses among options and writes nothing: whatever
/// the message says it wants, the record keeps only which of these it was.
/// </remarks>
public enum SpamPhishingRequest
{
    /// <summary>A password, or a sign-in to a page that collects one.</summary>
    Credentials = 0,

    /// <summary>A card number, bank details, or a payment.</summary>
    PaymentDetails = 1,

    /// <summary>A code sent by text message, an authenticator, or another second factor.</summary>
    OneTimeCode = 2,

    /// <summary>Something none of the other values describes.</summary>
    Other = 3,

    /// <summary>The message names nothing it asks for.</summary>
    NothingStated = 4,
}
