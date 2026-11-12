// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Agent.Reports;

/// <summary>One entry of the list of sources a report closes with.</summary>
/// <param name="Kind">Whether it is a message or a file attached to the question.</param>
/// <param name="Title">A message's subject, or a file's name.</param>
/// <param name="Author">Who sent a message, by name or else by address; <see langword="null" /> where the message names nobody, and for a file, which the person attached.</param>
/// <param name="At">When a message was sent or a file was attached, on the person's own clock; <see langword="null" /> where the message says nothing.</param>
public sealed record AgentReportSource(
    AgentReportSourceKind Kind,
    string Title,
    string? Author,
    DateTimeOffset? At);
