// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Categories;

/// <summary>A bounded excerpt of the first message of a conversation, which is what a reply is read against.</summary>
/// <param name="Text">The excerpt.</param>
/// <param name="IsCut">Whether the message went on past it.</param>
public sealed record ConversationOpening(string Text, bool IsCut)
{
    /// <summary>The most characters of a conversation's first message one request carries.</summary>
    /// <remarks>Enough to say what a conversation is about, and small enough that a reply costs little more than any other message.</remarks>
    public const int MaximumCharacters = 1_000;
}
