// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MailFathom.AI.DecisionModels;
using MailFathom.Host.Configuration.Chat;
using MailFathom.Host.Configuration.Embeddings;

namespace MailFathom.Host.Configuration.Decisions;

/// <summary>Declares which models this deployment asks closed questions, and which of them each decision function asks.</summary>
/// <remarks>
/// <para>
/// A configuration root of its own beside <c>Chat</c>, because a decision model is reached through a contract a chat
/// model is not, and is chosen for a reason a chat model is not: its probabilities are what a threshold is calibrated
/// against. Its shape follows <c>Chat</c> otherwise — a declared array under aliases, a default, and a reference per
/// function — so an operator who has written one has written the other.
/// </para>
/// <para>
/// An absent section is a valid deployment: nothing calls the Decisions API, and no function that would ask one runs.
/// The section is read once, while the host composes itself, so an edit to it takes a restart.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class DecisionModelOptions : IValidatableObject
{
    /// <summary>The configuration section this declaration is bound from.</summary>
    public const string SectionName = "Decisions";

    /// <summary>Gets the models this deployment may ask, each under an alias of its own.</summary>
    public IList<DecisionModelDeclarationOptions> Models { get; } = [];

    /// <summary>Gets or sets which declared model every function asks unless it names one of its own.</summary>
    /// <remarks>May be left unwritten where exactly one model is declared, and must be written where more than one is.</remarks>
    public DecisionModelReferenceOptions DefaultModel { get; set; } = new();

    /// <summary>Gets or sets which declared model a message's spam verdict is asked of.</summary>
    public DecisionFunctionOptions SpamClassification { get; set; } = new();

    /// <summary>Gets every alias the section declares a model under, as written.</summary>
    public IReadOnlyCollection<string> DeclaredAliases => [.. this.Models
        .Select(model => model.Alias.Trim())
        .Where(alias => alias.Length > 0)];

    /// <summary>Gets the reference naming which model a function asks.</summary>
    /// <param name="function">The function whose model is wanted.</param>
    /// <returns>That function's own reference, which names no model where the deployment wrote none.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the function is not one this section declares a reference for.</exception>
    public DecisionModelReferenceOptions ReferenceFor(DecisionFunction function) => function switch
    {
        DecisionFunction.SpamClassification => this.SpamClassification.Model,
        _ => throw new ArgumentOutOfRangeException(nameof(function), function, "The section declares no model reference for this function."),
    };

    /// <summary>Names the configuration key a function's reference is written under.</summary>
    /// <param name="function">The function whose key is wanted.</param>
    /// <returns>The key an operator edits.</returns>
    public static string DescribeReference(DecisionFunction function) =>
        $"{SectionName}:{function}:{nameof(DecisionFunctionOptions.Model)}";

    /// <summary>Resolves which declared model each function asks.</summary>
    /// <returns>One plan per function, empty when the deployment declared no decision model.</returns>
    /// <remarks>Called only after validation has passed: a function whose reference resolves to nothing is one validation already refused.</remarks>
    public IReadOnlyDictionary<DecisionFunction, DecisionModelPlan> ToPlans() => Enum.GetValues<DecisionFunction>()
        .Select(function => (Function: function, Model: this.FindModelFor(this.ReferenceFor(function))))
        .Where(resolved => resolved.Model is not null)
        .ToDictionary(resolved => resolved.Function, resolved => resolved.Model!.ToPlan());

    /// <summary>Reports everything an operator must fix before this declaration can be used.</summary>
    /// <param name="candidate">The bound decision declaration, or <see langword="null" /> when the deployment wrote no section.</param>
    /// <param name="chat">The bound chat declaration, or <see langword="null" /> when the deployment wrote none.</param>
    /// <param name="embeddings">The bound embedding declaration, or <see langword="null" /> when the deployment wrote none.</param>
    /// <returns>One message per rule the declaration breaks, each prefixed with the key it is about, empty when it is usable.</returns>
    /// <remarks>
    /// <para>
    /// The section's own rules, and the two it cannot judge alone because it cannot see the other sections: an alias
    /// names one endpoint across the deployment, because a credential, a resilience circuit, and a log line are keyed by
    /// it; and a reference naming a chat model is told so by name, because a chat model answers a different contract and
    /// the plain "undeclared" refusal would send an operator looking for a typo that is not there.
    /// </para>
    /// <para>
    /// An absent section is a supported deployment and reports nothing.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> FindDeclarationErrors(
        DecisionModelOptions? candidate,
        ChatModelOptions? chat,
        EmbeddingOptions? embeddings)
    {
        if (candidate is null)
        {
            return [];
        }

        var declared = candidate.DeclaredAliases;
        var chatAliases = chat?.DeclaredAliases ?? [];
        IEnumerable<(string Key, DecisionModelReferenceOptions Reference)> references =
        [
            ($"{SectionName}:{nameof(DefaultModel)}", candidate.DefaultModel),
            .. Enum.GetValues<DecisionFunction>().Select(function => (DescribeReference(function), candidate.ReferenceFor(function))),
        ];

        var namingChatModels = references
            .Select(reference => (reference.Key, Alias: reference.Reference.Alias.Trim()))
            .Where(reference => reference.Alias.Length > 0
                && !declared.Contains(reference.Alias, StringComparer.OrdinalIgnoreCase)
                && chatAliases.Contains(reference.Alias, StringComparer.OrdinalIgnoreCase))
            .Select(reference => $"{reference.Key} — names '{reference.Alias}', which is a chat model. A decision is asked of a model declared under {SectionName}:{nameof(Models)}, because a chat model answers a different contract and its text is not a calibrated probability.");

        var sectionResults = new List<ValidationResult>();

        Validator.TryValidateObject(candidate, new ValidationContext(candidate), sectionResults, validateAllProperties: true);

        var sectionErrors = sectionResults
            .Where(result => !string.IsNullOrWhiteSpace(result.ErrorMessage))
            .Select(result => result.MemberNames.FirstOrDefault() is { Length: > 0 } member
                ? $"{SectionName}:{member} — {result.ErrorMessage}"
                : $"{SectionName} — {result.ErrorMessage}");

        // A chat model reusing a decision alias is refused by the chat rules, which a reload runs as well; what is left
        // here is the embedding chain, which neither section reloads.
        var embeddingAliases = (embeddings?.Endpoints.Select(endpoint => endpoint.Alias.Trim()) ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reused = declared
            .Where(embeddingAliases.Contains)
            .Select(alias => $"{SectionName}:{nameof(Models)} — the alias '{alias}' is declared by an embedding endpoint as well. An alias names one endpoint, because it is what a credential, a resilience circuit, and a log line are keyed by.");

        return [.. namingChatModels, .. sectionErrors, .. reused];
    }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (this.Models.Count == 0)
        {
            if (this.DefaultModel.NamesModel
                || Enum.GetValues<DecisionFunction>().Any(function => this.ReferenceFor(function).NamesModel))
            {
                yield return new ValidationResult(
                    $"The {SectionName} section names a model but declares none under {SectionName}:{nameof(this.Models)}, so nothing could be asked. Declare a model, or remove the section.",
                    [nameof(this.Models)]);
            }

            yield break;
        }

        foreach (var error in this.Models.SelectMany((model, position) => model.FindConfigurationErrors(position)))
        {
            yield return error;
        }

        foreach (var error in this.FindRepeatedAliases())
        {
            yield return error;
        }

        foreach (var error in this.FindReferenceErrors())
        {
            yield return error;
        }
    }

    /// <summary>Finds the model an alias names.</summary>
    /// <param name="alias">The alias a reference or a credential lookup carries.</param>
    /// <returns>The declaration, or <see langword="null" /> when nothing declares that alias.</returns>
    /// <remarks>Matched trimmed and without case, as an alias is matched everywhere else it is used.</remarks>
    public DecisionModelDeclarationOptions? FindModel(string alias)
    {
        var wanted = alias.Trim();

        return wanted.Length == 0
            ? null
            : this.Models.FirstOrDefault(
                model => string.Equals(model.Alias.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
    }

    private DecisionModelDeclarationOptions? FindDefaultModel()
    {
        if (this.DefaultModel.NamesModel)
        {
            return this.FindModel(this.DefaultModel.Alias);
        }

        return this.Models.Count == 1 && this.Models[0].Alias.Trim().Length > 0
            ? this.Models[0]
            : null;
    }

    private DecisionModelDeclarationOptions? FindModelFor(DecisionModelReferenceOptions reference) =>
        reference.NamesModel ? this.FindModel(reference.Alias) : this.FindDefaultModel();

    private IEnumerable<ValidationResult> FindRepeatedAliases() => this.DeclaredAliases
        .GroupBy(alias => alias, StringComparer.OrdinalIgnoreCase)
        .Where(group => group.Count() > 1)
        .Select(group => new ValidationResult(
            $"The alias '{group.Key}' is declared by more than one decision model. An alias names one endpoint, because it is what a credential, a resilience circuit, and a log line are keyed by.",
            [nameof(this.Models)]));

    private IEnumerable<ValidationResult> FindReferenceErrors()
    {
        var declared = this.DeclaredAliases;

        foreach (var error in this.DefaultModel.FindConfigurationErrors(nameof(this.DefaultModel), declared))
        {
            yield return error;
        }

        foreach (var error in Enum.GetValues<DecisionFunction>().SelectMany(function =>
            this.ReferenceFor(function).FindConfigurationErrors($"{function}:{nameof(DecisionFunctionOptions.Model)}", declared)))
        {
            yield return error;
        }

        // A function that names its own model needs no default, so the default is required only where some function
        // would fall through to it — which, with one function today, is exactly when that function names none.
        var fallsThrough = Enum.GetValues<DecisionFunction>().Any(function => !this.ReferenceFor(function).NamesModel);

        if (fallsThrough && !this.DefaultModel.NamesModel && this.FindDefaultModel() is null)
        {
            yield return new ValidationResult(
                $"{SectionName}:{nameof(this.DefaultModel)} names no model, and {this.Models.Count} are declared. State which one a function asks when it names none; it may be left unwritten only where exactly one model is declared.",
                [nameof(this.DefaultModel)]);
        }
    }
}
