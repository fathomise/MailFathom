// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Infrastructure.Mail.Dkim;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MimeKit;
using NSubstitute;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Dkim;

public sealed class OrganizationSignatureVerifierTests
{
    private static readonly Guid Organization = Guid.Parse("0199f000-0000-7000-8000-000000000001");
    private static readonly Guid OtherOrganization = Guid.Parse("0199f000-0000-7000-8000-000000000002");
    private static readonly MailAccountId Author = MailAccountId.Create("0199f000-0000-7000-8000-00000000000a");
    private static readonly MailAccountId Receiver = MailAccountId.Create("0199f000-0000-7000-8000-00000000000b");

    /// <summary>A message signed by a member's key, written by a member, and sent to the receiving account proves its author.</summary>
    [Fact]
    public async Task VerifyAsync_EveryConditionHolds_RecognizesTheAuthorAsAMember()
    {
        // Arrange
        var (directory, key) = Directory();
        using var message = Signed(key);

        // Act
        var verification = await Verifier(directory).VerifyAsync(Receiver, message, CancellationToken.None);

        // Assert
        Assert.NotNull(verification);
        Assert.Equal(Organization, verification.ReceivingOrganizationId);
        Assert.True(verification.SenderIsMember);
        Assert.Equal([Author], verification.AuthorAccounts);
    }

    /// <summary>A receiving account in no organization verifies nothing and reads no key.</summary>
    [Fact]
    public async Task VerifyAsync_ReceivingAccountInNoOrganization_RunsNoVerification()
    {
        // Arrange
        var (directory, key) = Directory();
        directory.Memberships.Remove(Receiver);
        using var message = Signed(key);

        // Act
        var verification = await Verifier(directory).VerifyAsync(Receiver, message, CancellationToken.None);

        // Assert
        Assert.Null(verification);
        Assert.Equal(0, directory.KeyReads);
    }

    /// <summary>A key of another organization confirms nothing for this one, however valid the signature.</summary>
    [Fact]
    public async Task VerifyAsync_KeyOfAnotherOrganization_DoesNotRecognizeTheAuthor()
    {
        // Arrange
        var (directory, _) = Directory();
        var foreignKey = OrganizationSignatureFixtures.CreateKey();
        directory.Holding(OtherOrganization, foreignKey);
        using var message = Signed(foreignKey);

        // Act
        var verification = await Verifier(directory).VerifyAsync(Receiver, message, CancellationToken.None);

        // Assert
        Assert.False(verification?.SenderIsMember);
    }

    /// <summary>A revoked key confirms nothing for any message verified after the revocation.</summary>
    [Fact]
    public async Task VerifyAsync_RevokedKey_DoesNotRecognizeTheAuthor()
    {
        // Arrange
        var (directory, key) = Directory();
        directory.Keys[key.KeyId] = directory.Keys[key.KeyId] with { Revoked = true };
        using var message = Signed(key);

        // Act
        var verification = await Verifier(directory).VerifyAsync(Receiver, message, CancellationToken.None);

        // Assert
        Assert.False(verification?.SenderIsMember);
    }

    /// <summary>A retired key keeps its public half, so mail it signed still verifies.</summary>
    [Fact]
    public async Task VerifyAsync_RetiredKeyStillHeld_RecognizesTheAuthor()
    {
        // Arrange
        var (directory, retired) = Directory();
        var active = OrganizationSignatureFixtures.CreateKey();
        directory.Holding(Organization, active);
        using var message = Signed(retired);

        // Act
        var verification = await Verifier(directory).VerifyAsync(Receiver, message, CancellationToken.None);

        // Assert
        Assert.True(verification?.SenderIsMember);
    }

    /// <summary>A message altered after it was signed proves nothing, and is not a statement against it either.</summary>
    [Fact]
    public async Task VerifyAsync_BodyAlteredAfterSigning_DoesNotRecognizeTheAuthor()
    {
        // Arrange
        var (directory, key) = Directory();
        using var message = Signed(key, alter: static text => text.Replace("Dzień dobry.", "Pay this invoice.", StringComparison.Ordinal));

        // Act
        var verification = await Verifier(directory).VerifyAsync(Receiver, message, CancellationToken.None);

        // Assert
        Assert.NotNull(verification);
        Assert.False(verification.SenderIsMember);
    }

