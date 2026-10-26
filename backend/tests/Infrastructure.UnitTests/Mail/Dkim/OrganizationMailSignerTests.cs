// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using MailFathom.Application.Access.Organizations.SigningKeys;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Common;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.DataEncryption;
using MailFathom.Infrastructure.Mail.Dkim;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Security.SigningKeys;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MimeKit;
using MimeKit.Cryptography;
using NSubstitute;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Dkim;

/// <summary>Covers the organization signature on outgoing mail: what it covers, that it verifies through the rename, and that it never refuses a send.</summary>
/// <remarks>
/// Every signature here is real and is verified by MimeKit's own verifier against the public half of the key that made
/// it, the way a receiving account of the organization will: a <c>DKIM-Signature</c> header is rebuilt from the
/// signature's value in memory and handed to the verifier beside the parsed message.
/// </remarks>
public sealed class OrganizationMailSignerTests
{
    private const string RingKeyId = "2026-10";

    private const string AuthorDomain = "organization.example.test";

    private static readonly MailAccountId Account = MailAccountId.Create("0199a0c0-0000-7000-8000-0000000000b1");

    private static readonly Guid KeyId = new("0199a0c0-0000-7000-8000-0000000000a1");

    private static readonly string[] SignedHeaderNames =
        ["from", "reply-to", "to", "cc", "subject", "date", "message-id", "in-reply-to", "references"];

    private readonly IOrganizationSigningKeyTelemetry telemetry = Substitute.For<IOrganizationSigningKeyTelemetry>();

    private readonly IAccountSigningKeyReader keys = Substitute.For<IAccountSigningKeyReader>();

    private readonly FieldEncryptor fieldEncryptor = CreateEncryptor();

    private readonly Ed25519PrivateKeyParameters privateKey = new(new SecureRandom());

    [Fact]
    public async Task SignAsync_AnAccountItsOrganizationHoldsAKeyFor_AddsOneSignatureThatVerifiesAgainstTheKey()
    {
        // Arrange
        await this.HoldActiveKeyAsync();
        var rawMime = ComposeMessage();

        // Act
        var signed = await this.CreateSigner().SignAsync(Account, rawMime, TestContext.Current.CancellationToken);

        // Assert
        using var message = Parse(signed);
        var signature = Assert.Single(message.Headers, header => header.Field == OrganizationMailSigner.SignatureHeaderName);
        Assert.DoesNotContain(message.Headers, header => header.Id is HeaderId.DkimSignature);
        Assert.True(this.Verifies(message, signature));
        this.telemetry.DidNotReceive().MessageSentUnsigned();
    }

    /// <summary>The tags are ADR 0042's, and each signed name is listed twice so an inserted instance falls inside the signature.</summary>
    [Fact]
    public async Task SignAsync_AnAccountItsOrganizationHoldsAKeyFor_WritesTheTagsTheDecisionSettles()
    {
        // Arrange
        await this.HoldActiveKeyAsync();

        // Act
        var signed = await this.CreateSigner().SignAsync(Account, ComposeMessage(), TestContext.Current.CancellationToken);

        // Assert
        using var message = Parse(signed);
        var signature = message.Headers[OrganizationMailSigner.SignatureHeaderName];
        Assert.NotNull(signature);
        var tags = TagsOf(signature);
        Assert.Equal("ed25519-sha256", tags["a"]);
        Assert.Equal("relaxed/relaxed", tags["c"]);
        Assert.Equal(AuthorDomain, tags["d"]);
        Assert.Equal(KeyId.ToString("D"), tags["s"]);
        Assert.Equal(
            SignedHeaderNames.SelectMany(name => new[] { name, name }),
            tags["h"].Split(':'),
            StringComparer.OrdinalIgnoreCase);
        Assert.False(tags.ContainsKey("x"));
        Assert.False(tags.ContainsKey("l"));
    }

    /// <summary>An author on an internationalized domain is signed for in the domain's ASCII form, which is how <c>d=</c> writes a domain.</summary>
    [Fact]
    public async Task SignAsync_AnAuthorOnAnInternationalizedDomain_SignsForTheDomainsAsciiForm()
    {
        // Arrange
        await this.HoldActiveKeyAsync();
        const string asciiDomain = "xn--bcher-kva.example.test";

        // Act
        var signed = await this.CreateSigner().SignAsync(
            Account,
            ComposeMessage("anna@bücher.example.test"),
            TestContext.Current.CancellationToken);

        // Assert
        using var message = Parse(signed);
        var signature = Assert.Single(message.Headers, header => header.Field == OrganizationMailSigner.SignatureHeaderName);
        Assert.Equal(asciiDomain, TagsOf(signature.Value)["d"]);
        Assert.True(this.Verifies(message, signature, asciiDomain));
    }

