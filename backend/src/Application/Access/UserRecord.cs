// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access;

/// <summary>The relational envelope of one user this deployment holds a record for.</summary>
/// <param name="User">The identity every mail account and every stored message of theirs hangs on.</param>
/// <param name="DisplayName">The label an operator reads this user by, which another user may carry as well.</param>
/// <remarks>
/// The document itself is deliberately absent. Who this deployment holds is decided from the envelope alone, so
/// listing the users it holds never materializes one person's record, let alone everybody's.
/// </remarks>
public sealed record UserRecord(UserId User, string DisplayName)
{
    /// <summary>The longest label a user is told apart by.</summary>
    /// <remarks>
    /// Stated here rather than on the row it bounds because two things have to agree about it and neither owns the
    /// other: the column that stores the label, and the rule that judges a declaration carrying one. A declaration
    /// refused for a label the column would have truncated is a refusal an operator can act on; the same declaration
    /// accepted and then refused by PostgreSQL is a start that fails with the server's own sentence.
    /// </remarks>
    public const int MaximumDisplayNameLength = 128;

    /// <summary>The longest title a user is described by in their organization's member directory.</summary>
    /// <remarks>Stated here for the reason <see cref="MaximumDisplayNameLength" /> is: the column and the rule that judges a title an administrator writes have to agree about it.</remarks>
    public const int MaximumJobTitleLength = 256;

    /// <summary>Gets which endpoints this user may be served on, which is part of the envelope rather than of the document.</summary>
    /// <remarks>Both until an administrator says otherwise, which is what a user is recorded with.</remarks>
    public UserEndpointAccess EndpointAccess { get; init; } = UserEndpointAccess.Everywhere;

    /// <summary>Gets whether the user is a person or a technical account, which is a person until an administrator says otherwise.</summary>
    public UserType Type { get; init; } = UserType.User;

    /// <summary>Gets the title the user's colleagues read them by, or <see langword="null" /> where an administrator stated none.</summary>
    public string? JobTitle { get; init; }

    /// <summary>Gets the member of the same organization the user reports to, or <see langword="null" /> where an administrator named nobody.</summary>
    public UserId? Manager { get; init; }
}
