// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Extraction.Attachments;

namespace MailFathom.Application.Agent.Files;

/// <summary>What a file's octets proved it to be.</summary>
/// <param name="Kind">Whether it is a document or a picture.</param>
/// <param name="MediaType">The media type it is read and sent as.</param>
/// <param name="DocumentFormat">The format the attachment text reader reads it as, for a document.</param>
public sealed record RecognizedConversationFile(
    AgentConversationFileKind Kind,
    string MediaType,
    AttachmentDocumentFormat? DocumentFormat);
