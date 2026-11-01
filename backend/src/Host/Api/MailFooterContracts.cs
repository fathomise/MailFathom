// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Serialization;
using MailFathom.Application.Mail.Delivery.Footers;

namespace MailFathom.Host.Api;

/// <summary>A footer or a template as a write states it: its markup and every image uploaded with it.</summary>
/// <param name="Html">The markup, referring to each image as <c>cid:</c> followed by its name.</param>
/// <param name="Images">The images, or <see langword="null" /> for none.</param>
/// <remarks>Shared by the client's own footer and the administrator's template, which are written in one shape.</remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record MailFooterRequest(string? Html = null, IReadOnlyList<MailFooterImageRequest>? Images = null)
{
    /// <summary>The greatest request body a footer write reads before refusing it.</summary>
    /// <remarks>
    /// The most images at their largest, written as base64, and the longest markup at the widest JSON escaping of each
    /// character, with room for the names and the structure around them.
    /// </remarks>
    internal const int MaxRequestBytes = 2 * 1024 * 1024;

    /// <summary>Reads the body's images, or says why it does not state a footer at all.</summary>
    /// <param name="refusal">Why it does not, when the answer is <see langword="null" />.</param>
    /// <returns>The images, or <see langword="null" />.</returns>
    internal IReadOnlyList<MailFooterImage>? Stated(out string? refusal)
    {
        if (this.Html is null)
        {
            refusal = "A footer states its markup.";

            return null;
        }

        if ((this.Images ?? []).Any(static image => image is null || image.Name is null || image.MediaType is null || image.Content is null))
        {
            refusal = "Each footer image states its name, its media type, and its content.";

            return null;
        }

        refusal = null;

        return [.. (this.Images ?? []).Select(static image => new MailFooterImage(image.Name!, image.MediaType!, image.Content!))];
    }
}

/// <summary>One image a footer write uploads.</summary>
/// <param name="Name">The name the markup refers to it by.</param>
/// <param name="MediaType">Its media type: <c>image/png</c>, <c>image/jpeg</c>, or <c>image/gif</c>.</param>
/// <param name="Content">Its octets, written as base64.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "The serializer reads base64 into a byte array, which is the form the wire carries.")]
internal sealed record MailFooterImageRequest(string? Name = null, string? MediaType = null, byte[]? Content = null);

/// <summary>A footer or a template as it was saved, its markup sanitized.</summary>
/// <param name="Html">The sanitized markup.</param>
/// <param name="Images">The images.</param>
internal sealed record MailFooterResponse(string Html, IReadOnlyList<MailFooterImageResponse> Images)
{
    /// <summary>Describes a footer on the wire.</summary>
    /// <param name="footer">The footer.</param>
    /// <returns>The response body.</returns>
    internal static MailFooterResponse For(MailFooterContent footer) => new(
        footer.Html,
        [.. footer.Images.Select(static image => new MailFooterImageResponse(image.Name, image.MediaType, image.Content.ToArray()))]);
}

/// <summary>One image of a footer as it was saved.</summary>
/// <param name="Name">The name the markup refers to it by.</param>
/// <param name="MediaType">Its media type.</param>
/// <param name="Content">Its octets, written as base64.</param>
[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "The serializer writes a byte array as base64, which is the form the wire carries.")]
internal sealed record MailFooterImageResponse(string Name, string MediaType, byte[] Content);

/// <summary>Says what was wrong with a footer or a template that was not saved, in the words a writer acts on.</summary>
internal static class MailFooterRefusals
{
    /// <summary>Describes one refusal.</summary>
    /// <param name="refusal">The refusal.</param>
    /// <returns>The sentence.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the reason is not one this build declares.</exception>
    internal static string Describe(MailFooterRefusal refusal) => refusal.Reason switch
    {
        MailFooterRefusalReason.MarkupTooLong => string.Create(
            CultureInfo.InvariantCulture,
            $"A footer's markup is at most {MailFooterContent.MaximumHtmlLength} characters long."),
        MailFooterRefusalReason.TooManyImages => string.Create(
            CultureInfo.InvariantCulture,
            $"A footer carries at most {MailFooterContent.MaximumImageCount} images."),
        MailFooterRefusalReason.ImageSizeNotAccepted => string.Create(
            CultureInfo.InvariantCulture,
            $"Image '{refusal.Subject}' is empty or larger than {MailFooterContent.MaximumImageBytes} bytes."),
        MailFooterRefusalReason.ImageTypeNotAccepted =>
            $"Image '{refusal.Subject}' is not a PNG, a JPEG, or a GIF image, which are the images a footer carries.",
        MailFooterRefusalReason.ImageNameNotAccepted =>
            "An image is named by one to sixty-four letters, digits, dots, hyphens, and underscores, opening with a letter or a digit.",
        MailFooterRefusalReason.ImageNameRepeated => $"Two images are named '{refusal.Subject}'.",
        MailFooterRefusalReason.ImageNotUploaded =>
            "A footer image has to be uploaded with the footer and referred to as 'cid:' followed by its name; an address or a data: URI is not carried.",
        MailFooterRefusalReason.ImageUnknown => refusal.Subject is { } name
            ? $"The markup refers to image '{name}', which was not uploaded with it."
            : "The markup refers to an image that was not uploaded with it.",
        MailFooterRefusalReason.VariableUnknown =>
            $"The template names variable '{refusal.Subject}', which is not one of {string.Join(", ", MailFooterTemplate.Variables)}.",
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal.Reason, "The footer refusal reason is not one this build declares."),
    };
}
