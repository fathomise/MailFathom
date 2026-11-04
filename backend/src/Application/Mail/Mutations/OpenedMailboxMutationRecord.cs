// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations;

/// <summary>An overlaying record, and whether the call that asked for it opened it.</summary>
/// <param name="Record">The record.</param>
/// <param name="IsNew"><see langword="false" /> where the same request had already been recorded, so its local commit has already happened.</param>
public sealed record OpenedMailboxMutationRecord(MailboxMutationRecord Record, bool IsNew);
