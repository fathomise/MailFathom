// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Categories;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>That one stored message holds one category, and what said so.</summary>
/// <remarks>
/// <para>
/// A row per message and category rather than columns on the message, because an account's own categories are a set
/// that grows and overlaps, and because a category is derived personal data that goes with the message it describes.
/// </para>
/// <para>
/// <see cref="Category" /> is a default's published identity or an own category's name, which is what the account's
/// record calls it; a name that leaves that record takes its rows with it, so a rename is a removal and an addition.
/// <see cref="Kind" /> is stored beside it so that removal can never reach a default.
/// </para>
/// <para>
/// <see cref="QuestionRevision" /> is its own column rather than part of <see cref="Origin" /> because a run over stored
/// mail selects on it to leave alone what was already asked under the question as it now stands.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmailCategoryEntity
{
    /// <summary>The greatest length a stored category has, which is an own category's name; every published identity is shorter.</summary>
    internal const int MaximumCategoryLength = OwnMailCategoryBounds.MaximumNameLength;

    /// <summary>The greatest length a stored origin has, as enrichment's and a spam signal's do.</summary>
    internal const int MaximumOriginLength = 128;

    /// <summary>The greatest length a stored question revision has.</summary>
    internal const int MaximumQuestionRevisionLength = 64;

    public Guid StoredEmailId { get; set; }

    /// <summary>Gets or sets the email this record is about, which a write leaves unset.</summary>
    /// <remarks>Optional for the reason a classification's own navigation onto its email is: the record is staged by the identifier its caller was given, and the foreign key already refuses a record about a message that is not there.</remarks>
    public StoredEmailEntity? StoredEmail { get; set; }

    public EmailCategoryKind Kind { get; set; }

    public required string Category { get; set; }

    public double Probability { get; set; }

    public EmailCategorySource Source { get; set; }

    /// <summary>Gets or sets what within that source said so: the model build that answered, or a rule's identity.</summary>
    public required string Origin { get; set; }

    /// <summary>Gets or sets the revision of the question this answers.</summary>
    public required string QuestionRevision { get; set; }
}
