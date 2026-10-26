// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Host.Configuration.Decisions;

/// <summary>Holds what one decision function declares under <c>Decisions:&lt;Function&gt;</c>: today, which model it runs on.</summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class DecisionFunctionOptions
{
    /// <summary>Gets or sets which declared model this function runs on; left empty, it runs on <c>Decisions:DefaultModel</c>.</summary>
    public DecisionModelReferenceOptions Model { get; set; } = new();
}
