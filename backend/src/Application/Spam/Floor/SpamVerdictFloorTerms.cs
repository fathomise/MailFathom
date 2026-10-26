// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Folders;

namespace MailFathom.Application.Spam.Floor;

/// <summary>What one pass of the spam floor reads an account's mail under.</summary>
/// <param name="JunkFolders">The aliases of the account's junk folders, empty where it maps none.</param>
/// <param name="ClassifiedFolders">The folders the account's classification covers, empty where it classifies nothing.</param>
/// <param name="AwaitedWhenStoredAfter">The instant after which a covered message with no verdict yet is still awaiting its classification.</param>
/// <remarks>
/// A message classification still awaits is left to it, because the classification applies the same floor and records
/// every signal the message carried beside it. The floor reaches it once the wait a verdict is allowed has run out, so a
/// classification that stalled or was never queued leaves no message below the floor for longer than that.
/// </remarks>
public sealed record SpamVerdictFloorTerms(
    IReadOnlyList<MailFolderAlias> JunkFolders,
    IReadOnlyList<MailFolderAlias> ClassifiedFolders,
    DateTimeOffset AwaitedWhenStoredAfter);
