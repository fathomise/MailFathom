// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.Retention;

/// <summary>States how long one account keeps the rows it holds as tombstones.</summary>
/// <param name="IsEnabled">Whether the account's tombstones are erased at all.</param>
/// <param name="After">How long a row stays a tombstone before it is erased.</param>
/// <param name="ExcludedFolders">The folders left alone, each a folder alias or a local folder identifier.</param>
public sealed record MailTombstoneRetention(bool IsEnabled, TimeSpan After, IReadOnlyList<string> ExcludedFolders)
{
    /// <summary>Gets the answer for an account no retention applies to, which erases nothing.</summary>
    public static MailTombstoneRetention Disabled { get; } = new(IsEnabled: false, TimeSpan.Zero, []);
}
