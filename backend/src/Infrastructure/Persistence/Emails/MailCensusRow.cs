// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>One leading group of a census statement, beside the figures every row of that statement shares.</summary>
/// <param name="Text">The sender address or domain the group is keyed by, or <see langword="null" /> for a period or for mail carrying none.</param>
/// <param name="Period">The first day of the period the group is keyed by, on the person's clock, or <see langword="null" /> for a text key or for undated mail.</param>
/// <param name="Count">How many messages the group counts.</param>
/// <param name="GroupCount">How many groups the statement found in all.</param>
/// <param name="CountedInGroups">How many messages every group counts between them.</param>
/// <param name="Total">How many messages the selection admits.</param>
internal sealed record MailCensusRow(
    string? Text,
    DateTime? Period,
    int Count,
    int GroupCount,
    int CountedInGroups,
    int Total);
