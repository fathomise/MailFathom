// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.ReplyDrafts;

/// <summary>The passages of a draft rewritten as somebody asked, each to stand where the one it replaces stood.</summary>
/// <remarks>
/// <para>
/// <b>It is a proposal and nothing else.</b> Nothing is sent, stored, or written into the draft here: the client shows each
/// passage beside the one it would replace, and the person keeps or undoes it. That is why it carries the passages alone
/// and never the draft they would make — the draft is the person's, and stays exactly as they left it until they say.
/// </para>
/// <para>
/// One passage comes back for every passage asked about, in the same order, or none at all. A rewriting that answered
/// some and not others would leave a client guessing which stretch a text belongs to, so a partial answer is
/// <see cref="Nothing" /> rather than half a rewriting.
/// </para>
/// </remarks>
public sealed record PassageRewrite
{
    /// <summary>The greatest length one rewritten passage carries before it is shortened to it.</summary>
    /// <remarks>The bound the whole draft is sent under, because a passage stands inside that draft and one rewritten past it has stopped being a passage.</remarks>
    public const int MaximumPassageLength = PassageRewriteRequest.MaximumTextLength;

    private PassageRewrite(bool wasWritten, IReadOnlyList<string> passages)
    {
        this.WasWritten = wasWritten;
        this.Passages = passages;
    }

    /// <summary>Gets the answer a rewriting that produced nothing gives, which leaves the draft as it was.</summary>
    public static PassageRewrite Nothing { get; } = new(wasWritten: false, []);

    /// <summary>Gets whether the passages were rewritten at all.</summary>
    public bool WasWritten { get; }

    /// <summary>Gets one rewritten passage for each passage asked about, in the order they were asked about, which is empty where nothing was written.</summary>
    public IReadOnlyList<string> Passages { get; }

    /// <summary>Records the rewritten passages.</summary>
    /// <param name="passages">One text for each passage asked about, in the order they were asked about.</param>
    /// <returns>The rewriting, each passage trimmed and shortened to <see cref="MaximumPassageLength" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="passages" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when there is no passage or one is blank, a rewritten passage being words somebody can keep rather than a deletion dressed as one.</exception>
    public static PassageRewrite Written(IReadOnlyList<string> passages)
    {
        ArgumentNullException.ThrowIfNull(passages);

        if (passages.Count is 0 || passages.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("A rewriting carries words for every passage it was asked about.", nameof(passages));
        }

        return new PassageRewrite(
            wasWritten: true,
            [.. passages.Select(static passage => MailTextBounds.TruncateAtTextElementBoundary(passage.Trim(), MaximumPassageLength))]);
    }
}