    /// <summary>
    /// The signature is added above the message, and nothing already in it is rewritten or removed — a provider's own
    /// <c>DKIM-Signature</c> included.
    /// </summary>
    [Fact]
    public async Task SignAsync_AMessageCarryingAHeaderOfItsOwn_KeepsEveryByteOfItBelowTheSignature()
    {
        // Arrange
        await this.HoldActiveKeyAsync();
        var foreignSignature = Encoding.ASCII.GetBytes("DKIM-Signature: v=1; a=rsa-sha256; d=provider.example.test; s=one; b=AAAA\r\n");
        byte[] rawMime = [.. foreignSignature, .. ComposeMessage()];

        // Act
        var signed = (await this.CreateSigner().SignAsync(Account, rawMime, TestContext.Current.CancellationToken)).ToArray();

        // Assert
        Assert.StartsWith(OrganizationMailSigner.SignatureHeaderName + ":", Encoding.ASCII.GetString(signed), StringComparison.Ordinal);
        Assert.Equal(rawMime, signed[^rawMime.Length..]);
        using var message = Parse(signed);
        Assert.Single(message.Headers, header => header.Id is HeaderId.DkimSignature);
    }

    /// <summary>A second <c>From</c> inserted above the signed one is inside the over-signed list, so it breaks the signature.</summary>
    [Fact]
    public async Task SignAsync_ASecondFromInsertedAfterSigning_NoLongerVerifies()
    {
        // Arrange
        await this.HoldActiveKeyAsync();
        var signed = await this.CreateSigner().SignAsync(Account, ComposeMessage(), TestContext.Current.CancellationToken);
        using var message = Parse(signed);
        var signature = message.Headers[message.Headers.IndexOf(OrganizationMailSigner.SignatureHeaderName)];

        // Act
        message.Headers.Insert(1, new Header(HeaderId.From, "ceo@organization.example.test"));

        // Assert
        Assert.False(this.Verifies(message, signature));
    }

    /// <summary>A <c>Reply-To</c> absent at signing is signed as absent, so inserting one breaks the signature.</summary>
    [Fact]
    public async Task SignAsync_AReplyToInsertedAfterSigning_NoLongerVerifies()
    {
        // Arrange
        await this.HoldActiveKeyAsync();
        var signed = await this.CreateSigner().SignAsync(Account, ComposeMessage(), TestContext.Current.CancellationToken);
        using var message = Parse(signed);
        var signature = message.Headers[message.Headers.IndexOf(OrganizationMailSigner.SignatureHeaderName)];

        // Act
        message.ReplyTo.Add(MailboxAddress.Parse("attacker@elsewhere.example.test"));

        // Assert
        Assert.False(this.Verifies(message, signature));
    }

