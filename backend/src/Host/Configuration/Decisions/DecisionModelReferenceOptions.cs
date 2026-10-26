// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel.DataAnnotations;

namespace MailFathom.Host.Configuration.Decisions;

/// <summary>Names which declared decision model something runs on.</summary>
/// <remarks>
/// An alias and nothing else — no <c>Fallback</c> beside it as a chat reference has. A threshold is calibrated against
/// one model build, so a second model asked in its place would answer with probabilities nobody calibrated; the section
/// is bound strictly, so a <c>Fallback</c> written here refuses the start rather than being read.
/// </remarks>
internal sealed class DecisionModelReferenceOptions
{
    /// <summary>Gets or sets the alias of the model this runs on, empty to take whatever the deployment declared as its default.</summary>
    public string Alias { get; set; } = string.Empty;

    /// <summary>Gets whether this reference names a model at all.</summary>
    public bool NamesModel => this.Alias.Trim().Length > 0;

    /// <summary>Reports a reference naming a model no block declares.</summary>
    /// <param name="referencePath">Where the reference sits under the section, such as <c>DefaultModel</c>, which the message and the result's key name.</param>
    /// <param name="declaredAliases">Every alias the section declares a model under.</param>
    /// <returns>One result when the reference names an undeclared model, empty otherwise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="declaredAliases" /> is <see langword="null" />.</exception>
    public IEnumerable<ValidationResult> FindConfigurationErrors(
        string referencePath,
        IReadOnlyCollection<string> declaredAliases)
    {
        ArgumentNullException.ThrowIfNull(declaredAliases);

        var alias = this.Alias.Trim();

        if (alias.Length > 0 && !declaredAliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
        {
            yield return new ValidationResult(
                $"{DecisionModelOptions.SectionName}:{referencePath} names the decision model '{alias}', which no block under {DecisionModelOptions.SectionName}:{nameof(DecisionModelOptions.Models)} declares.",
                [$"{referencePath}:{nameof(this.Alias)}"]);
        }
    }
}
