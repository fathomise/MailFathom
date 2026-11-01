// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.RegularExpressions;

namespace MailFathom.Application.Mail.Delivery.Footers;

/// <summary>A footer as it was written and stored: a user's own for one account, or an organization's template.</summary>
/// <param name="Html">The sanitized markup, which a template still carries its variables in.</param>
/// <param name="Images">The images uploaded with it, each referred to from the markup by <c>cid:</c> and its name.</param>
public sealed partial record MailFooterContent(string Html, IReadOnlyList<MailFooterImage> Images)
{
    /// <summary>The longest markup a footer is written in, in characters.</summary>
    public const int MaximumHtmlLength = 16 * 1024;

    /// <summary>The most images one footer carries.</summary>
    public const int MaximumImageCount = 5;

    /// <summary>The largest one image may be, in octets.</summary>
    /// <remarks>Every message the footer goes out on carries every image, so the bound is about everybody's mail rather than one upload.</remarks>
    public const int MaximumImageBytes = 256 * 1024;

    /// <summary>The image types a footer carries: the three every mail client draws.</summary>
    public static IReadOnlySet<string> AcceptedImageMediaTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg", "image/gif" };

    /// <summary>Reads what somebody wrote as a footer, sanitizing its markup, or refuses it naming what to change.</summary>
    /// <param name="html">The markup as it was written.</param>
    /// <param name="images">The images uploaded with it.</param>
    /// <param name="markup">Sanitizes the markup and checks what its images refer to.</param>
    /// <param name="refusal">Why it was refused, when the answer is <see langword="null" />.</param>
    /// <returns>The footer, or <see langword="null" /> when it was refused.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public static MailFooterContent? Read(
        string html,
        IReadOnlyList<MailFooterImage> images,
        IMailFooterMarkup markup,
        out MailFooterRefusal? refusal)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(markup);

        refusal = FindBoundRefusal(html, images);
        if (refusal is not null)
        {
            return null;
        }

        var reading = markup.Sanitize(html, images.Select(static image => image.Name).ToHashSet(StringComparer.Ordinal));
        refusal = reading.Refusal;

        return reading.Html is { } sanitized ? new MailFooterContent(sanitized, images) : null;
    }

    /// <summary>Reports whether a name is one an image is referred to by.</summary>
    /// <param name="name">The name.</param>
    /// <returns><see langword="true" /> for one to sixty-four letters, digits, dots, hyphens, and underscores, opening with a letter or a digit.</returns>
    /// <remarks>A name ends up in a <c>cid:</c> reference and in a MIME header, so it is held to characters neither needs to escape.</remarks>
    public static bool IsImageName(string? name) => name is not null && ImageNamePattern().IsMatch(name);

    private static MailFooterRefusal? FindBoundRefusal(string html, IReadOnlyList<MailFooterImage> images)
    {
        if (html.Length > MaximumHtmlLength)
        {
            return new MailFooterRefusal(MailFooterRefusalReason.MarkupTooLong);
        }

        if (images.Count > MaximumImageCount)
        {
            return new MailFooterRefusal(MailFooterRefusalReason.TooManyImages);
        }

        if (images.Any(static image => !IsImageName(image.Name)))
        {
            return new MailFooterRefusal(MailFooterRefusalReason.ImageNameNotAccepted);
        }

        if (images.GroupBy(static image => image.Name, StringComparer.Ordinal).FirstOrDefault(static group => group.Count() > 1) is { } repeated)
        {
            return new MailFooterRefusal(MailFooterRefusalReason.ImageNameRepeated, repeated.Key);
        }

        if (images.FirstOrDefault(static image => image.Content.IsEmpty || image.Content.Length > MaximumImageBytes) is { } sized)
        {
            return new MailFooterRefusal(MailFooterRefusalReason.ImageSizeNotAccepted, sized.Name);
        }

        return images.FirstOrDefault(static image => !AcceptedImageMediaTypes.Contains(image.MediaType)) is { } typed
            ? new MailFooterRefusal(MailFooterRefusalReason.ImageTypeNotAccepted, typed.Name)
            : null;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ImageNamePattern();
}
