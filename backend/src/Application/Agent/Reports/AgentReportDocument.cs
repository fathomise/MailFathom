// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Agent.Reports;

/// <summary>A report set as a PDF.</summary>
/// <param name="Content">The octets of the PDF.</param>
/// <param name="PageCount">How many pages it was set on.</param>
public sealed record AgentReportDocument(ReadOnlyMemory<byte> Content, int PageCount);
