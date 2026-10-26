// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.DecisionModels;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Chat;
using MailFathom.Host.Configuration.Decisions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Decisions;

/// <summary>Covers what a start decides about the <c>Decisions</c> section: which model each function asks, and every declaration it refuses.</summary>
/// <remarks>
/// Driven through <see cref="ComposedSettings.FindProviderRefusals" />, because that is where a start and a
/// configuration write both judge the section, ahead of the mapping composition makes from it.
/// </remarks>
public sealed class DecisionModelOptionsTests
{
    private const string DatedModel = "vendor/decider-2026-10-01";

    [Fact]
    public void FindProviderRefusals_NoDecisionsSection_RefusesNothingAndPlansNothing()
    {
        // Arrange
        var configuration = Configuration([]);

        // Act
        var refusals = ComposedSettings.FindProviderRefusals(configuration);

        // Assert
        Assert.Empty(refusals);
        Assert.Null(Declared(configuration));
    }

    /// <summary>One model and nothing written beside it is the ordinary deployment, so it needs no default stated.</summary>
    [Fact]
    public void ToPlans_ASingleModelAndNoReferences_RoutesEveryFunctionToThatModel()
    {
        // Arrange
        var configuration = Configuration(ModelBlock(0, "decider"));

        // Act
        var refusals = ComposedSettings.FindProviderRefusals(configuration);
        var plans = Declared(configuration)!.ToPlans();

        // Assert
        Assert.Empty(refusals);
        var plan = plans[DecisionFunction.SpamClassification];
        Assert.Equal("decider", plan.Alias);
        Assert.Equal(DatedModel, plan.Model);
        Assert.Null(plan.Address);
        Assert.Equal(TimeSpan.FromSeconds(30), plan.RequestTimeout);
    }

    /// <summary>A function names its own model, the default answers for the rest, and nothing falls further than that.</summary>
    [Fact]
    public void ToPlans_AFunctionNamingItsOwnModel_IsRoutedThereRatherThanToTheDefault()
    {
        // Arrange
        var keys = Merged(ModelBlock(0, "general"), ModelBlock(1, "spam-tuned", address: "https://eu.openrouter.ai"));
        keys["Decisions:DefaultModel:Alias"] = "general";
        keys["Decisions:SpamClassification:Model:Alias"] = "spam-tuned";
        var configuration = Configuration(keys);

        // Act
        var refusals = ComposedSettings.FindProviderRefusals(configuration);
        var plan = Declared(configuration)!.ToPlans()[DecisionFunction.SpamClassification];

        // Assert
        Assert.Empty(refusals);
        Assert.Equal("spam-tuned", plan.Alias);
        Assert.Equal(new Uri("https://eu.openrouter.ai"), plan.Address);
    }

    [Fact]
    public void FindProviderRefusals_TwoModelsAndNoDefault_IsRefusedNamingTheDefault()
    {
        // Arrange
        var configuration = Configuration(Merged(ModelBlock(0, "first"), ModelBlock(1, "second")));

        // Act
        var refusal = Assert.Single(ComposedSettings.FindProviderRefusals(configuration));

        // Assert
        Assert.Equal(DecisionModelOptions.SectionName, refusal.SectionName);
        Assert.Contains(refusal.Errors, error => error.StartsWith("Decisions:DefaultModel — ", StringComparison.Ordinal));
    }

