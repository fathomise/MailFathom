// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Emails.Settlement;

/// <summary>One follow-up that failed after a batch had settled, named by what failed and why, never by mail.</summary>
/// <param name="FollowUp">Which follow-up failed.</param>
/// <param name="ErrorCode">The error code a failure of MailFathom's own carried, or the type name of any other failure.</param>
public sealed record MailSettlementFollowUpFailure(MailSettlementFollowUp FollowUp, string ErrorCode);
