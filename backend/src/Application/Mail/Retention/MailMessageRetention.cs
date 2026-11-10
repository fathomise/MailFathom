// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.Retention;

/// <summary>States how long one account keeps the mail a person sees.</summary>
/// <param name="IsEnabled">Whether the account's mail is retired at all.</param>
/// <param name="After">How old a message may become before it is retired.</param>
/// <param name="ExcludedFolders">The folders left alone, each a folder alias or a local folder identifier.</param>
public sealed record MailMessageRetention(bool IsEnabled, TimeSpan After, IReadOnlyList<string> ExcludedFolders)
{
    /// <summary>Gets the answer for an account no retention applies to, which retires nothing.</summary>
    public static MailMessageRetention Disabled { get; } = new(IsEnabled: false, TimeSpan.Zero, []);
}
