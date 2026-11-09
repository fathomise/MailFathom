// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>Configures one half of an account's retention: whether it runs, and past what age.</summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class MailRetentionPassOptions
{
    /// <summary>Gets or sets whether this half removes anything from this account.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the age past which this half removes what it covers.</summary>
    /// <remarks>
    /// The default and the floor differ between the halves, so <see cref="MailAccountRetentionOptions" /> supplies the
    /// first and judges the second.
    /// </remarks>
    public TimeSpan After { get; set; }
}