    /// <summary>A second <c>From</c> inserted above the signed one is outside nothing, because every name is over-signed.</summary>
    [Fact]
    public async Task VerifyAsync_SecondFromInsertedAboveTheSignedOne_DoesNotRecognizeTheAuthor()
    {
        // Arrange
        var (directory, key) = Directory();
        using var message = Signed(key, alter: static text => "From: ceo@org.example.test\r\n" + text);

        // Act
        var verification = await Verifier(directory).VerifyAsync(Receiver, message, CancellationToken.None);

        // Assert
        Assert.False(verification?.SenderIsMember);
    }

    /// <summary>A <c>Reply-To</c> inserted after signing would send the reply elsewhere, so it breaks the proof.</summary>
    [Fact]
    public async Task VerifyAsync_ReplyToInsertedAfterSigning_DoesNotRecognizeTheAuthor()
    {
        // Arrange
        var (directory, key) = Directory();
        using var message = Signed(key, alter: static text => "Reply-To: attacker@elsewhere.example.test\r\n" + text);

        // Act
        var verification = await Verifier(directory).VerifyAsync(Receiver, message, CancellationToken.None);

        // Assert
        Assert.False(verification?.SenderIsMember);
    }

    /// <summary>A validly signed message whose author is no member's sending address now proves nobody.</summary>
    [Fact]
    public async Task VerifyAsync_AuthorIsNoMembersSenderIdentity_DoesNotRecognizeTheAuthor()
    {
        // Arrange
        var (directory, key) = Directory();
        using var message = Signed(key, from: "former@org.example.test");

        // Act
        var verification = await Verifier(directory).VerifyAsync(Receiver, message, CancellationToken.None);

        // Assert
        Assert.False(verification?.SenderIsMember);
    }

    /// <summary>A member whose IMAP user name is a bare login is matched through the address it sends as.</summary>
    [Fact]
    public async Task VerifyAsync_AuthorMatchedThroughTheSenderIdentityRatherThanTheLogin_RecognizesTheAuthor()
    {
        // Arrange
        var (directory, key) = Directory();
        var identities = Identities((Author, "anna.kowalska@org.example.test"), (Receiver, "bob@org.example.test"));
        using var message = Signed(key, from: "anna.kowalska@org.example.test");

        // Act
        var verification = await new OrganizationSignatureVerifier(directory, identities)
            .VerifyAsync(Receiver, message, CancellationToken.None);

        // Assert
        Assert.True(verification?.SenderIsMember);
    }

    /// <summary>
    /// A member's colleagues are served accounts outside the organization too, so an author sending as one of those proves
    /// nothing: membership is an account in the organization, not a user who also holds one.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_AuthorIsAMembersAccountOutsideTheOrganization_DoesNotRecognizeTheAuthor()
    {
        // Arrange
        var (directory, key) = Directory();
        var personal = MailAccountId.Create("0199f000-0000-7000-8000-00000000000c");
        var identities = Identities(
            (Author, "anna@org.example.test"),
            (Receiver, "bob@org.example.test"),
            (personal, "anna@personal.example.test"));
        using var message = Signed(key, from: "anna@personal.example.test");

        // Act
        var verification = await new OrganizationSignatureVerifier(directory, identities)
            .VerifyAsync(Receiver, message, CancellationToken.None);

        // Assert
        Assert.False(verification?.SenderIsMember);
    }

    /// <summary>A signed message replayed to somebody it never named, and never sent to as a blind copy, proves nothing to them.</summary>
    [Fact]
    public async Task VerifyAsync_ReceiverNeitherNamedNorOnTheSendRecord_DoesNotRecognizeTheAuthor()
    {
        // Arrange
        var (directory, key) = Directory();
        using var message = Signed(key, to: ["carol@org.example.test"]);

        // Act
        var verification = await Verifier(directory).VerifyAsync(Receiver, message, CancellationToken.None);

        // Assert
        Assert.False(verification?.SenderIsMember);
    }

