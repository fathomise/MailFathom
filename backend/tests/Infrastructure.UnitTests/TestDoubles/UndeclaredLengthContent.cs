// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Net;

namespace MailFathom.Infrastructure.UnitTests.TestDoubles;

/// <summary>A response body that does not say how long it is, as a chunked answer does not.</summary>
/// <remarks>
/// The content types the platform ships all declare a length when they can compute one, and a
/// <see cref="StreamContent" /> over a seekable stream can. A size bound has two branches, one refusing a declared
/// length and one buffering against the limit, and only content that reports no length reaches the second.
/// </remarks>
internal sealed class UndeclaredLengthContent(int byteCount) : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        stream.WriteAsync(new byte[byteCount]).AsTask();

    protected override bool TryComputeLength(out long length)
    {
        length = 0;

        return false;
    }
}
