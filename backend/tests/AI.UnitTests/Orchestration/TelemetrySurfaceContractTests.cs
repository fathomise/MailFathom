// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.DecisionModels;
using MailFathom.AI.Decisions;
using MailFathom.AI.Orchestration;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.AI.UnitTests.Orchestration;

/// <summary>Asserts the redaction contract over what this boundary publishes, which is where a decision about somebody's mail is measured.</summary>
/// <remarks>
/// <para>
/// The drive hands the publisher the mail itself, the question asked of it, and the verdict, each as a sentinel, under a
/// model declared by a sentinel alias, and ends a call every way one can end. The contract then asserts that the alias
/// surfaced only on the dimension named for it and that nothing read out of the mail surfaced anywhere.
/// </para>
/// <para>
/// The telemetry is opened over a source and a meter of this test's own rather than MailFathom's process-wide pair,
/// because this boundary takes both as constructor arguments; what is asserted is what the publisher writes to them.
/// </para>
/// </remarks>
public sealed class TelemetrySurfaceContractTests
{
    private static readonly DecisionModelPlan Plan =
        new(TelemetryRedactionContract.ConfiguredAliasSentinel, Address: null, "vendor/decider-2026-10-01", TimeSpan.FromSeconds(30), 32_000);

    private static readonly ChatMessage[] Input = [new(ChatRole.User, TelemetryRedactionContract.MailDerivedSentinel)];

    private static readonly DecisionQuestion[] Questions =
    [
        DecisionQuestion.Choice(
            "kind",
            TelemetryRedactionContract.MailDerivedSentinel,
            [new(TelemetryRedactionContract.MailDerivedSentinel, TelemetryRedactionContract.MailDerivedSentinel), new("ordinary", string.Empty)]),
    ];

    /// <summary>The drive really emits the surface, and the configured alias really reaches the publisher.</summary>
    /// <remarks>The control for every absence below: an alias read back where it belongs says the drive published something.</remarks>
    [Fact]
    public async Task EmittedSurface_EveryEndingDriven_NamesTheModelByItsAliasOnTheEndpointDimension()
    {
        // Arrange
        using var published = new PublishedDecisionTelemetry();

        // Act
        await DriveEveryEndingAsync(published);

        // Assert
        Assert.Contains("mailfathom.decisions.duration", published.InstrumentNames);
        Assert.Contains("mailfathom.decisions.tokens", published.InstrumentNames);
        Assert.Contains(
            published.EmittedTags,
            tag => tag.Key == DecisionTelemetry.EndpointTagName && Equals(tag.Value, TelemetryRedactionContract.ConfiguredAliasSentinel));
    }

    [Fact]
    public async Task EmittedSurface_EveryEndingDriven_IsNamedAfterNothingInAMailbox()
    {
        // Arrange
        using var published = new PublishedDecisionTelemetry();

        // Act
        await DriveEveryEndingAsync(published);

        // Assert
        TelemetryRedactionContract.AssertNothingIsNamedAfterMailOrASecret(published.EmittedNames);
        TelemetryRedactionContract.AssertEverySpanIsNamedAfterItsOperation([.. published.Spans.Select(span => span.OperationName)]);
    }

    [Fact]
    public async Task EmittedSurface_EveryEndingDriven_IsNamespacedUnderMailFathom()
    {
        // Arrange
        using var published = new PublishedDecisionTelemetry();

        // Act
        await DriveEveryEndingAsync(published);

        // Assert
        TelemetryRedactionContract.AssertEveryDimensionIsNamespacedUnderMailFathom(published.InstrumentNames, published.EmittedTags);
    }

    /// <summary>The mail, the question, and the verdict reached no name, no key, and no dimension value.</summary>
    [Fact]
    public async Task EmittedSurface_EveryCallMadeOverMail_LetsNoneOfItReachAnExporter()
    {
        // Arrange
        using var published = new PublishedDecisionTelemetry();

        // Act
        await DriveEveryEndingAsync(published);

        // Assert
        TelemetryRedactionContract.AssertNoPoisonedInputEscaped(published.EmittedNames, published.EmittedTags);
    }

    /// <summary>A publisher nobody added to the drive fails here rather than going unasserted.</summary>
    [Fact]
    public void EveryPublisherInTheAssembly_WhateverItIsCalled_IsDrivenByThisSuite() =>
        TelemetryRedactionContract.AssertEveryPublisherInTheAssemblyIsDriven(
            typeof(DecisionTelemetry).Assembly,
            [typeof(DecisionTelemetry)]);

    /// <summary>Ends a call every way one ends: answered, failed and classified, unreadable but billed, refused before it was sent, and cancelled.</summary>
    private static async Task DriveEveryEndingAsync(PublishedDecisionTelemetry published)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var unreadable = new DecisionFailedException(Plan.Alias, DecisionFailure.AnswerUnreadable)
        {
            Usage = new UsageDetails { InputTokenCount = 9, OutputTokenCount = 0 },
        };

        using (var answered = ScriptedDecisionClient.AnsweringFirstOption(inputTokens: 412, outputTokens: 3))
        {
            await DecideAsync(published, answered, cancellationToken);
        }

        foreach (var failure in new Exception[]
        {
            new DecisionFailedException(Plan.Alias, DecisionFailure.RateLimited),
            unreadable,
            new ArgumentException(TelemetryRedactionContract.MailDerivedSentinel),
        })
        {
            using var failing = ScriptedDecisionClient.Throwing(failure);
            await Assert.ThrowsAnyAsync<Exception>(() => DecideAsync(published, failing, cancellationToken));
        }

        using var stalling = ScriptedDecisionClient.Stalling();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await caller.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DecideAsync(published, stalling, caller.Token));
    }

    private static async Task DecideAsync(PublishedDecisionTelemetry published, IDecisionClient inner, CancellationToken cancellationToken)
    {
        using var client = new ObservedDecisionClient(inner, published.Telemetry, Plan, new FakeTimeProvider());

        await client.DecideAsync(Input, Questions, cancellationToken: cancellationToken);
    }
}