    /// <summary>A receiver named in the signed <c>Cc</c> was sent the message as much as one in <c>To</c>.</summary>
    [Fact]
    public async Task VerifyAsync_ReceiverInTheSignedCc_RecognizesTheAuthor()
    {
        // Arrange
        var (directory, key) = Directory();
        using var message = Signed(key, to: ["carol@org.example.test"], cc: ["BOB@org.example.test"]);

        // Act
        var verification = await Verifier(directory).VerifyAsync(Receiver, message, CancellationToken.None);

        // Assert
        Assert.True(verification?.SenderIsMember);
    }

    /// <summary>A blind copy the author's account sent to the receiver is vouched for by the record of that send.</summary>
    [Fact]
    public async Task VerifyAsync_BlindCopyOnTheAuthorsSendRecord_RecognizesTheAuthor()
    {
        // Arrange
        var (directory, key) = Directory();
        directory.BlindCopies.Add((Author, "signed-1@org.example.test", "bob@org.example.test"));
        using var message = Signed(key, to: ["carol@org.example.test"]);

        // Act
        var verification = await Verifier(directory).VerifyAsync(Receiver, message, CancellationToken.None);

        // Assert
        Assert.True(verification?.SenderIsMember);
    }

    /// <summary>A blind copy recorded for another account's send vouches for nothing the author sent.</summary>
    [Fact]
    public async Task VerifyAsync_BlindCopyOnAnotherAccountsSendRecord_DoesNotRecognizeTheAuthor()
    {
        // Arrange
        var (directory, key) = Directory();
        directory.BlindCopies.Add((MailAccountId.Create("someone-else"), "signed-1@org.example.test", "bob@org.example.test"));
        using var message = Signed(key, to: ["carol@org.example.test"]);

        // Act
        var verification = await Verifier(directory).VerifyAsync(Receiver, message, CancellationToken.None);

        // Assert
        Assert.False(verification?.SenderIsMember);
        Assert.Empty(verification?.AuthorAccounts ?? []);
    }

    /// <summary>A message carrying no <c>Message-ID</c> can never be found on a send record, so a blind copy of it proves nothing.</summary>
    [Fact]
    public async Task VerifyAsync_BlindCopyWithoutAMessageId_DoesNotRecognizeTheAuthor()
    {
        // Arrange
        var (directory, key) = Directory();
        directory.BlindCopies.Add((Author, "signed-1@org.example.test", "bob@org.example.test"));
        using var message = Signed(key, to: ["carol@org.example.test"], messageId: null);

        // Act
        var verification = await Verifier(directory).VerifyAsync(Receiver, message, CancellationToken.None);

        // Assert
        Assert.False(verification?.SenderIsMember);
    }

    /// <summary>A receiving account that sends as nobody cannot be shown the message was sent to it.</summary>
    [Fact]
    public async Task VerifyAsync_ReceivingAccountWithNoSenderIdentity_DoesNotRecognizeTheAuthor()
    {
        // Arrange
        var (directory, key) = Directory();
        var identities = Identities((Author, "anna@org.example.test"));
        using var message = Signed(key);

        // Act
        var verification = await new OrganizationSignatureVerifier(directory, identities)
            .VerifyAsync(Receiver, message, CancellationToken.None);

        // Assert
        Assert.False(verification?.SenderIsMember);
    }

    /// <summary>A message with no organization signature verifies nothing, and reads no key.</summary>
    [Fact]
    public async Task VerifyAsync_MessageWithNoOrganizationSignature_ReadsNoKey()
    {
        // Arrange
        var (directory, _) = Directory();
        using var message = OrganizationSignatureFixtures.Compose();

        // Act
        var verification = await Verifier(directory).VerifyAsync(Receiver, message, CancellationToken.None);

        // Assert
        Assert.False(verification?.SenderIsMember);
        Assert.Equal(0, directory.KeyReads);
    }

