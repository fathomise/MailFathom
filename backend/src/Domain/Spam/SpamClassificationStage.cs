// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Spam;

/// <summary>Names which stage of classification reached a verdict.</summary>
/// <remarks>
/// The deterministic stage reads what the receiving server already concluded and needs no network and no model. <c>1</c>
/// is absent because the stage that held it is gone, and the number is not reissued.
/// </remarks>
public enum SpamClassificationStage
{
    /// <summary>What the message itself carried: sender authentication, a provider's own spam headers, and where the mailbox filed it.</summary>
    Deterministic = 0,

    /// <summary>A decision model's judgement, on mail the deterministic stage left <see cref="SpamVerdict.NotSpam" /> or <see cref="SpamVerdict.Undetermined" />.</summary>
    /// <remarks>
    /// The model names a subcategory of deterministic spam too, but that verdict stays the deterministic stage's: this
    /// value says the model decided whether the message is spam at all, and nothing less.
    /// </remarks>
    Model = 2,

    /// <summary>The person's own verdict, which outranks every other stage and is replaced only by the person.</summary>
    /// <remarks>
    /// Its <see cref="SpamVerdict.NotSpam" /> is a statement the person made rather than a check that found nothing, and
    /// no later stage, profile change, or reclassification replaces it.
    /// </remarks>
    Person = 3,
}
