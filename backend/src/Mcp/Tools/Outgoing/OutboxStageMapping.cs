// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Delivery;

namespace MailFathom.Mcp.Tools.Outgoing;

/// <summary>Maps a published outbox stage onto the stage the outgoing record carries.</summary>
internal static class OutboxStageMapping
{
    /// <summary>Gets the stored stage a published one names.</summary>
    /// <param name="stage">The stage the caller named.</param>
    /// <returns>The stored stage.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="stage" /> is not a declared value.</exception>
    public static OutgoingEmailStage Stored(OutboxStage stage) => stage switch
    {
        OutboxStage.Queued => OutgoingEmailStage.Recorded,
        OutboxStage.Sending => OutgoingEmailStage.TransmissionBegun,
        OutboxStage.Refused => OutgoingEmailStage.Refused,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "The outbox stage has no stored counterpart."),
    };
}
