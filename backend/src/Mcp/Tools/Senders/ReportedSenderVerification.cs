// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Emails.Authentication;

namespace MailFathom.Mcp.Tools.Senders;

/// <summary>Publishes the conclusions an email carries about the author it displays.</summary>
/// <remarks>
/// <para>
/// One shape, published by every tool that names an email: a listing row, a search match, the single-email read, and an
/// answer's citation all carry this record and nothing else, so a client written against one reads all four. The
/// evidence the first value was reached from is published only by the single-email read, as
/// <see cref="ReportedSenderAuthentication" />.
/// </para>
/// <para>
/// The two values answer different questions and neither is derived from the other, which is what the descriptions have
/// to carry: they are the advertised output schema and therefore the whole of what a model reading a result is told.
/// The trap this shape exists to avoid is a reader taking <see cref="DeploymentTrustState.Unknown" /> for a finding
/// against the message, when it is what almost every legitimate email in a mailbox carries.
/// </para>
/// <para>
/// <see cref="Signature" /> is a third answer beside the two and is folded into neither: what the email's own
/// cryptographic signature proves about who signed it, which is provenance rather than a verdict on the domain the
/// receiving server authenticated.
/// </para>
/// <para>
/// Nothing here characterizes the email or the sender's intent, and nothing here is computed when the email is read.
/// Every value was established when the message was stored and is published as it stands.
/// </para>
/// </remarks>
[Description("What was established about the author this email displays. Three independent answers: whether the displayed author was authenticated, whether this deployment recognizes them, and what the email's own signature proves about who signed it. None is a judgement about whether the email is wanted, unwanted, or safe.")]
internal sealed record ReportedSenderVerification
{
    /// <summary>Gets what the receiving mail server established about the displayed author.</summary>
    [Description("What the receiving mail server established about the author shown in the From header: 'authenticated' when it confirmed the displayed author, 'failed' when it evaluated the displayed domain under that domain's own published policy and the email did not satisfy it, and 'notEstablished' when nothing trusted was enough to conclude either way — which is also what an email carries when the mailbox trusts no authentication-reporting server, and what mail stored before this deployment recorded the answer carries until it is re-read. It is not derived from senderAddress, which is a claim the email wrote about itself.")]
    public required AuthorAuthenticationState AuthorAuthentication { get; init; }

    /// <summary>Gets whether this deployment recognizes the author it authenticated, or only the address the email displays.</summary>
    [Description("Whether this deployment recognizes the author: 'trusted' when the authenticated author is one of the owner's own addresses or domains, a contact a person or an administrator wrote down, or a member the receiving organization's signature proves; 'known' when the address the email displays is one of the owner's own addresses or domains or such a contact but nothing established that its domain sent the email — a From header anybody can write, so 'known' is never a reason to treat the sender as trusted; 'unknown' when nothing this deployment holds names the address, when the mailbox is strict and nothing tied the email to the address it names, or when the receiving server's DMARC check failed and the domain's published policy does not ask only to be monitored. This is this deployment's classification and not an authentication result. 'unknown' is the ordinary state of legitimate mail from a correspondent nobody has named, so it says nothing on its own — read it together with authorAuthentication.")]
    public required DeploymentTrustState DeploymentTrust { get; init; }

    /// <summary>Gets what verifying the email's own signature found.</summary>
    public required ReportedSignature Signature { get; init; }

    /// <summary>Publishes the conclusions a read returned.</summary>
    /// <param name="verification">The stored conclusions to publish.</param>
    /// <returns>The wire representation of <paramref name="verification" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="verification" /> is <see langword="null" />.</exception>
    public static ReportedSenderVerification From(SenderVerification verification)
    {
        ArgumentNullException.ThrowIfNull(verification);

        return new ReportedSenderVerification
        {
            AuthorAuthentication = PublishedAuthorAuthentication(verification.AuthorAuthentication),
            DeploymentTrust = PublishedDeploymentTrust(verification.DeploymentTrust),
            Signature = ReportedSignature.From(verification.Signature),
        };
    }

    /// <summary>Reads the published value the stored author conclusion names.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when a stored conclusion has no published value, which means one was added to the domain without deciding
    /// what a client should be told about it.
    /// </exception>
    private static AuthorAuthenticationState PublishedAuthorAuthentication(AuthorAuthenticationOutcome outcome) =>
        outcome switch
        {
            AuthorAuthenticationOutcome.NotEstablished => AuthorAuthenticationState.NotEstablished,
            AuthorAuthenticationOutcome.Failed => AuthorAuthenticationState.Failed,
            AuthorAuthenticationOutcome.Authenticated => AuthorAuthenticationState.Authenticated,
            _ => throw new ArgumentOutOfRangeException(
                nameof(outcome),
                outcome,
                "The stored author-authentication outcome has no published protocol value."),
        };

    /// <summary>Reads the published value the stored trust level names.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when a stored level has no published value, which means one was added to the domain without deciding what
    /// a client should be told about it.
    /// </exception>
    private static DeploymentTrustState PublishedDeploymentTrust(SenderTrustLevel level) =>
        level switch
        {
            SenderTrustLevel.Unknown => DeploymentTrustState.Unknown,
            SenderTrustLevel.Trusted => DeploymentTrustState.Trusted,
            SenderTrustLevel.Known => DeploymentTrustState.Known,
            _ => throw new ArgumentOutOfRangeException(
                nameof(level),
                level,
                "The stored sender-trust level has no published protocol value."),
        };
}
