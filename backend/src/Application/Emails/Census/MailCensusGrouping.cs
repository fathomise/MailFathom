// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Emails.Census;

/// <summary>How a census groups the mail it counts, settled by the use case before storage is asked.</summary>
/// <param name="Dimension">What the mail is grouped by.</param>
/// <param name="Zone">The person's time zone, which a day, a week, or a month is read on.</param>
/// <param name="ShownCategories">
/// The categories each account in scope shows, by the names its records spell them, which is the whole of what a census
/// by category may count: a default an account switched off stays recorded and is still not counted.
/// </param>
public sealed record MailCensusGrouping(
    MailCensusDimension Dimension,
    UserTimeZone Zone,
    IReadOnlyDictionary<MailAccountId, IReadOnlyList<string>> ShownCategories);
