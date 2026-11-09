// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MailFathom.Domain.Categories;

namespace MailFathom.Host.Configuration.Categories;

/// <summary>One subject an account names for itself, and how a model recognizes it.</summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The configuration binder materializes this type when a user's document is read.")]
internal sealed class MailAccountOwnCategoryOptions
{
    /// <summary>Gets or sets what this category is called, which is also what its stored records name it by.</summary>
    /// <remarks>Refused with white space around it, so the name written is the one form a record can name.</remarks>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets what a model reads to decide whether a message is about this subject.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Finds what is wrong with this entry.</summary>
    /// <param name="path">The entry's path within the account record, which a refusal names it by.</param>
    /// <returns>One result per refusal.</returns>
    internal IEnumerable<ValidationResult> FindRefusals(string path)
    {
        if (ReasonUnusable(this.Name, OwnMailCategoryBounds.MaximumNameLength) is { } name)
        {
            yield return new ValidationResult(
                $"{path}:{nameof(this.Name)} {name}.",
                [nameof(this.Name)]);
        }

        if (ReasonUnusable(this.Description, OwnMailCategoryBounds.MaximumDescriptionLength) is { } description)
        {
            yield return new ValidationResult(
                $"{path}:{nameof(this.Description)} {description}.",
                [nameof(this.Description)]);
        }
    }

    private static string? ReasonUnusable(string text, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "is empty";
        }

        if (text.Length > maximumLength)
        {
            return string.Create(CultureInfo.InvariantCulture, $"is longer than the {maximumLength} characters it may hold");
        }

        if (!string.Equals(text, text.Trim(), StringComparison.Ordinal))
        {
            return "starts or ends with white space";
        }

        return text.Any(char.IsControl) ? "contains a control character" : null;
    }
}
