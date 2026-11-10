// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.Retention;

/// <summary>Publishes what a night of retention did, as counts and durations and never as anything about the mail.</summary>
public interface IMailRetentionTelemetry
{
    /// <summary>Records one account's run of the retention steps.</summary>
    /// <param name="erasedTombstones">How many tombstones the run erased.</param>
    /// <param name="retiredMessages">How many visible messages the run retired.</param>
    /// <param name="removedConversations">How many emptied conversations the run removed.</param>
    /// <param name="duration">How long the run took.</param>
    void RecordAccountRun(int erasedTombstones, int retiredMessages, int removedConversations, TimeSpan duration);
}
