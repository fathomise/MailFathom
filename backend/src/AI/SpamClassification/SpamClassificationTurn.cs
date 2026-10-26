// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Orchestration;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.Spam.Model;

namespace MailFathom.AI.SpamClassification;

/// <summary>Writes one message's evidence as the input a decision model reads, inside the turn-document envelope every agent here reads mail through.</summary>
/// <remarks>
/// <para>
/// Only this service names elements and attributes, and every value is escaped by <see cref="AgentTurnDocument" />, so a
/// message that closes its envelope and writes a forged <c>authentication</c> element of its own arrives as that text,
/// visible and inert. What the message cannot forge — each authentication outcome, the deployment's trust in its
/// author, and whether a link's words name another host — is written as attributes, from values the deployment read.
/// </para>
/// <para>
/// The body is written last, so the one value a budget would cut is the one already cut to the deployment's bound.
/// </para>
/// </remarks>
internal static class SpamClassificationTurn
{
    internal const string MessageElementName = "message";
    internal const string SenderElementName = "sender";
    internal const string DisplayNameElementName = "display-name";
    internal const string ReplyToElementName = "reply-to";
    internal const string AuthenticationElementName = "authentication";
    internal const string SubjectElementName = "subject";
    internal const string LinkElementName = "link";
    internal const string AttachmentElementName = "attachment";
    internal const string BodyElementName = "body";

    internal const string AddressAttributeName = "address";
    internal const string SenderTrustedAttributeName = "sender-trusted";
    internal const string MethodAttributeName = "method";
    internal const string ResultAttributeName = "result";
    internal const string ForwardedAttributeName = "forwarded";
    internal const string HostAttributeName = "host";
    internal const string DisplayedHostAttributeName = "displayed-host";
    internal const string DisplayedHostDiffersAttributeName = "displayed-host-differs";
    internal const string TypeAttributeName = "type";
    internal const string NameAttributeName = "name";

    private const string TrueValue = "true";

    /// <summary>Guards every value the account's posture reads and writes the input from what the guard returned.</summary>
    /// <param name="evidence">The evidence, as the stage read it.</param>
    /// <param name="egressGuard">Withholds from the provider whatever the account's posture withholds.</param>
    /// <param name="cancellationToken">Withdraws the scan.</param>
    /// <returns>The input text.</returns>
    /// <exception cref="Application.SensitiveContent.Detection.SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not read a value.</exception>
    /// <remarks>
    /// Everything is guarded but the two addresses, which cross as an address does at every egress point: the posture
    /// draws its line between a routing identity and free text, and ADR 0041 keeps them because a lookalike sender and a
    /// differing reply address are among the strongest signs of phishing.
    /// </remarks>
    internal static async Task<string> ComposeAsync(
        SpamModelEvidence evidence,
        SensitiveContentEgressGuard egressGuard,
        CancellationToken cancellationToken) =>
        Compose(await GuardedAsync(evidence, egressGuard, cancellationToken));

    /// <summary>Writes evidence whose values are already guarded.</summary>
    /// <param name="evidence">The guarded evidence.</param>
    /// <returns>The input text.</returns>
    internal static string Compose(SpamModelEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        return AgentTurnDocument.Compose(int.MaxValue, document => Write(document, evidence));
    }

    private static void Write(AgentTurnDocument document, SpamModelEvidence evidence)
    {
        document.StartElement(MessageElementName);

        if (evidence.IsSenderTrusted)
        {
            document.WriteAttribute(SenderTrustedAttributeName, TrueValue);
        }

        if (evidence.SenderAddress is { } senderAddress)
        {
            document.StartElement(SenderElementName);
            document.WriteAttribute(AddressAttributeName, senderAddress);

            if (evidence.SenderDisplayName is { Length: > 0 } displayName)
            {
                document.WriteQuoted(DisplayNameElementName, displayName);
            }

            document.EndElement();
        }

        if (evidence.DifferingReplyToAddress is { } replyTo)
        {
            document.StartElement(ReplyToElementName);
            document.WriteAttribute(AddressAttributeName, replyTo);
            document.EndElement();
        }

        foreach (var outcome in evidence.SenderAuthentication)
        {
            document.StartElement(AuthenticationElementName);
            document.WriteAttribute(MethodAttributeName, outcome.Method);
            document.WriteAttribute(ResultAttributeName, outcome.Result);

            if (outcome.IsForwarded)
            {
                document.WriteAttribute(ForwardedAttributeName, TrueValue);
            }

            document.EndElement();
        }

        if (evidence.Subject is { } subject)
        {
            document.WriteQuoted(SubjectElementName, subject);
        }

        foreach (var link in evidence.Links)
        {
            document.StartElement(LinkElementName);
            document.WriteAttribute(HostAttributeName, link.TargetHost);

            if (link.DisplayedHost is { } displayedHost)
            {
                document.WriteAttribute(DisplayedHostAttributeName, displayedHost);
            }

            if (link.DisplayedHostDiffers)
            {
                document.WriteAttribute(DisplayedHostDiffersAttributeName, TrueValue);
            }

            document.EndElement();
        }

        foreach (var attachment in evidence.Attachments)
        {
            document.StartElement(AttachmentElementName);
            document.WriteAttribute(TypeAttributeName, attachment.MediaType);

            if (attachment.FileName is { } fileName)
            {
                document.WriteAttribute(NameAttributeName, fileName);
            }

            document.EndElement();
        }

        document.WriteQuoted(BodyElementName, evidence.Body, evidence.IsBodyCut);
        document.EndElement();
    }

    /// <summary>Returns the evidence with every value but the two addresses replaced by what the guard let through.</summary>
    private static async Task<SpamModelEvidence> GuardedAsync(
        SpamModelEvidence evidence,
        SensitiveContentEgressGuard egressGuard,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(egressGuard);

        var point = SensitiveContentEgressPoint.SpamClassificationInput;

        var displayName = await egressGuard.GuardOptionalAsync(point, evidence.SenderDisplayName, cancellationToken);
        var subject = await egressGuard.GuardOptionalAsync(point, evidence.Subject, cancellationToken);
        var body = await egressGuard.GuardAsync(point, evidence.Body, cancellationToken);
        var hosts = await egressGuard.GuardAllAsync(
            point,
            [.. evidence.Links.SelectMany(static link => (string[])[link.TargetHost, link.DisplayedHost ?? string.Empty])],
            cancellationToken);
        var attachments = await egressGuard.GuardAllAsync(
            point,
            [.. evidence.Attachments.SelectMany(static attachment => (string[])[attachment.MediaType, attachment.FileName ?? string.Empty])],
            cancellationToken);

        return evidence with
        {
            SenderDisplayName = displayName,
            Subject = subject,
            Body = body,
            Links =
            [
                .. evidence.Links.Select((link, position) => link with
                {
                    TargetHost = hosts[position * 2],
                    DisplayedHost = link.DisplayedHost is null ? null : hosts[(position * 2) + 1],
                }),
            ],
            Attachments =
            [
                .. evidence.Attachments.Select((attachment, position) => attachment with
                {
                    MediaType = attachments[position * 2],
                    FileName = attachment.FileName is null ? null : attachments[(position * 2) + 1],
                }),
            ],
        };
    }
}
