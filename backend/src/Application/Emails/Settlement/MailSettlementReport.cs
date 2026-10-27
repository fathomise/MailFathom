// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Emails.Settlement;

/// <summary>What one settlement pass did, in counts alone.</summary>
/// <param name="SettledCount">The messages this pass settled.</param>
/// <param name="AnnouncedCount">How many of them the arrival notification was raised for.</param>
/// <param name="StillSettlingCount">The messages the pass read and left settling.</param>
public sealed record MailSettlementReport(int SettledCount, int AnnouncedCount, int StillSettlingCount);
