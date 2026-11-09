// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MailFathom.Domain.Categories;

namespace MailFathom.Host.Configuration.Categories;

/// <summary>Whether one account's mail is sorted into categories, and which of them it recognizes.</summary>
/// <remarks>
/// <para>
/// The account's rather than each assigned user's for the reason spam classification is: the mail is one copy, so a
/// mailbox pays for one consultation and everybody it is assigned to sees the same categories.
/// </para>
/// <para>
/// It names no folders. Categories cover exactly the account's folders that are visible to AI, so a folder mapped
/// <c>VisibleToAI: false</c> is outside them with nothing more to configure here.
/// </para>
/// <para>
/// A refusal names an entry by its position and never repeats what it holds: a subject somebody named can be personal
/// data, and a refusal is written to a log.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The configuration binder materializes this type when a user's document is read.")]
internal sealed class MailAccountCategorizationOptions
{
    /// <summary>The property an account's record holds this block under.</summary>
    internal const string RecordProperty = "Categorization";

    /// <summary>The path an account's record holds the switch under, as a refusal names it.</summary>
    internal const string EnabledPath = $"{RecordProperty}:{nameof(Enabled)}";

    /// <summary>Every published default identity, as one phrase a refusal ends with.</summary>
    private static readonly string DefaultIdentities = string.Join(", ", DefaultMailCategory.All);

    /// <summary>Gets or sets whether this account's mail is sorted into categories at all.</summary>
    /// <remarks>Off when the block is absent, so nothing new leaves the deployment until an account asks for it.</remarks>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the identities of the default categories this account does not show.</summary>
    /// <remarks>Hidden rather than unasked: the defaults are one choice evaluated once, so switching one back on costs nothing.</remarks>
    public List<string> DisabledDefaults { get; set; } = [];

    /// <summary>Gets or sets the subjects this account names for itself.</summary>
    public List<MailAccountOwnCategoryOptions> OwnCategories { get; set; } = [];

    /// <summary>Finds everything about this block that would otherwise be discovered on the account's own mail.</summary>
    /// <returns>One result per refusal, each naming the setting that carries it.</returns>
    internal IEnumerable<ValidationResult> FindRefusals() =>
        this.FindDefaultRefusals().Concat(this.FindOwnCategoryCountRefusal()).Concat(this.FindOwnCategoryRefusals());

    private static string Position(int index) => index.ToString(CultureInfo.InvariantCulture);

    private IEnumerable<ValidationResult> FindDefaultRefusals()
    {
        var unknown = this.DisabledDefaults.Index()
            .Where(static entry => !DefaultMailCategory.TryParse(entry.Item, out _))
            .Select(entry => new ValidationResult(
                $"The account record's {RecordProperty}:{nameof(this.DisabledDefaults)}:{Position(entry.Index)} names no default category. It takes {DefaultIdentities}.",
                [nameof(this.DisabledDefaults)]));

        var repeated = this.DisabledDefaults.Index()
            .GroupBy(static entry => entry.Item, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(group => new ValidationResult(
                $"The account record's {RecordProperty}:{nameof(this.DisabledDefaults)}:{Position(group.Last().Index)} repeats an entry above it. Name each default once.",
                [nameof(this.DisabledDefaults)]));

        return unknown.Concat(repeated);
    }

    private IEnumerable<ValidationResult> FindOwnCategoryCountRefusal()
    {
        if (this.OwnCategories.Count > OwnMailCategoryBounds.MaximumPerAccount)
        {
            yield return new ValidationResult(
                $"The account record declares {Position(this.OwnCategories.Count)} categories under {RecordProperty}:{nameof(this.OwnCategories)}, past the {Position(OwnMailCategoryBounds.MaximumPerAccount)} one account holds.",
                [nameof(this.OwnCategories)]);
        }
    }

    private IEnumerable<ValidationResult> FindOwnCategoryRefusals()
    {
        var entryRefusals = this.OwnCategories.Index().SelectMany(entry => entry.Item.FindRefusals(
            $"{RecordProperty}:{nameof(this.OwnCategories)}:{Position(entry.Index)}"));

        var repeated = this.OwnCategories.Index()
            .GroupBy(static entry => entry.Item.Name, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(group => new ValidationResult(
                $"The account record's {RecordProperty}:{nameof(this.OwnCategories)}:{Position(group.Last().Index)} repeats the name of a category above it, whatever the case. Name each category once.",
                [nameof(this.OwnCategories)]));

        var takingADefault = this.OwnCategories.Index()
            .Where(static entry => DefaultMailCategory.All.Any(
                category => string.Equals(category.ToString(), entry.Item.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(entry => new ValidationResult(
                $"The account record's {RecordProperty}:{nameof(this.OwnCategories)}:{Position(entry.Index)} takes the name of a default category, and a name offered to a search or an agent has to mean one thing. Name it something else.",
                [nameof(this.OwnCategories)]));

        return entryRefusals.Concat(repeated).Concat(takingADefault);
    }
}
