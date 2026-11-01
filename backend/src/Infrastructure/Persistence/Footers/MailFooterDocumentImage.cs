// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Infrastructure.Persistence.Footers;

/// <summary>One image as a footer document holds it.</summary>
/// <param name="Name">The name the markup refers to it by.</param>
/// <param name="MediaType">Its media type.</param>
/// <param name="Content">Its octets.</param>
[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "The serializer writes a byte array as base64, which is the form the document holds.")]
internal sealed record MailFooterDocumentImage(string Name, string MediaType, byte[] Content);
