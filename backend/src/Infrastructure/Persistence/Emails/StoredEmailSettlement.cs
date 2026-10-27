// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Linq.Expressions;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>States which stored rows a reader may see while arrivals are still settling, once for every read.</summary>
/// <remarks>
/// A message is readable from the moment its spam verdict and its moving rules have decided where it belongs rather
/// than from the moment synchronization committed it, so a person never watches it land in one folder and leave for
/// another. Every query that narrows to mail a person or a tool may read composes this expression; the passes that
/// decide where a message belongs, and the governance that must reach every row whatever a reader sees, do not.
/// </remarks>
internal static class StoredEmailSettlement
{
    /// <summary>Admits the rows whose placement has settled.</summary>
    internal static Expression<Func<StoredEmailEntity, bool>> IsSettled { get; } = email => email.SettledAt != null;
}
