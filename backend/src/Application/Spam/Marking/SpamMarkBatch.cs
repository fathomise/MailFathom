// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Spam.Actions;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Spam.Marking;

/// <summary>A batch of marks whose grants were decided, with the switches each account it reaches had at that moment.</summary>
/// <remarks>
/// The switch that decides whether a mark moves mail is reloadable, so the grant it requires and the move it makes are
/// read from this one snapshot rather than from the switch again per message: a switch turned on partway through a batch
/// can neither refuse a message after earlier ones committed nor move mail the grant was not asked for. Only
/// <see cref="SpamMarkRecorder.RequireGrantsAsync" /> makes one.
/// </remarks>
public sealed class SpamMarkBatch
{
    private readonly IReadOnlyDictionary<MailAccountId, SpamActionSettings> settingsByAccount;

    internal SpamMarkBatch(IReadOnlyDictionary<MailAccountId, SpamActionSettings> settingsByAccount) =>
        this.settingsByAccount = settingsByAccount;

    /// <summary>Gets the switches an account had when the batch was admitted, or nothing for an account no message of it reached then.</summary>
    /// <param name="account">The account a message is on.</param>
    /// <returns>The switches, or <see langword="null" />.</returns>
    internal SpamActionSettings? SettingsFor(MailAccountId account) =>
        this.settingsByAccount.GetValueOrDefault(account);
}
