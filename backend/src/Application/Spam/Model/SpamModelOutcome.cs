// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Spam.Model;

/// <summary>How one consultation of the model stage ended.</summary>
/// <remarks>
/// Every member but <see cref="Answered" /> leaves the deterministic result where it was, with no model stage and no
/// model signal in the record. They are kept apart because an operator reading the count needs to know which one to
/// act on: a missing model is configuration, an exhausted budget is a ceiling, an unreachable provider is an outage, and
/// an unreadable answer is a contract that moved.
/// </remarks>
public enum SpamModelOutcome
{
    /// <summary>The model answered, and the answer was read.</summary>
    Answered = 0,

    /// <summary>No decision model is declared for spam classification, so nothing was asked.</summary>
    NotActivated = 1,

    /// <summary>The deployment's period allowance or the call's own ceiling refused the call before it was made.</summary>
    BudgetExhausted = 2,

    /// <summary>The provider could not be reached, timed out, refused the credential, or refused the request.</summary>
    ProviderUnavailable = 3,

    /// <summary>The provider answered with something outside the closed set the question offered.</summary>
    AnswerUnreadable = 4,

    /// <summary>The request would carry more than the decision model is declared to accept, so it was not sent.</summary>
    InputTooLarge = 5,

    /// <summary>A sensitive-content scanner the account's posture switched on could not read the input, so nothing was sent.</summary>
    ScannerUnavailable = 6,
}
