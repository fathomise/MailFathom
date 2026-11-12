// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Emails.Census;

/// <summary>One group of a census and how many messages it counts.</summary>
/// <param name="Key">
/// The sender's normalized address, its domain, the category's name, or the first day of the period written as
/// <c>yyyy-MM-dd</c> — <c>yyyy-MM</c> for a month — or <see langword="null" /> for the mail that carries no sender
/// address or no arrival date.
/// </param>
/// <param name="Count">How many messages fall into the group.</param>
/// <remarks>
/// A sender, a domain, or a category leads by how many messages it counts, the largest first; a period leads by how
/// recent it is, the latest first, because a question about periods reads them as a timeline rather than as a ranking.
/// A sender's address and its domain are what somebody else wrote, and whoever hands one to a model guards it first.
/// </remarks>
public sealed record MailCensusGroup(string? Key, int Count);
