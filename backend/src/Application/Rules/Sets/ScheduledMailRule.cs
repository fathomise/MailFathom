// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Jobs.Scheduling;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Rules.Sets;

/// <summary>An enabled rule that walks its account's mailbox on a schedule of its own.</summary>
/// <param name="Account">The account the rule belongs to.</param>
/// <param name="Rule">The rule's identifier, which the schedule's identity is composed from.</param>
/// <param name="Schedule">The occasions the walk happens on.</param>
public sealed record ScheduledMailRule(MailAccountId Account, MailRuleId Rule, JobRecurrence Schedule);
