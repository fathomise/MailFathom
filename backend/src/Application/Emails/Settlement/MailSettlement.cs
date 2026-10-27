// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Emails.Settlement;

/// <summary>States whether an arriving message settles now, and whether every stage before it completed.</summary>
/// <remarks>
/// The three releases are distinct from <see cref="Settled" /> because each is a message shown before something that
/// decides its placement finished, and an operator has to be able to tell a deployment whose classification or rules are
/// not keeping up from one whose mail simply settled.
/// </remarks>
public enum MailSettlement
{
    /// <summary>Something that decides where the message belongs has not finished, and the wait has not run out.</summary>
    Settling = 0,

    /// <summary>Its verdict, its rules, and every move they asked for are done.</summary>
    Settled = 1,

    /// <summary>No verdict will ever be reached, because the message carries nothing a classification could read.</summary>
    ReleasedAsUnclassifiable = 2,

    /// <summary>No verdict was reached in the wait a verdict is allowed, and everything after the verdict is done.</summary>
    ReleasedWithoutVerdict = 3,

    /// <summary>Something it waited on did not finish within the longest a message is held, so it is shown as it is.</summary>
    ReleasedAfterWaiting = 4,
}