    [Fact]
    public void FindProviderRefusals_AReferenceToAnUndeclaredAlias_IsRefusedNamingTheKey()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>(ModelBlock(0, "decider"))
        {
            ["Decisions:SpamClassification:Model:Alias"] = "missing",
        });

        // Act
        var refusal = Assert.Single(ComposedSettings.FindProviderRefusals(configuration));

        // Assert
        Assert.Contains(refusal.Errors, error => error.StartsWith("Decisions:SpamClassification:Model:Alias — Decisions:SpamClassification:Model names the decision model 'missing'", StringComparison.Ordinal));
    }

    /// <summary>A chat model answers a different contract, so naming one is told by name rather than as a typo.</summary>
    [Fact]
    public void FindProviderRefusals_AReferenceNamingAChatModel_IsRefusedAsAChatModel()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>(ModelBlock(0, "decider"))
        {
            ["Chat:Models:0:Alias"] = "answering",
            ["Chat:Models:0:Model"] = "a-chat-model",
            ["Chat:Models:0:Unauthenticated"] = "true",
            ["Decisions:DefaultModel:Alias"] = "answering",
        });

        // Act
        var refusal = Assert.Single(ComposedSettings.FindProviderRefusals(configuration));

        // Assert
        Assert.Equal(DecisionModelOptions.SectionName, refusal.SectionName);
        Assert.Contains(refusal.Errors, error => error.StartsWith("Decisions:DefaultModel — names 'answering', which is a chat model.", StringComparison.Ordinal));
    }

    /// <summary>Refused from the chat side, because that is the section a reload changes, and once rather than from both sides.</summary>
    [Fact]
    public void FindProviderRefusals_AnAliasAChatModelAlsoDeclares_IsRefusedAsTheChatModels()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>(ModelBlock(0, "shared"))
        {
            ["Chat:Models:0:Alias"] = "shared",
            ["Chat:Models:0:Model"] = "a-chat-model",
            ["Chat:Models:0:Unauthenticated"] = "true",
        });

        // Act
        var refusal = Assert.Single(ComposedSettings.FindProviderRefusals(configuration));

        // Assert
        Assert.Equal(ChatModelOptions.SectionName, refusal.SectionName);
        Assert.Contains(refusal.Errors, error => error.Contains("A chat model and a decision model both declare the alias 'shared'", StringComparison.Ordinal));
    }

    /// <summary>The credential source searches the embedding chain first, so a shared alias would present that endpoint's credential at the decision model's address.</summary>
    [Fact]
    public void FindProviderRefusals_AnAliasAnEmbeddingEndpointAlsoDeclares_IsRefused()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>(ModelBlock(0, "shared"))
        {
            ["Embeddings:Endpoints:0:Alias"] = "shared",
            ["Embeddings:Endpoints:0:Model"] = "an-embedding-model",
            ["Embeddings:Endpoints:0:Dimension"] = "4",
            ["Embeddings:Endpoints:0:Unauthenticated"] = "true",
        });

        // Act
        var refusal = Assert.Single(ComposedSettings.FindProviderRefusals(configuration));

        // Assert
        Assert.Equal(DecisionModelOptions.SectionName, refusal.SectionName);
        Assert.Contains(refusal.Errors, error => error.Contains("the alias 'shared' is declared by an embedding endpoint as well", StringComparison.Ordinal));
    }

    /// <summary>A router alias moves to a new build, and the threshold calibrated against the old one moves with it unannounced.</summary>
    [Fact]
    public void FindProviderRefusals_AModelDeclaredByAMovingAlias_IsRefused()
    {
        // Arrange
        var configuration = Configuration(ModelBlock(0, "decider", model: "~vendor/decider-latest"));

        // Act
        var refusal = Assert.Single(ComposedSettings.FindProviderRefusals(configuration));

        // Assert
        Assert.Contains(refusal.Errors, error => error.StartsWith("Decisions:Models:0:Model — ", StringComparison.Ordinal) && error.Contains("moving aliases", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null, null, "neither a provider key")]
    [InlineData("plaintext:a-key", "true", "more than one of")]
    public void FindProviderRefusals_ADeclarationWithoutExactlyOneCredentialShape_IsRefused(
        string? keyReference,
        string? unauthenticated,
        string expected)
    {
        // Arrange
        var keys = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Decisions:Models:0:Alias"] = "decider",
            ["Decisions:Models:0:Model"] = DatedModel,
        };

        if (keyReference is not null)
        {
            keys["Decisions:Models:0:ApiKey:Name"] = "decision-key";
            keys["Decisions:Models:0:ApiKey:SecretReference"] = keyReference;
        }

        if (unauthenticated is not null)
        {
            keys["Decisions:Models:0:Unauthenticated"] = unauthenticated;
        }

        // Act
        var refusal = Assert.Single(ComposedSettings.FindProviderRefusals(Configuration(keys)));

        // Assert
        Assert.Contains(refusal.Errors, error => error.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void FindProviderRefusals_AKeyOverAPlainHttpAddress_IsRefused()
    {
        // Arrange
        var configuration = Configuration(ModelBlock(0, "decider", address: "http://openrouter.invalid"));

        // Act
        var refusal = Assert.Single(ComposedSettings.FindProviderRefusals(configuration));

        // Assert
        Assert.Contains(refusal.Errors, error => error.StartsWith("Decisions:Models:0:Address — ", StringComparison.Ordinal));
    }

    /// <summary>
    /// The keys a chat block takes and this one does not — a fallback, sampling parameters, headers that link requests,
    /// and request members that could loosen the routing block — stop a start rather than being ignored.
    /// </summary>
    [Theory]
    [InlineData("Decisions:DefaultModel:Fallback", "other")]
    [InlineData("Decisions:SpamClassification:Model:Fallback", "other")]
    [InlineData("Decisions:Models:0:Temperature", "0.2")]
    [InlineData("Decisions:Models:0:MaxOutputTokens", "10")]
    [InlineData("Decisions:Models:0:StickySessions", "true")]
    [InlineData("Decisions:Models:0:ExtraHeaders:0:Name", "X-Session")]
    [InlineData("Decisions:Models:0:AdditionalProperties:provider:zdr", "false")]
    [InlineData("Decisions:Models:0:EntraCredential:TenantId", "tenant")]
    public void FindProviderRefusals_AKeyTheDecisionBlockDoesNotTake_StopsTheStart(string key, string value)
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>(ModelBlock(0, "decider")) { [key] = value });

        // Act, Assert
        Assert.Throws<InvalidOperationException>(() => ComposedSettings.FindProviderRefusals(configuration));
    }

    [Fact]
    public void FindProviderRefusals_AReferenceWithNoModelDeclared_IsRefused()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Decisions:DefaultModel:Alias"] = "decider",
        });

        // Act
        var refusal = Assert.Single(ComposedSettings.FindProviderRefusals(configuration));

        // Assert
        Assert.Contains(refusal.Errors, error => error.StartsWith("Decisions:Models — ", StringComparison.Ordinal));
    }

    private static Dictionary<string, string?> ModelBlock(
        int position,
        string alias,
        string model = DatedModel,
        string? address = null)
    {
        var block = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [$"Decisions:Models:{position}:Alias"] = alias,
            [$"Decisions:Models:{position}:Model"] = model,
            [$"Decisions:Models:{position}:ApiKey:Name"] = $"decision-key-{position}",
            [$"Decisions:Models:{position}:ApiKey:SecretReference"] = "plaintext:an-openrouter-key",
        };

        if (address is not null)
        {
            block[$"Decisions:Models:{position}:Address"] = address;
        }

        return block;
    }

    private static Dictionary<string, string?> Merged(params Dictionary<string, string?>[] parts) =>
        parts.SelectMany(part => part).ToDictionary(StringComparer.Ordinal);

    private static DecisionModelOptions? Declared(IConfiguration configuration) =>
        configuration.GetSection(DecisionModelOptions.SectionName).Get<DecisionModelOptions>();

    private static IConfiguration Configuration(Dictionary<string, string?> keys) =>
        new ConfigurationBuilder().AddInMemoryCollection(keys).Build();
}
