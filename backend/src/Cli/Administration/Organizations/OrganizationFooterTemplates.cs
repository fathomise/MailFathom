// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Organizations;

/// <summary>An organization's footer template as a write sends it, and as a read answers with it.</summary>
/// <param name="Html">The markup, naming its variables as <c>{{name}}</c> and its images as <c>cid:</c> followed by their names.</param>
/// <param name="Images">The images uploaded with it.</param>
internal sealed record OrganizationFooterTemplate(
    [property: JsonPropertyName("html")] string Html,
    [property: JsonPropertyName("images")] IReadOnlyList<OrganizationFooterTemplateImage> Images);

/// <summary>One image of a footer template.</summary>
/// <param name="Name">The name the markup refers to it by.</param>
/// <param name="MediaType">Its media type.</param>
/// <param name="Content">Its octets, which the wire carries as base64.</param>
[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "The serializer writes a byte array as base64, which is the form the wire carries.")]
internal sealed record OrganizationFooterTemplateImage(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("mediaType")] string MediaType,
    [property: JsonPropertyName("content")] byte[] Content);

/// <summary>What a read of an organization's footer template answers with.</summary>
/// <param name="Template">The template, or <see langword="null" /> where none is set.</param>
internal sealed record OrganizationFooterTemplateReading(
    [property: JsonPropertyName("template")] OrganizationFooterTemplate? Template);
