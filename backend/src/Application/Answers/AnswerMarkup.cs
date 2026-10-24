// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Net;
using System.Text.RegularExpressions;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.Retrieval;

namespace MailFathom.Application.Answers;

/// <summary>Holds an AI answer's Markdown to what the service lets it carry: no picture, and every link judged.</summary>
/// <remarks>
/// <para>
/// An answer is written by a model that read mail anybody can send, so a message can ask it to carry a picture or a link.
/// A picture is fetched by whatever renders the answer the moment it is drawn, which hands the address a message chose a
/// request nobody decided to make; so every answer the service returns — to the client and to an MCP caller alike — has
/// its picture markup turned into something no renderer fetches. Whether the renderer would have drawn it is beside the
/// point: the rule is the service's, so it holds for a renderer nobody here wrote.
/// </para>
/// <para>
/// A link is kept, because following one is a decision a reader makes. What it gets instead is the judgement a link in
/// mail gets, and a statement of whether the mail the answer cites carries it at all.
/// </para>
/// <para>
/// The reading is by pattern rather than by a Markdown parser, and it is one-sided on purpose. Neutralising a picture
/// needs no parse at all, because <c>![</c> opens every picture form Markdown has and nothing else; and a link the
/// patterns below do not find is one a client finds no judgement for, which it draws as the least trusted link it can.
/// </para>
/// </remarks>
public static partial class AnswerMarkup
{
    /// <summary>The most links one answer carries a judgement for.</summary>
    /// <remarks>An answer is a few paragraphs; one with more links than this is a link list a message asked for, and a link past the bound is drawn as unjudged rather than dropped.</remarks>
    public const int MaximumLinks = 32;

    private static readonly char[] TrailingPunctuation = ['.', ',', ':', ';', '!', '?', '*', '_', '~', '\'', '"'];

    /// <summary>Turns every picture an answer's Markdown would draw into something no renderer fetches.</summary>
    /// <param name="answer">The answer as the model wrote it.</param>
    /// <returns>The answer with each Markdown picture turned into a link and each raw HTML tag escaped into text.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="answer" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// A Markdown picture becomes a link to the same address, so its target is judged like any other link and a reader
    /// may still decide to follow it — what goes is only the request a renderer would have made on its own. A raw HTML
    /// tag is escaped rather than removed, so the words around it survive and every tag is held to the same rule: there
    /// is no list of the tags that fetch, because which tags a renderer fetches for is the renderer's choice.
    /// </para>
    /// <para>
    /// Neither change looks inside code. An HTML tag inside a code span therefore reads with a backslash before it, which
    /// is a blemish in an answer about mail rather than a hole in the rule.
    /// </para>
    /// <para>
    /// Escaping a tag adds a character, so a caller applies this before it cuts an answer to what it publishes rather
    /// than after.
    /// </para>
    /// </remarks>
    public static string WithoutPictures(string answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        return HtmlTagOpening().Replace(PictureOpening().Replace(answer, "["), @"\<");
    }

    /// <summary>Judges every link an answer's Markdown carries and says whether the cited mail carries it too.</summary>
    /// <param name="answer">The answer as it is published, after <see cref="WithoutPictures" /> and any cut.</param>
    /// <param name="citedSources">The text of every source the answer cites, as the model was shown it.</param>
    /// <returns>One judgement per distinct target, in the order the targets first appear, and at most <see cref="MaximumLinks" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="answer" /> or <paramref name="citedSources" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// A target written twice under different words keeps the more alarming judgement of the two, because a client looks
    /// a link up by where it goes and the honest occurrence must not vouch for the deceptive one.
    /// </para>
    /// <para>
    /// A link's words are judged as a renderer shows them rather than as they are written: emphasis, strikethrough and
    /// code-span marks are dropped, and backslash escapes and character references read as the characters they stand
    /// for. Otherwise <c>[**bank.example**](…)</c> would name no host and escape the judgement its plain form gets.
    /// </para>
    /// <para>
    /// The cited text is compared as addresses rather than as text: an address the mail carries inside a longer one is
    /// not the same place, and an address written in a different case of the same host is.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<AnswerLink> LinksIn(string answer, IEnumerable<string> citedSources)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(citedSources);

