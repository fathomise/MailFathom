// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Infrastructure.Mail.Mime;
using MimeKit;
using MimeKit.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;

namespace MailFathom.Infrastructure.Mail.Dkim;

/// <summary>Verifies a message against the keys of the receiving account's organization, and decides whether it proves the author a member.</summary>
/// <remarks>
/// <para>
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0042-signing-an-organizations-mail-and-trusting-a-sender-its-signature-proves.md">ADR 0042</see>
/// decides every rule here. The signature is a DKIM value written under <see cref="HeaderName" />, which MimeKit's
/// verifier refuses, so each value is handed to it as a <c>DKIM-Signature</c> header rebuilt in memory from the exact
/// bytes the message carried; the message itself is never modified. The key locator resolves a selector to a key of the
/// receiving account's organization and to nothing else: <c>d=</c> is never read and no DNS is asked.
/// </para>
/// <para>
/// A member is recognized only where all three hold: a signature verifies against a key of the organization that is not
/// revoked; the message carries exactly one <c>From</c> field naming exactly one mailbox, whose address is the outgoing
/// sender identity of an account in that organization now; and the message was sent to the receiving account — its
/// sender identity is in the signed <c>To</c> or <c>Cc</c>, or the record of the send through the author's account with
/// the signed <c>Message-ID</c> lists it as a blind copy. The last condition is what refuses a validly signed message
/// replayed to somebody it never named.
/// </para>
/// <para>
/// A signature that fails, names no key of the organization, or names a revoked one is never a warning: a relay may
/// legitimately have altered the message, so it contributes nothing and the message is judged by the other sources.
/// Nothing here is logged — an address, a key, and an organization are each personal or security data, and the verdict
/// is where the answer is recorded.
/// </para>
/// </remarks>
internal sealed class OrganizationSignatureVerifier
{
    /// <summary>The header an organization's signature is written under, which no server outside the deployment reads.</summary>
    internal const string HeaderName = "MailFathom-Organization-Signature";

    /// <summary>The only algorithm an organization signs with.</summary>
    private const string Algorithm = "ed25519-sha256";

    /// <summary>The length of an Ed25519 public key, which every key the store holds has.</summary>
    private const int PublicKeyLength = 32;

    private readonly IOrganizationSenderDirectory directory;
    private readonly IOutgoingSenderIdentityReader identities;

    /// <summary>Initializes a verifier over the deployment's own state.</summary>
    /// <param name="directory">Answers the organization, its keys, its accounts, and the records of its sends.</param>
    /// <param name="identities">Resolves the address each account sends as.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public OrganizationSignatureVerifier(IOrganizationSenderDirectory directory, IOutgoingSenderIdentityReader identities)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(identities);