    /// <summary>The sender writes how many signature headers a message carries, so no more than the bound are ever looked up.</summary>
    [Fact]
    public async Task VerifyAsync_MoreSignatureHeadersThanTheBound_ReadsAKeyForTheBoundAlone()
    {
        // Arrange
        var (directory, _) = Directory();
        using var composed = OrganizationSignatureFixtures.Compose();
        foreach (var unheld in Enumerable.Range(0, LocalSenderAuthenticationReading.MaximumVerifiedSignaturesPerMessage + 3)
            .Select(static _ => OrganizationSignatureFixtures.CreateKey()))
        {
            OrganizationSignatureFixtures.Sign(composed, unheld);
        }

        using var message = OrganizationSignatureFixtures.RoundTrip(composed).Parsed;

        // Act
        var verification = await Verifier(directory).VerifyAsync(Receiver, message, CancellationToken.None);

        // Assert
        Assert.False(verification?.SenderIsMember);
        Assert.Equal(LocalSenderAuthenticationReading.MaximumVerifiedSignaturesPerMessage, directory.KeyReads);
    }

    /// <summary>A header naming another algorithm is passed over before its selector is looked up, even one naming a held key.</summary>
    [Fact]
    public async Task VerifyAsync_SignatureNamingAnotherAlgorithm_ReadsNoKeyAndRecognizesNobody()
    {
        // Arrange
        var (directory, key) = Directory();
        using var message = Signed(key, alter: static written => written.Replace("a=ed25519-sha256", "a=rsa-sha256", StringComparison.Ordinal));

        // Act
        var verification = await Verifier(directory).VerifyAsync(Receiver, message, CancellationToken.None);

        // Assert
        Assert.False(verification?.SenderIsMember);
        Assert.Equal(0, directory.KeyReads);
    }

    private static (InMemoryOrganizationSenderDirectory Directory, OrganizationSignatureFixtures.OrganizationKey Key) Directory()
    {
        var key = OrganizationSignatureFixtures.CreateKey();
        var directory = new InMemoryOrganizationSenderDirectory();
        directory.Memberships[Author] = Organization;
        directory.Memberships[Receiver] = Organization;
        directory.Holding(Organization, key);

        return (directory, key);
    }

    private static OrganizationSignatureVerifier Verifier(InMemoryOrganizationSenderDirectory directory) =>
        new(directory, Identities((Author, "anna@org.example.test"), (Receiver, "bob@org.example.test")));

    /// <summary>Answers the identities only when asked through the organization's assigned user, as the host would compose them.</summary>
    private static ISenderIdentityDirectory Identities(params (MailAccountId Account, string Address)[] identities)
    {
        var served = identities.ToDictionary(
            static identity => identity.Account,
            static identity =>
            {
                Assert.True(EmailAddress.TryCreate(displayName: null, identity.Address, out var parsed));

                return OutgoingSenderIdentity.Create(identity.Account, parsed);
            });
        var reader = Substitute.For<ISenderIdentityDirectory>();
        reader.ReadServedIdentitiesAsync(Arg.Any<IReadOnlyCollection<UserId>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<MailAccountId, OutgoingSenderIdentity>());
        reader.ReadServedIdentitiesAsync(
                Arg.Is<IReadOnlyCollection<UserId>>(users => users.Contains(InMemoryOrganizationSenderDirectory.UserOf(Organization))),
                Arg.Any<CancellationToken>())
            .Returns(served);

        return reader;
    }

    private static MimeMessage Signed(
        OrganizationSignatureFixtures.OrganizationKey key,
        string from = "anna@org.example.test",
        IReadOnlyList<string>? to = null,
        IReadOnlyList<string>? cc = null,
        string? messageId = "signed-1@org.example.test",
        Func<string, string>? alter = null)
    {
        using var message = OrganizationSignatureFixtures.Compose(from, to, cc, messageId);
        OrganizationSignatureFixtures.Sign(message, key);

        return OrganizationSignatureFixtures.RoundTrip(message, alter).Parsed;
    }
}
