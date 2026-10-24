// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel;
using MailFathom.Application.Answers;
using MailFathom.Application.EmailContent.Rendering.Document;

namespace MailFathom.Mcp.Tools.Answering;

/// <summary>Publishes one link an answer writes, with what the service established about it.</summary>
/// <remarks>
/// The judgement is the one a link in mail gets, made once in the service, so a caller relaying the answer to a person
/// can warn exactly where the client would rather than deriving a second opinion of its own.
/// </remarks>
[Description("One link the answer writes, and what MailFathom established about it before anybody follows it.")]
internal sealed record AnsweredLink
{
    /// <summary>Gets where the link goes.</summary>
    [Description("The absolute address the link goes to: an http, https, or mailto address.")]
    public required string Target { get; init; }

    /// <summary>Gets the target's host as a person recognizes it.</summary>
    [Description("The host the link goes to as a person reads it, or null for a mailto link, which has none.")]
    public string? Host { get; init; }

    /// <summary>Gets the host's ASCII form where it differs from the form a person reads.</summary>
    [Description("The same host in its ASCII form, present only where it differs from host — which is what a host written in a lookalike script looks like. Show both to a person when it is present.")]
    public string? AsciiHost { get; init; }

    /// <summary>Gets whether the link's own words name a host other than the one it goes to.</summary>
    [Description("Whether the words the link shows name one host while the link goes to another. True is the oldest trick in phishing; say so before a person follows it.")]
    public required bool WordsNameAnotherHost { get; init; }

    /// <summary>Gets whether any email the answer was drawn from carries the same address.</summary>
    [Description("Whether the text of any email the run retrieved carries this address. False means the model wrote it, or a message asked it to: it is not something the person's mail said.")]
    public required bool AppearsInCitedEmails { get; init; }

    /// <summary>Publishes one judged link.</summary>
    /// <param name="link">The link to publish.</param>
    /// <returns>The wire representation of <paramref name="link" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="link" /> is <see langword="null" />.</exception>
    public static AnsweredLink From(AnswerLink link)
    {
        ArgumentNullException.ThrowIfNull(link);

        return new AnsweredLink
        {
            Target = link.Link.Target,
            Host = link.Link.Host,
            AsciiHost = link.Link.AsciiHost,
            WordsNameAnotherHost = link.Link.Deception is MailLinkDeception.DisplayedHostDiffers,
            AppearsInCitedEmails = link.AppearsInCitedSources,
        };
    }
}
