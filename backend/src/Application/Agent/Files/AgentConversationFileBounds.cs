// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Agent.Files;

/// <summary>What a conversation may be handed in files, and how much of one a turn is given whole.</summary>
/// <remarks>
/// <para>
/// A conversation's files are a ceiling of their own rather than a share of the stored-content ceilings: those bound what
/// synchronization brings in from a mail server, and a file is something one person chose to hand over. Twenty files of
/// sixteen mebibytes is the most one conversation can hold, and a person holds at most
/// <see cref="AgentConversationBounds.MaximumConversations" /> conversations, which is the whole of what
/// they can put into the deployment's content store this way.
/// </para>
/// <para>
/// Constants rather than settings, for the reason a conversation's own ceilings are: they describe what the product does
/// with a conversation, and a deployment that wants less of it turns the Agent off.
/// </para>
/// </remarks>
public static class AgentConversationFileBounds
{
    /// <summary>The most octets one file may hold.</summary>
    /// <remarks>The attachment text reader's own default input ceiling, so a document taken in is one it would read.</remarks>
    public const int MaximumFileOctets = (int)AttachmentTextExtractionOptions.DefaultMaxInputOctets;

    /// <summary>The most files one question may carry.</summary>
    public const int MaximumFilesPerQuestion = 10;

    /// <summary>The most files one conversation may hold.</summary>
    public const int MaximumFilesPerConversation = 20;

    /// <summary>The most characters of a document a turn is given whole; a longer one is read a page at a time and searched.</summary>
    /// <remarks>Two of the pages a reading tool shows, which is short enough that a question carrying several still leaves the run its ceiling for mail.</remarks>
    public const int MaximumWholeTextCharacters = 8_000;

    /// <summary>The longest name a file is recorded under, which is the person's own name for it and decides nothing.</summary>
    /// <remarks>The bound a mail attachment's name is normalized to, because the name is normalized the same way.</remarks>
    public const int MaximumNameLength = AttachmentFileName.MaxLength;
}