        this.directory = directory;
        this.identities = identities;
    }

    /// <summary>Verifies one arrival for the account that received it.</summary>
    /// <param name="receivingAccount">The account the message arrived in.</param>
    /// <param name="message">The parsed message.</param>
    /// <param name="cancellationToken">Cancels the verification.</param>
    /// <returns>What it established, or <see langword="null" /> where the account belongs to no organization and nothing was verified.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message" /> is <see langword="null" />.</exception>
    public async Task<OrganizationSenderVerification?> VerifyAsync(
        MailAccountId receivingAccount,
        MimeMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (await this.directory.FindOrganizationAsync(receivingAccount, cancellationToken) is not { } organizationId)
        {
            return null;
        }

        var senderIsMember = await this.VerifiesAsync(organizationId, message, cancellationToken)
            && await this.ProvesMembershipAsync(organizationId, receivingAccount, message, cancellationToken);

        return new OrganizationSenderVerification(organizationId, senderIsMember);
    }

    /// <summary>Answers whether any of the message's organization signatures verifies against one of the organization's keys.</summary>
    /// <remarks>
    /// The number of headers is written by whoever sent the message, so it is bounded by the same count the local DKIM
    /// verification reads, and a header naming any other algorithm is passed over before a key is looked up.
    /// </remarks>
    private async Task<bool> VerifiesAsync(Guid organizationId, MimeMessage message, CancellationToken cancellationToken)
    {
        var signatures = message.Headers
            .Where(static header => header.Field.Equals(HeaderName, StringComparison.OrdinalIgnoreCase))
            .Where(static header => DkimSignatureTags.NamesAlgorithm(header.Value, Algorithm))
            .Take(LocalSenderAuthenticationReading.MaximumVerifiedSignaturesPerMessage)
            .ToArray();

        var verifier = new DkimVerifier(new OrganizationKeyLocator(this.directory, organizationId));

        foreach (var signature in signatures)
        {
            if (await VerifiedAsync(verifier, message, AsDkimSignature(signature), cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Answers whether a verified message's author is a current member who sent it to the receiving account.</summary>
    /// <remarks>
    /// The headers read here are the ones the signature covers, each over-signed so no instance can be inserted above
    /// it; refusing a second <c>From</c> field here as well keeps the condition true even of a signature that somehow
    /// covered one.
    /// </remarks>
    private async Task<bool> ProvesMembershipAsync(
        Guid organizationId,
        MailAccountId receivingAccount,
        MimeMessage message,
        CancellationToken cancellationToken)
    {
        if (SoleAuthorOf(message) is not { } author
            || this.identities.FindSenderIdentity(receivingAccount)?.Address is not { } receivingAddress)
        {
            return false;
        }

        var accounts = await this.directory.ListAccountsAsync(organizationId, cancellationToken);
        MailAccountId[] authorAccounts =
        [
            .. accounts.Where(account => this.identities.FindSenderIdentity(account)?.Address == author),
        ];

        if (authorAccounts.Length == 0)
        {
            return false;
        }

        if (message.To.Mailboxes.Concat(message.Cc.Mailboxes).Any(recipient => AddressOf(recipient) == receivingAddress))
        {
            return true;
        }

        return InternetMessageIdReader.Read(message.Headers) is { } internetMessageId
            && (await this.directory.ListBlindCopyRecipientsAsync(authorAccounts, internetMessageId, cancellationToken))
                .Contains(receivingAddress);
    }

    /// <summary>Reads the one mailbox the message's one <c>From</c> field names, or nothing where it names any other number.</summary>
    private static EmailAddress? SoleAuthorOf(MimeMessage message) =>
        message.Headers.Count(static header => header.Id == HeaderId.From) == 1
        && message.From is [MailboxAddress mailbox]
            ? AddressOf(mailbox)
            : null;

    private static EmailAddress? AddressOf(MailboxAddress mailbox) =>
        EmailAddress.TryCreate(displayName: null, mailbox.Address, out var address) ? address : null;

    /// <summary>Rebuilds a signature as the <c>DKIM-Signature</c> header it was made as, from the exact bytes it arrived in.</summary>
    /// <remarks>
    /// The raw value is carried over rather than the decoded one, so the header MimeKit canonicalizes is byte for byte
    /// the value that was signed and only its name differs — which relaxed canonicalization lowercases and signs, and is
    /// the name the signer wrote before the rename.
    /// </remarks>
    private static Header AsDkimSignature(Header signature)
    {
        var rebuilt = new Header(HeaderId.DkimSignature, signature.Value);
        rebuilt.SetRawValue(signature.RawValue);

        return rebuilt;
    }

    /// <summary>Verifies one signature, answering no wherever nothing could be checked.</summary>
    private static async Task<bool> VerifiedAsync(
        DkimVerifier verifier,
        MimeMessage message,
        Header signature,
        CancellationToken cancellationToken)
    {
        try
        {
            return await verifier.VerifyAsync(message, signature, cancellationToken);
        }
        catch (DkimPublicKeyUnavailableException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Hands MimeKit a key of one organization by the selector a signature names, and no other key.</summary>
    private sealed class OrganizationKeyLocator(IOrganizationSenderDirectory directory, Guid organizationId)
        : DkimPublicKeyLocatorBase
    {
        /// <inheritdoc />
        /// <exception cref="NotSupportedException">Always, because every verification here is asynchronous.</exception>
        [SuppressMessage("Design", "CA1065:Do not raise exceptions in unexpected locations", Justification = "The synchronous half of the library's contract is unreachable here, and blocking on the asynchronous read to satisfy it is the outcome the refusal prevents.")]
        public override AsymmetricKeyParameter LocatePublicKey(
            string methods,
            string domain,
            string selector,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Organization keys are read asynchronously, so the synchronous locator is never used.");

        /// <inheritdoc />
        /// <exception cref="DkimPublicKeyUnavailableException">Thrown when the selector names no usable key of the organization.</exception>
        public override async Task<AsymmetricKeyParameter> LocatePublicKeyAsync(
            string methods,
            string domain,
            string selector,
            CancellationToken cancellationToken = default)
        {
            if (!Guid.TryParse(selector, out var keyId)
                || await directory.FindVerifyingKeyAsync(organizationId, keyId, cancellationToken) is not { Length: PublicKeyLength } publicKey)
            {
                throw new DkimPublicKeyUnavailableException();
            }

            return new Ed25519PublicKeyParameters(publicKey.Span);
        }
    }
}
