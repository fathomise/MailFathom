// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Provenance;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the reader several suites arrange where a message came from with.</summary>
/// <remarks>
/// An email the reader was not told about has to be absent rather than answered with a default, because that absence is
/// how every suite arranges a message that stopped being stored, and a reader that filled it in would let a dropped
/// message pass as a present one.
/// </remarks>
public sealed class StubEmailProvenanceReaderTests
{
    private static readonly StoredEmailId First = StoredEmailId.Create(Guid.CreateVersion7());

    private static readonly StoredEmailId Second = StoredEmailId.Create(Guid.CreateVersion7());

    [Fact]
    public async Task ReadAsync_AReaderAnsweringOneProvenance_AnswersItForEveryEmailOnce()
    {
        // Arrange
        var reader = StubEmailProvenanceReader.Answering(StubEmailProvenanceReader.UnverifiedStranger);

        // Act
        var answered = await reader.ReadAsync([First, Second, First], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, answered.Count);
        Assert.All(answered.Values, provenance => Assert.Same(StubEmailProvenanceReader.UnverifiedStranger, provenance));
    }

    [Fact]
    public async Task ReadAsync_AnEmailTheReaderWasNotToldAbout_IsAbsentAsIfNoLongerStored()
    {
        // Arrange
        var reader = StubEmailProvenanceReader.Answering(
            new Dictionary<StoredEmailId, EmailProvenance> { [First] = StubEmailProvenanceReader.KnownCorrespondent });

        // Act
        var answered = await reader.ReadAsync([First, Second], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([First], answered.Keys);
        Assert.Same(StubEmailProvenanceReader.KnownCorrespondent, answered[First]);
    }

    /// <summary>The two stated provenances differ in the author verdict, the junk folder, and first contact, so a test choosing one of them proves which reached it.</summary>
    /// <remarks>Neither is trusted by the deployment, so a test about trust states a provenance of its own.</remarks>
    [Fact]
    public void KnownCorrespondent_AgainstUnverifiedStranger_DiffersInVerdictFolderAndFirstContact()
    {
        // Arrange, Act
        var known = StubEmailProvenanceReader.KnownCorrespondent;
        var stranger = StubEmailProvenanceReader.UnverifiedStranger;

        // Assert
        Assert.NotEqual(known.Sender.AuthorAuthentication, stranger.Sender.AuthorAuthentication);
        Assert.NotEqual(known.IsInJunkFolder, stranger.IsInJunkFolder);
        Assert.NotEqual(known.IsFirstContact, stranger.IsFirstContact);
    }
}
