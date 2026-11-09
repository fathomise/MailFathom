// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.SpamClassification;
using MailFathom.Application.Categories;
using MailFathom.Application.SensitiveContent.Egress;

namespace MailFathom.AI.EmailCategorization;

/// <summary>Writes one message's evidence as the input a decision model reads, as the spam model stage writes it, with a reply's conversation opening beside it.</summary>
internal static class EmailCategoryTurn
{
    internal const string ConversationOpeningElementName = "conversation-opening";

    /// <summary>Guards every value the account's posture reads, and writes the input from what the guard returned, cut so the request fits the model's bound.</summary>
    /// <param name="evidence">The evidence, as the pass read it.</param>
    /// <param name="egressGuard">Withholds from the provider whatever the account's posture withholds.</param>
    /// <param name="allowance">The most characters the input may take in the request body.</param>
    /// <param name="cancellationToken">Withdraws the scan.</param>
    /// <returns>The input text.</returns>
    /// <exception cref="Application.SensitiveContent.Detection.SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not read a value.</exception>
    internal static async Task<string> ComposeAsync(
        EmailCategoryEvidence evidence,
        SensitiveContentEgressGuard egressGuard,
        int allowance,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(egressGuard);

        const SensitiveContentEgressPoint Point = SensitiveContentEgressPoint.EmailCategorizationInput;

        var message = await SpamClassificationTurn.GuardedAsync(evidence.Message, egressGuard, Point, cancellationToken);
        var opening = evidence.ConversationOpening is { } written
            ? written with { Text = await egressGuard.GuardAsync(Point, written.Text, cancellationToken) }
            : null;

        return SpamClassificationTurn.Compose(
            message,
            allowance,
            document =>
            {
                if (opening is not null)
                {
                    document.WriteQuoted(ConversationOpeningElementName, opening.Text, opening.IsCut);
                }
            });
    }
}