    [Fact]
    public async Task SignAsync_AnAccountInNoOrganization_ReturnsTheMessageUnchangedAndCountsNothing()
    {
        // Arrange
        this.keys.ReadAsync(Account, Arg.Any<CancellationToken>()).Returns(AccountSigningKeyReading.OutsideEveryOrganization);
        var rawMime = ComposeMessage();

        // Act
        var signed = await this.CreateSigner().SignAsync(Account, rawMime, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(rawMime, signed.ToArray());
        this.telemetry.DidNotReceive().MessageSentUnsigned();
    }

    /// <summary>A send is never refused for want of a signature: an organization holding no key sends unsigned and is counted.</summary>
    [Fact]
    public async Task SignAsync_AnOrganizationWithoutAnActiveKey_SendsUnsignedAndCountsIt()
    {
        // Arrange
        this.keys.ReadAsync(Account, Arg.Any<CancellationToken>()).Returns(AccountSigningKeyReading.WithoutActiveKey);
        var rawMime = ComposeMessage();

        // Act
        var signed = await this.CreateSigner().SignAsync(Account, rawMime, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(rawMime, signed.ToArray());
        this.telemetry.Received(1).MessageSentUnsigned();
    }

    /// <summary>A sealed half that does not open is the deployment's fault, never the sender's, so the message still leaves.</summary>
    [Fact]
    public async Task SignAsync_AKeyWhosePrivateHalfDoesNotOpen_SendsUnsignedAndCountsIt()
    {
        // Arrange
        var sealedForAnotherKey = await this.fieldEncryptor.SealAsync(
            OrganizationSigningKeyMinter.BindingFor(Guid.CreateVersion7()),
            this.privateKey.GetEncoded(),
            TestContext.Current.CancellationToken);
        this.keys.ReadAsync(Account, Arg.Any<CancellationToken>()).Returns(AccountSigningKeyReading.Active(KeyId, sealedForAnotherKey));
        var rawMime = ComposeMessage();

        // Act
        var signed = await this.CreateSigner().SignAsync(Account, rawMime, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(rawMime, signed.ToArray());
        this.telemetry.Received(1).MessageSentUnsigned();
    }

    /// <summary>A ring key removed from configuration while a signing key is still sealed under it stops the signature, never the mail.</summary>
    [Fact]
    public async Task SignAsync_AKeySealedUnderARingKeyNoLongerConfigured_SendsUnsignedAndCountsIt()
    {
        // Arrange
        var sealedUnderRetiredRingKey = await CreateEncryptor("2025-01").SealAsync(
            OrganizationSigningKeyMinter.BindingFor(KeyId),
            this.privateKey.GetEncoded(),
            TestContext.Current.CancellationToken);
        this.keys.ReadAsync(Account, Arg.Any<CancellationToken>()).Returns(AccountSigningKeyReading.Active(KeyId, sealedUnderRetiredRingKey));
        var rawMime = ComposeMessage();

        // Act
        var signed = await this.CreateSigner().SignAsync(Account, rawMime, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(rawMime, signed.ToArray());
        this.telemetry.Received(1).MessageSentUnsigned();
    }

    /// <summary>The bound was measured on the composed bytes, so a signature that would carry them past it is left off rather than sent over it.</summary>
    [Fact]
    public async Task SignAsync_AMessageTheSignatureWouldCarryPastTheBound_SendsUnsignedAndCountsIt()
    {
        // Arrange
        await this.HoldActiveKeyAsync();
        var rawMime = ComposeMessage();
        var withinOnlyUnsigned = rawMime.Length + 16;

        // Act
        var signed = await this.CreateSigner(withinOnlyUnsigned).SignAsync(Account, rawMime, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(rawMime, signed.ToArray());
        this.telemetry.Received(1).MessageSentUnsigned();
    }

    /// <summary>A message naming no author has no domain for <c>d=</c>, and is sent unsigned rather than refused.</summary>
    [Fact]
    public async Task SignAsync_AMessageNamingNoAuthor_SendsUnsignedAndCountsIt()
    {
        // Arrange
        await this.HoldActiveKeyAsync();
        var rawMime = Encoding.ASCII.GetBytes("To: bob@example.test\r\nSubject: synthetic\r\n\r\nbody\r\n");

        // Act
        var signed = await this.CreateSigner().SignAsync(Account, rawMime, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(rawMime, signed.ToArray());
        this.telemetry.Received(1).MessageSentUnsigned();
    }

    private static FieldEncryptor CreateEncryptor(string ringKeyId = RingKeyId)
    {
        var resolver = new ProvisionedMaterialResolver();
        resolver.ProvisionText("plaintext:active", Convert.ToBase64String([.. Enumerable.Repeat((byte)0x11, AesGcmEnvelope.KeySizeInBytes)]));

        var settings = new DataEncryptionKeyRingSettings(
            ringKeyId,
            [new DataEncryptionKeyReference(ringKeyId, new ConfiguredSecret { Name = "data-key-active", SecretReference = "plaintext:active" })]);

        return new FieldEncryptor(new DataEncryptionKeyRing(() => settings, resolver));
    }

    private static byte[] ComposeMessage(string author = $"anna@{AuthorDomain}")
    {
        using var message = new MimeMessage
        {
            Subject = "Quarterly invoice",
            Date = new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero),
            MessageId = "signed-1@organization.example.test",
            Body = new TextPart("plain") { Text = "Dzień dobry." },
        };
        message.From.Add(MailboxAddress.Parse(author));
        message.To.Add(MailboxAddress.Parse("bob@example.test"));
        message.Prepare(EncodingConstraint.SevenBit);

        var format = FormatOptions.Default.Clone();
        format.NewLineFormat = NewLineFormat.Dos;
        format.International = true;

        using var rawMime = new MemoryStream();
        message.WriteTo(format, rawMime);

        return rawMime.ToArray();
    }

    private static MimeMessage Parse(ReadOnlyMemory<byte> rawMime) => DkimFixtures.Parse(rawMime.ToArray());

    private static Dictionary<string, string> TagsOf(string headerValue) =>
        headerValue
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(tag => tag.Split('=', 2))
            .ToDictionary(
                pair => pair[0],
                pair => string.Concat(pair[1].Where(static character => !char.IsWhiteSpace(character))));

    private async Task HoldActiveKeyAsync()
    {
        var sealedPrivateKey = await this.fieldEncryptor.SealAsync(
            OrganizationSigningKeyMinter.BindingFor(KeyId),
            this.privateKey.GetEncoded(),
            TestContext.Current.CancellationToken);

        this.keys.ReadAsync(Account, Arg.Any<CancellationToken>()).Returns(AccountSigningKeyReading.Active(KeyId, sealedPrivateKey));
    }

    private OrganizationMailSigner CreateSigner(long maxMessageBytes = 1_000_000) => new(
        this.keys,
        this.fieldEncryptor,
        this.telemetry,
        new OutgoingEmailBounds
        {
            MaxRecipientCount = 10,
            MaxBodyCharacters = 10_000,
            MaxAttachmentCount = 0,
            MaxAttachmentBytes = 0,
            MaxMessageBytes = maxMessageBytes,
        });

    /// <summary>Verifies the way the receiving side will: a <c>DKIM-Signature</c> rebuilt from the value, against the public half.</summary>
    private bool Verifies(MimeMessage message, Header signature, string signingDomain = AuthorDomain)
    {
        var locator = Substitute.For<IDkimPublicKeyLocator>();
        locator
            .LocatePublicKey(Arg.Any<string>(), signingDomain, KeyId.ToString("D"), Arg.Any<CancellationToken>())
            .Returns(this.privateKey.GeneratePublicKey());

        return new DkimVerifier(locator).Verify(message, new Header(HeaderId.DkimSignature, signature.Value));
    }
}
