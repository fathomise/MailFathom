// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>One phone number a user's record states, as it was written and before any rule judged it.</summary>
/// <remarks>
/// The same shape and the same rules as a contact's phone number, because the member directory shows it on the
/// member's contact: <see cref="UserAccountOptions.FindRefusals" /> judges a list of these through the rule a contact's
/// numbers are judged by, so a number refused there is refused here in the same words.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The configuration binder materializes this type when a user's document is read.")]
internal sealed class UserPhoneNumberOptions
{
    /// <summary>Gets or sets the number as the user or an administrator wrote it.</summary>
    public string? Number { get; set; }

    /// <summary>Gets or sets which of the user's phones it reaches: <c>Mobile</c>, <c>Work</c>, <c>Home</c>, or <c>Other</c>.</summary>
    public string? Label { get; set; }
}
