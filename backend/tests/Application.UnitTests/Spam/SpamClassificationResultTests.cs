// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Spam;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Spam;
using Xunit;

namespace MailFathom.Application.UnitTests.Spam;

public sealed class SpamClassificationResultTests
{
    private static readonly StoredEmailId Occurrence =
        StoredEmailId.Create(Guid.Parse("0199a0c0-0000-7000-8000-0000000000a1"));

    private static readonly DateTimeOffset EvaluatedAt = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Both outcomes carry a classification, so a result claiming either without one is refused.</summary>
    [Theory]
    [InlineData(SpamClassificationOutcome.Classified)]
    [InlineData(SpamClassificationOutcome.PersonsVerdictKept)]
    public void NotClassified_AnOutcomeThatCarriesAClassification_IsRefused(SpamClassificationOutcome outcome)
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => SpamClassificationResult.NotClassified(outcome));
    }

    [Fact]
    public void NotClassified_AReasonNothingWasRecorded_CarriesNoClassification()
    {
        // Arrange, Act
        var result = SpamClassificationResult.NotClassified(SpamClassificationOutcome.AlreadyClassified);

        // Assert
        Assert.Equal(SpamClassificationOutcome.AlreadyClassified, result.Outcome);
        Assert.Null(result.Classification);
    }

    [Fact]
    public void PersonsVerdictKept_AVerdictAStageReached_IsRefused()
    {
        // Arrange
        var stageVerdict = Classification(SpamClassificationStage.Deterministic);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => SpamClassificationResult.PersonsVerdictKept(stageVerdict));
    }

    [Fact]
    public void PersonsVerdictKept_ThePersonsVerdict_CarriesItWithTheOutcome()
    {
        // Arrange
        var personsVerdict = Classification(SpamClassificationStage.Person);

        // Act
        var result = SpamClassificationResult.PersonsVerdictKept(personsVerdict);

        // Assert
        Assert.Equal(SpamClassificationOutcome.PersonsVerdictKept, result.Outcome);
        Assert.Same(personsVerdict, result.Classification);
    }

    private static SpamClassification Classification(SpamClassificationStage decidedBy) => SpamClassification.Create(
        Occurrence,
        SpamVerdict.NotSpam,
        decidedBy,
        assessment: null,
        SpamClassificationProfile.Create(),
        [],
        EvaluatedAt);
}
