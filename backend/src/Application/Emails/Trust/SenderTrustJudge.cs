// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Mail;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Trust;

/// <summary>Decides what each receiving account's configuration and organization signature make of the author of messages about to be stored.</summary>
/// <remarks>
/// <para>
/// That is the half of the trust verdict a message stores. The contacts that hold the author's address are not read
/// here: every read of a message asks the books as they stand then and combines them with this half, so nothing about a
/// contact is fixed at extraction and no contact write ever has stored mail to re-judge.
/// </para>
/// <para>
/// Every path that stores a verdict goes through here: synchronization storing a message it fetched, the extraction
/// backfill and <c>mfctl mailbox rederive</c> re-reading stored mail, and a local copy of a message.
/// </para>
/// </remarks>
public sealed class SenderTrustJudge
{
    private readonly ISenderTrustPolicyReader policies;

    /// <summary>Initializes the judge.</summary>
    /// <param name="policies">Resolves what configuration states about each account's owner.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="policies" /> is <see langword="null" />.</exception>
    public SenderTrustJudge(ISenderTrustPolicyReader policies)
    {
        ArgumentNullException.ThrowIfNull(policies);

        this.policies = policies;
    }

    /// <summary>Reaches the stored half of each message's trust verdict.</summary>
    /// <param name="messages">What extracting each message established.</param>
    /// <returns>The messages in the same order, each carrying that half.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="messages" /> is <see langword="null" />.</exception>
    public IReadOnlyList<ExtractedEmailMetadata> Judge(IReadOnlyList<ExtractedEmailMetadata> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return [.. messages.Select(this.Judge)];
    }

    /// <summary>Reads the address a mail client displays as the message's author.</summary>
    /// <remarks>
    /// Taken from <c>From</c> alone and never from <c>Sender</c>, which names whoever submitted a message written on
    /// somebody else's behalf. The first mailbox wins where the header carried several, because a message can display
    /// only one author.
    /// </remarks>
    private static EmailAddress? DisplayedSenderOf(ExtractedEmailMetadata message) =>
        message.Participants.FirstOrDefault(participant => participant.Role == EmailAddressRole.From)?.Address;

    private ExtractedEmailMetadata Judge(ExtractedEmailMetadata message) =>
        message with
        {
            SenderTrust = this.policies.GetTrustPolicy(message.AccountId).Evaluate(
                message.SenderAuthentication,
                message.Signature,
                DisplayedSenderOf(message),
                message.OrganizationVerification),
        };
}
