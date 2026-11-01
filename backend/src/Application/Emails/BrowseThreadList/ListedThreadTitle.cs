// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.ThreadStates;

namespace MailFathom.Application.Emails.BrowseThreadList;

/// <summary>What a listed conversation is called, together with the shape of the conversation it was derived from.</summary>
/// <param name="Title">The topic and the standing a derivation wrote.</param>
/// <param name="DerivedFrom">How many messages the derivation counted and when the newest of them arrived.</param>
/// <remarks>
/// The revision travels with the title because the title can lag the conversation: a reply that arrived since names a
/// conversation the standing no longer describes, and a client that sees the revision move knows the title changed.
/// </remarks>
public sealed record ListedThreadTitle(ThreadTitle Title, ThreadStateRevision DerivedFrom);
