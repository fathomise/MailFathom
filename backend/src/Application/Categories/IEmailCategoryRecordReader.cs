// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Categories;

/// <summary>Reads which categories were recorded on stored messages.</summary>
/// <remarks>It answers with every record, a default the account has since hidden included: <see cref="OfferedMailCategories" /> is what withholds one.</remarks>
public interface IEmailCategoryRecordReader
{
    /// <summary>Reads the categories recorded on each message.</summary>
    /// <param name="emails">The messages to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The recorded names of each message, in ordinal order; a message with none is absent.</returns>
    Task<IReadOnlyDictionary<StoredEmailId, IReadOnlyList<string>>> ReadAsync(
        IReadOnlyCollection<StoredEmailId> emails,
        CancellationToken cancellationToken);
}
