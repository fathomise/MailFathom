// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Spam;

/// <summary>Names which stage of classification reached a verdict.</summary>
/// <remarks>
/// The deterministic stage reads what the receiving server already concluded and needs no network and no model. <c>1</c>
/// is absent because the stage that held it is gone, and the number is not reissued, so a stage added later takes
/// <c>2</c>.
/// </remarks>
public enum SpamClassificationStage
{
    /// <summary>What the message itself carried: sender authentication, a provider's own spam headers, and where the mailbox filed it.</summary>
    Deterministic = 0,
}
