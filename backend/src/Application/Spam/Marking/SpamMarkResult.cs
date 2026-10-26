// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Spam.Marking;

/// <summary>What marking one message spam or not spam produced.</summary>
/// <param name="StoredEmailId">The message the answer is about.</param>
/// <param name="Outcome">What became of the mark.</param>
/// <param name="Records">The durable records its keyword changes and its move were written down as, empty where none was opened.</param>
/// <param name="IsMoved">Whether the mark moved the message, which the account's switch for filing junk decides.</param>
public sealed record SpamMarkResult(
    StoredEmailId StoredEmailId,
    SpamMarkOutcome Outcome,
    IReadOnlyList<MailboxMutationRecord> Records,
    bool IsMoved)
{
    /// <summary>Describes a message the mark was not recorded for, and why.</summary>
    /// <param name="storedEmailId">The message.</param>
    /// <param name="outcome">Why nothing was recorded.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="outcome" /> is one that records something.</exception>
    public static SpamMarkResult NotRecorded(StoredEmailId storedEmailId, SpamMarkOutcome outcome) =>
        outcome is SpamMarkOutcome.Recorded or SpamMarkOutcome.Applied
            ? throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A mark that was recorded carries what it wrote.")
            : new SpamMarkResult(storedEmailId, outcome, [], IsMoved: false);
}

/// <summary>What became of marking one message spam or not spam.</summary>
public enum SpamMarkOutcome
{
    /// <summary>The verdict was recorded and at least one change was written down for the account's convergence pass to carry to the mail server.</summary>
    Recorded = 0,

    /// <summary>The verdict was recorded and everything the mark asked took effect at once, leaving no record to follow but one a restoring account's source is owed.</summary>
    Applied = 1,

    /// <summary>This deployment serves no readable message under that identity, so nothing was recorded.</summary>
    MessageNotFound = 2,

    /// <summary>The request identity already asked this message for other keywords, so nothing was recorded.</summary>
    RequestIdAlreadyAskedForAnother = 3,
}
