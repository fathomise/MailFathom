// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Spam;
using Xunit;

namespace MailFathom.Domain.UnitTests.Spam;

public sealed class SpamSignalProvenanceTests
{
    [Fact]
    public void FromMessageHeader_AFieldName_RecordsItAgainstTheHeaderSource()
    {
        // Arrange, Act
        var provenance = SpamSignalProvenance.FromMessageHeader("  Authentication-Results  ");

        // Assert
        Assert.Equal(SpamSignalSource.MessageHeader, provenance.Source);
        Assert.Equal("Authentication-Results", provenance.Origin);
    }

    [Fact]
    public void FromFolderPlacement_AnAlias_RecordsItAgainstTheFolderSource()
    {
        // Arrange, Act
        var provenance = SpamSignalProvenance.FromFolderPlacement("JUNK");

        // Assert
        Assert.Equal(SpamSignalSource.FolderPlacement, provenance.Source);
        Assert.Equal("JUNK", provenance.Origin);
    }

    [Fact]
    public void FromMailboxKeyword_AKeyword_RecordsItAgainstTheKeywordSource()
    {
        // Arrange, Act
        var provenance = SpamSignalProvenance.FromMailboxKeyword("$NotJunk");

        // Assert
        Assert.Equal(SpamSignalSource.MailboxKeyword, provenance.Source);
        Assert.Equal("$NotJunk", provenance.Origin);
    }

    [Fact]
    public void FromModel_ABuildAndARevision_NamesBothAgainstTheModelSource()
    {
        // Arrange, Act
        var provenance = SpamSignalProvenance.FromModel(" example/decider-2026-09-01 ", "3f9a1c");

        // Assert
        Assert.Equal(SpamSignalSource.Model, provenance.Source);
        Assert.Equal("example/decider-2026-09-01@3f9a1c", provenance.Origin);
        Assert.Equal(provenance, SpamSignalProvenance.Restore(provenance.Source, provenance.Origin));
    }

    /// <summary>The separator belongs to neither half, so a recorded origin always reads back as one build and one revision.</summary>
    [Theory]
    [InlineData("example/decider@2026", "3f9a1c", "model")]
    [InlineData("example/decider", "rev@3", "instructionRevision")]
    [InlineData(" ", "3f9a1c", "model")]
    [InlineData("example/decider", "", "instructionRevision")]
    public void FromModel_APartThatCannotBeReadBack_IsRefused(string model, string instructionRevision, string parameterName)
    {
        // Arrange, Act, Assert
        var failure = Assert.Throws<ArgumentException>(() => SpamSignalProvenance.FromModel(model, instructionRevision));

        Assert.Equal(parameterName, failure.ParamName);
    }

    [Fact]
    public void FromModel_AnOriginLongerThanTheBound_IsRefusedRatherThanShortened()
    {
        // Arrange
        var model = new string('m', SpamSignalProvenance.MaximumOriginLength);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => SpamSignalProvenance.FromModel(model, "3f9a1c"));
    }

    [Fact]
    public void Restore_ARecordedPair_ComparesEqualToTheOneItWasBuiltFrom()
    {
        // Arrange
        var recorded = SpamSignalProvenance.FromMessageHeader("X-Spam-Status");

        // Act
        var restored = SpamSignalProvenance.Restore(recorded.Source, recorded.Origin);

        // Assert
        Assert.Equal(recorded, restored);
    }

    [Fact]
    public void Restore_ASourceOutsideTheDeclaredSet_IsRefused()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SpamSignalProvenance.Restore((SpamSignalSource)9, "X-Spam-Flag"));
    }

    [Fact]
    public void FromMessageHeader_AnOriginLongerThanTheBound_IsRefusedRatherThanShortened()
    {
        // Arrange
        var written = new string('h', SpamSignalProvenance.MaximumOriginLength + 1);

        // Act, Assert
        var failure = Assert.Throws<ArgumentException>(() => SpamSignalProvenance.FromMessageHeader(written));

        Assert.Equal("headerFieldName", failure.ParamName);
    }
}
