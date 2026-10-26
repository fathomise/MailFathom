// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Folders;
using Xunit;

namespace MailFathom.Domain.UnitTests.Folders;

public sealed class MailFolderParticipationTests
{
    /// <summary>A mapping that says nothing about the three switches behaves exactly as a mapping did before they existed.</summary>
    [Fact]
    public void Full_ByItself_TakesPartInEverything()
    {
        // Act
        var participation = MailFolderParticipation.Full;

        // Assert
        Assert.True(participation.IsMapped);
        Assert.True(participation.IsSynchronized);
        Assert.True(participation.GeneratesEmbeddings);
        Assert.True(participation.IsVisibleToAI);
    }

    /// <summary>A folder no mapping names does not exist here, so nothing it once stored takes part in anything.</summary>
    [Fact]
    public void Unmapped_ByItself_TakesPartInNothing()
    {
        // Act
        var participation = MailFolderParticipation.Unmapped;

        // Assert
        Assert.False(participation.IsMapped);
        Assert.False(participation.IsSynchronized);
        Assert.False(participation.GeneratesEmbeddings);
        Assert.False(participation.IsVisibleToAI);
    }

    /// <summary>A folder an operator stopped mirroring and a folder they stopped mapping are two decisions, so one value cannot stand for both.</summary>
    [Fact]
    public void Unmapped_AgainstMappedOnly_IsADifferentValue()
    {
        // Act, Assert
        Assert.NotEqual(MailFolderParticipation.MappedOnly, MailFolderParticipation.Unmapped);
        Assert.True(MailFolderParticipation.MappedOnly.IsMapped);
    }

    /// <summary>An unmirrored folder stores nothing, so neither of the other two answers can be anything but no.</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Create_SynchronizationOff_WithdrawsEmbeddingAndVisibilityToAIWhateverTheySaid(
        bool generatesEmbeddings,
        bool isVisibleToAI)
    {
        // Act
        var participation = MailFolderParticipation.Create(
            isSynchronized: false,
            generatesEmbeddings,
            isVisibleToAI);

        // Assert
        Assert.False(participation.IsSynchronized);
        Assert.False(participation.GeneratesEmbeddings);
        Assert.False(participation.IsVisibleToAI);
    }

    /// <summary>A folder visible to AI may still go unembedded, and a withheld one stays mirrored and unembedded.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Create_SynchronizationOn_KeepsTheOtherTwoAnswersAsWritten(
        bool generatesEmbeddings,
        bool isVisibleToAI)
    {
        // Act
        var participation = MailFolderParticipation.Create(
            isSynchronized: true,
            generatesEmbeddings,
            isVisibleToAI);

        // Assert
        Assert.True(participation.IsSynchronized);
        Assert.Equal(generatesEmbeddings, participation.GeneratesEmbeddings);
        Assert.Equal(isVisibleToAI, participation.IsVisibleToAI);
    }

    /// <summary>An embedding is a model's reading of the folder, so a folder withheld from AI is embedded by nobody whatever the mapping asked.</summary>
    [Fact]
    public void Create_WithheldFromAI_WithdrawsEmbeddings()
    {
        // Act
        var participation = MailFolderParticipation.Create(
            isSynchronized: true,
            generatesEmbeddings: true,
            isVisibleToAI: false);

        // Assert
        Assert.True(participation.IsSynchronized);
        Assert.False(participation.GeneratesEmbeddings);
        Assert.False(participation.IsVisibleToAI);
    }

    /// <summary>A folder withheld from AI is still the person's folder, so the person reaches it while an agent does not.</summary>
    [Fact]
    public void Admits_WithheldFromAI_AdmitsThePersonAndNotAnAgent()
    {
        // Arrange
        var participation = MailFolderParticipation.Create(
            isSynchronized: true,
            generatesEmbeddings: false,
            isVisibleToAI: false);

        // Act, Assert
        Assert.True(participation.Admits(MailboxReaderKind.Person));
        Assert.False(participation.Admits(MailboxReaderKind.Agent));
    }

    /// <summary>A folder nothing mirrors holds only what an earlier configuration left, so neither reader is shown it.</summary>
    [Fact]
    public void Admits_MappedOnly_AdmitsNoReader()
    {
        // Act, Assert
        Assert.False(MailFolderParticipation.MappedOnly.Admits(MailboxReaderKind.Person));
        Assert.False(MailFolderParticipation.MappedOnly.Admits(MailboxReaderKind.Agent));
    }

    /// <summary>A value outside the two readers names nobody, so it is refused rather than read as either.</summary>
    [Fact]
    public void Admits_UndefinedReader_Throws()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => MailFolderParticipation.Full.Admits((MailboxReaderKind)7));
    }

    /// <summary>The two named values are the ones every other reading is compared against, so they must be the values the factory produces.</summary>
    [Fact]
    public void Create_TheTwoExtremes_AreTheNamedValues()
    {
        // Act, Assert
        Assert.Equal(
            MailFolderParticipation.Full,
            MailFolderParticipation.Create(isSynchronized: true, generatesEmbeddings: true, isVisibleToAI: true));
        Assert.Equal(
            MailFolderParticipation.MappedOnly,
            MailFolderParticipation.Create(isSynchronized: false, generatesEmbeddings: true, isVisibleToAI: true));
    }

    /// <summary>The three switches are what a mapping says, so every value built from them is a mapped folder's.</summary>
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    public void Create_AnyThreeAnswers_DescribeAMappedFolder(
        bool isSynchronized,
        bool generatesEmbeddings,
        bool isVisibleToAI)
    {
        // Act
        var participation = MailFolderParticipation.Create(
            isSynchronized,
            generatesEmbeddings,
            isVisibleToAI);

        // Assert
        Assert.True(participation.IsMapped);
    }
}
