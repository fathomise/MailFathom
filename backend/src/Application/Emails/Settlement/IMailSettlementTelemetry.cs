// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Emails.Settlement;

/// <summary>Reports how arriving messages settled, in counts alone.</summary>
/// <remarks>
/// A release is a message shown before what decides its placement finished, and without a count a deployment whose
/// classification or rules have stalled looks exactly like one whose mail settled. Nothing recorded here is mail or
/// derived from it: the one tag is a settlement, which is MailFathom's own closed set.
/// </remarks>
public interface IMailSettlementTelemetry
{
    /// <summary>Records one message this replica settled.</summary>
    /// <param name="settlement">How it settled; never <see cref="MailSettlement.Settling" />.</param>
    void RecordSettled(MailSettlement settlement);
}
