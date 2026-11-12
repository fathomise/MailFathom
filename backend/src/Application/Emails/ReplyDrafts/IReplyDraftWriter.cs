// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Detection;

namespace MailFathom.Application.Emails.ReplyDrafts;

/// <summary>Writes a reply out of the correspondence it answers and the way its author writes, or rewrites passages of one.</summary>
/// <remarks>
/// <para>
/// The port a composer reaches the model through, and the whole of what a provider decides about a reply. It answers
/// with text and with what backs it, so nothing provider-shaped travels beyond this boundary and the message that may
/// eventually be sent is composed by the same authoring a message somebody typed is composed by.
/// </para>
/// <para>
/// Rewriting passages is the same port's second act rather than a port of its own, because it is the same capability
/// reached from the same composer: one registration decides whether a deployment does either, and the same allowance,
/// ledgers, and failure contract govern both.
/// </para>
/// <para>
/// It is registered only where the deployment declared a chat endpoint and turned drafting on, so a caller resolves it
/// optionally and a deployment without one serves a composer somebody writes in themselves rather than a button that
/// fails. That is a registration rather than a branch inside a call: there is no path by which a correspondence leaves
/// an instance whose operator did not ask for this.
/// </para>
/// </remarks>
public interface IReplyDraftWriter
{
    /// <summary>Writes one reply, or answers with nothing where none could be written.</summary>
    /// <param name="brief">The correspondence, the people in it, the style, and what the person asked for.</param>
    /// <param name="cancellationToken">Cancels the drafting.</param>
    /// <returns>The draft, or <see cref="ReplyDraft.Nothing" /> where the provider failed or answered unreadably.</returns>
    /// <remarks>
    /// A provider that failed is not published as a failure: what a person has for that case is the composer they were
    /// already looking at. A refusal travels only where this deployment's own spend ceiling declined the call, because
    /// falling back there would leave somebody pressing a button the operator has already paid the last of the
    /// allowance for.
    /// </remarks>
    /// <exception cref="MailAnsweringBudgetExhaustedException">Thrown when this deployment has spent what it allows a provider for the period, which is the one refusal a caller publishes rather than answering with nothing.</exception>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what the correspondence carries, which withholds the drafting rather than sending text nothing scanned.</exception>
    Task<ReplyDraft> WriteAsync(ReplyDraftBrief brief, CancellationToken cancellationToken);

    /// <summary>Rewrites the passages of a draft as the person asked, or answers with nothing where they could not be rewritten.</summary>
    /// <param name="request">The draft, the passages of it to rewrite, and how they should change.</param>
    /// <param name="cancellationToken">Cancels the rewriting.</param>
    /// <returns>One rewritten passage for each passage asked about, or <see cref="PassageRewrite.Nothing" /> where the provider failed or answered unreadably.</returns>
    /// <remarks>The failure contract is <see cref="WriteAsync" />'s, for the same reasons: what a person has when a provider fails is the draft they were already looking at.</remarks>
    /// <exception cref="MailAnsweringBudgetExhaustedException">Thrown when this deployment has spent what it allows a provider for the period, which is the one refusal a caller publishes rather than answering with nothing.</exception>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what the draft carries, which withholds the rewriting rather than sending text nothing scanned.</exception>
    Task<PassageRewrite> RewritePassagesAsync(PassageRewriteRequest request, CancellationToken cancellationToken);
}
