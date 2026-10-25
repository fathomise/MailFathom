// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Spam;

/// <summary>Names where a signal was read from, which is the half of its provenance that is not the value itself.</summary>
/// <remarks>
/// A signal without this is a claim with no way to check it. Knowing that a DMARC failure came out of the message's own
/// <c>Authentication-Results</c> header rather than out of where the mailbox filed it is what lets an operator decide
/// whether to believe it. <c>2</c> is absent because the source that held it went with the stage that read it, and the
/// number is not reissued.
/// </remarks>
public enum SpamSignalSource
{
    /// <summary>A header the message carried when synchronization stored it.</summary>
    MessageHeader = 0,

    /// <summary>The folder of the account the occurrence was stored from.</summary>
    FolderPlacement = 1,
}
