// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Emails.BrowseThreadList;

/// <summary>Somebody who wrote into a listed conversation.</summary>
/// <param name="Address">Their address as they last wrote it.</param>
/// <param name="DisplayName">The name they last wrote beside it, or <see langword="null" /> where they never wrote one.</param>
/// <param name="IsReader">Whether the address is one the reader sends as, which a row draws as the reader rather than by name.</param>
public sealed record ListedThreadParticipant(string Address, string? DisplayName, bool IsReader);