        var carried = citedSources.SelectMany(AddressesIn).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return
        [
            .. WrittenLinks(answer)
                .OrderBy(static written => written.Index)
                .Select(static written => MailLinkReader.Read(written.Reference, written.Words))
                .OfType<MailDocumentLink>()
                .GroupBy(static link => link.Target, StringComparer.Ordinal)
                .Select(static sameTarget => sameTarget.MaxBy(static link => link.Deception)!)
                .Take(MaximumLinks)
                .Select(link => new AnswerLink(link, carried.Contains(PlaceOf(link.Target)))),
        ];
    }

    /// <summary>Reads the text a run showed a model out of the passages it retrieved: each extract, and what each file contributed beside it.</summary>
    /// <param name="passages">The passages the answer cites.</param>
    /// <returns>The texts, which is what <see cref="LinksIn" /> traces an answer's links to.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="passages" /> is <see langword="null" />.</exception>
    public static IEnumerable<string> TextOf(IEnumerable<EmailKnowledgePassage> passages)
    {
        ArgumentNullException.ThrowIfNull(passages);

        return passages.SelectMany(static passage => passage.AttachmentExtracts.Select(static extract => extract.Text).Prepend(passage.Text));
    }

    /// <summary>Every link the answer writes, as where it is written, the reference it names, and the words it shows.</summary>
    private static IEnumerable<(int Index, string Reference, string Words)> WrittenLinks(string answer)
    {
        foreach (Match inline in InlineLink().Matches(answer))
        {
            yield return (inline.Index, inline.Groups["reference"].Value, Shown(inline.Groups["words"].Value));
        }

        foreach (Match automatic in AutomaticLink().Matches(answer))
        {
            var reference = automatic.Groups["reference"].Value;

            yield return reference.Contains(':', StringComparison.Ordinal)
                ? (automatic.Index, reference, reference)
                : (automatic.Index, $"mailto:{reference}", reference);
        }

        foreach (Match bare in BareAddress().Matches(answer))
        {
            yield return (bare.Index, Absolute(bare.Value), bare.Value);
        }

        var usages = ReferenceUsage().Matches(answer).ToArray();

        foreach (Match definition in ReferenceDefinition().Matches(answer))
        {
            var label = definition.Groups["label"].Value;
            var reference = definition.Groups["reference"].Value;

            // The collapsed and shortcut forms show the label itself; the full form shows its own words.
            yield return (definition.Index, reference, Shown(label));

            foreach (var usage in usages.Where(candidate => string.Equals(candidate.Groups["label"].Value, label, StringComparison.OrdinalIgnoreCase)))
            {
                yield return (usage.Index, reference, Shown(usage.Groups["words"].Value));
            }
        }
    }

    /// <summary>Reads a link's words as a renderer shows them: without inline marks, and with every escape read as what it stands for.</summary>
    private static string Shown(string words) =>
        WebUtility.HtmlDecode(InlineMarkOrEscape().Replace(words, static found => found.Groups["escaped"].Value));

    /// <summary>Every address a source's text carries, in the form an answer link's target is compared in.</summary>
    private static IEnumerable<string> AddressesIn(string source) =>
        SourceAddress().Matches(source).Select(static found => Absolute(found.Value.TrimEnd(TrailingPunctuation)))
        .Concat(SourceMailbox().Matches(source).Select(static found => $"mailto:{found.Value}"))
        .Select(static reference => MailLinkReader.Read(reference, string.Empty)?.Target)
        .OfType<string>()
        .Select(PlaceOf);

    /// <summary>Writes a bare <c>www.</c> address as the link a renderer turns it into.</summary>
    private static string Absolute(string address) =>
        address.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? $"http://{address}" : address;

    /// <summary>Drops what a <c>mailto:</c> target adds to the mailbox it names, which is not where it goes.</summary>
    private static string PlaceOf(string target) =>
        target.StartsWith(Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase) && target.IndexOf('?', StringComparison.Ordinal) is > 0 and var query
            ? target[..query]
            : target;

    // Every exclamation mark in front of a bracket at once: dropping one from `!![` would leave `![`, a picture again.
    [GeneratedRegex(@"!+\[")]
    private static partial Regex PictureOpening();

    // A tag's opening bracket, unless a backslash already escapes it: an odd run of backslashes before it is an escape,
    // an even one is backslashes written as text. Autolinks are left alone, because a scheme is followed by a colon.
    [GeneratedRegex(@"(?<=(?:^|[^\\])(?:\\\\)*)<(?=/?[A-Za-z][A-Za-z0-9-]*(?:[\s/>]|$))")]
    private static partial Regex HtmlTagOpening();

    // An inline link with its destination bare or in angle brackets, one level of parentheses inside it, and a title.
    [GeneratedRegex(@"\[(?<words>[^\[\]\n]*)\]\(\s*(?:<(?<reference>[^<>\n]*)>|(?<reference>(?:[^\s()]|\([^\s()]*\))+))(?:\s+(?:""[^""\n]*""|'[^'\n]*'|\([^()\n]*\)))?\s*\)")]
    private static partial Regex InlineLink();

    // An autolink to an absolute address, or to a mailbox.
    [GeneratedRegex(@"<(?<reference>[A-Za-z][A-Za-z0-9+.-]{1,31}:[^<>\s]*|[^<>\s@:]+@[^<>\s@]+)>")]
    private static partial Regex AutomaticLink();

    // A bare address a GitHub-flavoured renderer links on its own, without the punctuation that ends the sentence after it.
    [GeneratedRegex(@"(?<![\w<\[""'/=@])(?<!\]\(\s*)(?:https?://|www\.)[^\s<]*[^\s<?!.,:;*_~'""\])]", RegexOptions.IgnoreCase)]
    private static partial Regex BareAddress();

    // A backslash escape of any ASCII punctuation, which shows the character escaped, or an inline mark, which shows nothing.
    [GeneratedRegex(@"\\(?<escaped>[!-/:-@\[-`{-~])|[*_~`]")]
    private static partial Regex InlineMarkOrEscape();

    [GeneratedRegex(@"^ {0,3}\[(?<label>[^\[\]\n]+)\]:[ \t]*<?(?<reference>[^\s<>]+)>?", RegexOptions.Multiline)]
    private static partial Regex ReferenceDefinition();

    [GeneratedRegex(@"\[(?<words>[^\[\]\n]+)\]\[(?<label>[^\[\]\n]+)\]")]
    private static partial Regex ReferenceUsage();

    [GeneratedRegex(@"(?:https?://|www\.)[^\s<>""'()\[\]]+", RegexOptions.IgnoreCase)]
    private static partial Regex SourceAddress();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex SourceMailbox();
}
