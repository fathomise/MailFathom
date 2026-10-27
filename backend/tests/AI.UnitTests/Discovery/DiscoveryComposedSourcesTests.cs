// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Discovery;
using MailFathom.Application.Retrieval;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.AI.UnitTests.Discovery;

/// <summary>Covers which sources a run declares out of what its lookups retrieved.</summary>
public sealed class DiscoveryComposedSourcesTests
{
    private static readonly StoredEmailId Correction = StoredEmailId.Create(Guid.CreateVersion7());
    private static readonly StoredEmailId Announcement = StoredEmailId.Create(Guid.CreateVersion7());

    /// <summary>Two lookups cut one message around different words, and the later cut is as often the one that answers.</summary>
    [Fact]
    public void Declare_TwoPassagesOfOneMessage_HandsBothOverUnderItsOneSource()
    {
        // Act
        var sources = DiscoveryComposedSources.Declare(
        [
            Passage(Correction, "Best regards, Ingrid"),
            Passage(Announcement, "the move is on Saturday"),
            Passage(Correction, "the move is on Sunday, not on Saturday"),
        ]);

        // Assert
        Assert.Equal(["s1", "s2"], sources.Select(source => source.Citation.Id.Value));
        Assert.Equal("Best regards, Ingrid\nthe move is on Sunday, not on Saturday", sources[0].Extract);
    }

    /// <summary>A passage every lookup reached alike is one extract, so it is shown once rather than once per lookup.</summary>
    [Fact]
    public void Declare_OnePassageTwoLookupsReachedAlike_ShowsItOnce()
    {
        // Act
        var sources = DiscoveryComposedSources.Declare(
        [
            Passage(Correction, "the move is on Sunday"),
            Passage(Correction, "the move is on Sunday"),
        ]);

        // Assert
        Assert.Equal("the move is on Sunday", Assert.Single(sources).Extract);
    }

    /// <summary>When a message arrived is what orders a correction after what it corrects, so the source carries it.</summary>
    [Fact]
    public void Declare_AMessageThatArrivedAtAKnownTime_CarriesThatTime()
    {
        // Arrange
        var arrivedAt = new DateTimeOffset(2026, 8, 4, 9, 0, 0, TimeSpan.Zero);

        // Act
        var sources = DiscoveryComposedSources.Declare([Passage(Correction, "the move is on Sunday") with { ReceivedAt = arrivedAt }]);

        // Assert
        Assert.Equal(arrivedAt, Assert.Single(sources).ReceivedAt);
    }

    /// <summary>Where a message came from travels with its source, so the turn can qualify a finding drawn on it.</summary>
    [Fact]
    public void Declare_AMessageFromAFirstContactInJunk_CarriesThatProvenance()
    {
        // Act
        var sources = DiscoveryComposedSources.Declare(
            [Passage(Correction, "our account has changed") with { Provenance = StubEmailProvenanceReader.UnverifiedStranger }]);

        // Assert
        Assert.Same(StubEmailProvenanceReader.UnverifiedStranger, Assert.Single(sources).Provenance);
    }

    private static EmailKnowledgePassage Passage(StoredEmailId message, string text) => new()
    {
        StoredEmailId = message,
        AccountId = MailAccountId.Create("work"),
        Folder = new StoredEmailFolder(MailFolderId.Create(Guid.Parse("83ecb521-bf32-7f67-8287-e8b923809bb3")), "INBOX", MailFolderAlias.Create("INBOX")),
        Subject = "Kestrel Quay move",
        ReceivedAt = null,
        Provenance = StubEmailProvenanceReader.KnownCorrespondent,
        MachineAuthorship = MachineAuthorshipAssessment.NotAssessed,
        Text = text,
    };
}
