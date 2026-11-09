// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Categories;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.TestSupport;

/// <summary>Answers what accounts offer and what messages hold from what a test wrote.</summary>
/// <param name="offers">What each account offers; every other account offers nothing.</param>
/// <param name="records">What each message has recorded; every other message has nothing.</param>
internal sealed class StubMailCategories(
    IReadOnlyDictionary<MailAccountId, MailCategoryOffer> offers,
    IReadOnlyDictionary<StoredEmailId, IReadOnlyList<string>> records) : IMailCategoryOfferReader, IEmailCategoryRecordReader
{
    /// <summary>Initializes a double of a deployment where no account categorizes.</summary>
    public StubMailCategories()
        : this(new Dictionary<MailAccountId, MailCategoryOffer>(), new Dictionary<StoredEmailId, IReadOnlyList<string>>())
    {
    }

    /// <summary>Gets the collaborator of a use case that is not about categories.</summary>
    public static OfferedMailCategories OfferingNothing { get; } = Offering(new StubMailCategories());

    /// <summary>Builds the collaborator a use case takes over one double.</summary>
    /// <param name="categories">The double answering both ports.</param>
    /// <returns>The collaborator.</returns>
    public static OfferedMailCategories Offering(StubMailCategories categories) => new(categories, categories);

    /// <inheritdoc />
    public MailCategoryOffer OfferOf(MailAccountId account) => offers.GetValueOrDefault(account, MailCategoryOffer.None);

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<StoredEmailId, IReadOnlyList<string>>> ReadAsync(
        IReadOnlyCollection<StoredEmailId> emails,
        CancellationToken cancellationToken) => Task.FromResult(records);
}
