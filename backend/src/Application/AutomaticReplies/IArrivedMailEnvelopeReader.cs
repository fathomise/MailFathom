// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.AutomaticReplies;

/// <summary>Reads the header block of stored raw MIME for what an automatic reply needs to know about it.</summary>
/// <remarks>Only the header block is parsed, and nothing is reached over a network: the bytes are already stored.</remarks>
public interface IArrivedMailEnvelopeReader
{
    /// <summary>Reads one message's envelope.</summary>
    /// <param name="rawMime">The stored raw MIME.</param>
    /// <returns>The envelope, or <see langword="null" /> where the header block does not parse.</returns>
    ArrivedMailEnvelope? Read(ReadOnlyMemory<byte> rawMime);
}
