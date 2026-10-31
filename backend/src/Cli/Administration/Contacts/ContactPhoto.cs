// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Contacts;

/// <summary>A contact's photo as the export hands it back.</summary>
/// <param name="MediaType">The kind of image the octets are.</param>
/// <param name="Content">The octets, base64-encoded exactly as the deployment answered them, so the command prints them unchanged.</param>
internal sealed record ContactPhoto(
    [property: JsonPropertyName("mediaType")] string MediaType,
    [property: JsonPropertyName("content")] string Content);
