// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.AI.DecisionModels;
using MailFathom.Host.Configuration.Providers;
using MailFathom.Infrastructure.Secrets.Discovery;

namespace MailFathom.Host.Configuration.Decisions;

/// <summary>One model this deployment may ask a closed question: where it is, which dated build it is, and what one request may carry.</summary>
/// <remarks>
/// <para>
/// The endpoint keys of a <c>Chat:Models</c> block and nothing else, as
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0041-asking-a-model-a-closed-question-through-openrouters-decisions-api.md">ADR 0041</see>
/// states. A decision has no generation parameters, so there is no output budget, temperature, or reasoning effort to
/// declare; and the adapter owns every request member, so there are no extra headers, sticky sessions, or additional
/// properties either — a session or a static header would link requests at the router, and a body member would be how
/// the routing block that keeps mail away from a provider that retains it got loosened from configuration. The section
/// is bound strictly, so any of those keys written here refuses the start rather than being ignored.
/// </para>
/// <para>
/// A Microsoft Entra credential has no meaning for a router that takes a bearer key, so it is not a setting here at
/// all: the reach rules see it as never declared.
/// </para>
/// </remarks>
internal sealed class DecisionModelDeclarationOptions : IProviderEndpointReachDeclaration
{
    /// <summary>The character a router's moving alias begins with, which a decision model may not be declared by.</summary>
    internal const char MovingAliasPrefix = '~';

    /// <summary>Gets or sets the deployment's own name for this model, which every reference to it is written with.</summary>
    /// <remarks>The name a log line, a metric tag, a resilience circuit, and a failure message use, and the key its credential is resolved by, so it may repeat no other declared model's or endpoint's.</remarks>
    public string Alias { get; set; } = string.Empty;

    /// <summary>Gets or sets the dated build or versioned identifier requests are routed to.</summary>
    /// <remarks>
    /// Never one of the router's moving aliases, which begin with <c>~</c>: a threshold is calibrated against one build,
    /// and an alias moves to the next one without anybody being told.
    /// </remarks>
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets the base address requests are sent to, empty for <c>https://openrouter.ai</c>.</summary>
    /// <remarks>A regional host is written here. The adapter appends the route itself, so this is the host and never the API path.</remarks>
    public string Address { get; set; } = string.Empty;

    /// <summary>Gets or sets the time one request may take before it is abandoned.</summary>
    /// <remarks>Shorter than a chat request's by default, because a decision is a handful of probabilities rather than an answer the model writes out.</remarks>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the greatest number of characters one request body may hold.</summary>
    /// <remarks>A bound on what leaves the deployment, measured on the body as it is sent: the evidence, the questions, and their criteria together.</remarks>
    [Range(1, 1_000_000)]
    public int MaxRequestCharacters { get; set; } = 32_000;

    /// <summary>Gets or sets the reference to the OpenRouter key this model is reached with.</summary>
    /// <remarks>Absent for a model reached with no credential, and absent by default rather than an empty block, so secret discovery does not find an unresolvable reference nobody wrote.</remarks>
    public ConfiguredSecret? ApiKey { get; set; }

    /// <summary>Gets or sets whether this model asks for no credential, so a request presents none.</summary>
    /// <remarks>Written rather than inferred from an absent key, because an omission is exactly what a forgotten key reference looks like.</remarks>
    public bool Unauthenticated { get; set; }

    /// <inheritdoc />
    ProviderEntraCredentialOptions? IProviderEndpointReachDeclaration.EntraCredential => null;

    /// <summary>Reports everything an operator must fix before this model could be asked.</summary>
    /// <param name="position">Where in the declared array this block sits, which is how a message names a block whose alias is missing.</param>
    /// <returns>One result per rule the declaration breaks, keyed to the block, empty when it is usable.</returns>
    public IEnumerable<ValidationResult> FindConfigurationErrors(int position) =>
        this.FindDeclaredErrors(position).Select(error => new ValidationResult(
            error.ErrorMessage,
            [.. error.MemberNames.Select(member => $"{nameof(DecisionModelOptions.Models)}:{position}:{member}")]));

    /// <summary>Builds the plan a request to this model runs on.</summary>
    /// <returns>The plan.</returns>
    /// <exception cref="UriFormatException">Thrown when the declared address is not a URI.</exception>
    /// <remarks>Called only after validation has passed, so what is left here is mapping rather than checking.</remarks>
    public DecisionModelPlan ToPlan() => new(
        this.Alias.Trim(),
        this.Address is { Length: > 0 } address ? new Uri(address, UriKind.Absolute) : null,
        this.Model.Trim(),
        this.RequestTimeout,
        this.MaxRequestCharacters);

    private IEnumerable<ValidationResult> FindDeclaredErrors(int position)
    {
        var bound = new List<ValidationResult>();

        Validator.TryValidateObject(this, new ValidationContext(this), bound, validateAllProperties: true);

        foreach (var error in bound)
        {
            yield return error;
        }

        var alias = this.Alias.Trim();

        if (alias.Length == 0)
        {
            yield return new ValidationResult(
                $"The decision model declared at {DecisionModelOptions.SectionName}:{nameof(DecisionModelOptions.Models)}:{position} has no Alias, so nothing could name it. The alias is how every reference, every log line, and every credential reaches this model.",
                [nameof(this.Alias)]);

            yield break;
        }

        var description = $"Decision model '{alias}'";
        var model = this.Model.Trim();

        if (model.Length == 0)
        {
            yield return new ValidationResult(
                $"{description} declares no Model, so no request could name what to route it to.",
                [nameof(this.Model)]);
        }
        else if (model[0] == MovingAliasPrefix)
        {
            yield return new ValidationResult(
                $"{description} declares the Model '{model}', which is one of the router's moving aliases. A threshold is calibrated against one build, so declare the dated build or versioned identifier instead.",
                [nameof(this.Model)]);
        }

        if (this.RequestTimeout <= TimeSpan.Zero)
        {
            yield return new ValidationResult(
                $"{description} declares a RequestTimeout that is not positive, because an unbounded request would hold the work behind it open for as long as the endpoint stays silent.",
                [nameof(this.RequestTimeout)]);
        }

        foreach (var error in ProviderEndpointReachRules.FindConfigurationErrors(description, this))
        {
            yield return error;
        }
    }
}
