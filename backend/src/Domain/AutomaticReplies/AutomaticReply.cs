// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Domain.AutomaticReplies;

/// <summary>The reply a user has MailFathom send to mail arriving on the accounts they chose, for a period they set.</summary>
/// <remarks>
/// <para>
/// Two texts, because what a colleague is told and what a stranger is told are usually different: the one for senders
/// inside the user's organization is optional, and a sender it does not reach is answered with the other. A user in no
/// organization has no colleagues to tell anything, so for them the text for senders outside it is the whole reply.
/// </para>
/// <para>
/// The period is half-open — answering from <see cref="StartsAt" /> up to but not at <see cref="EndsAt" /> — so a reply
/// set to end when the next one starts never answers one message with both. Switching it off before the end keeps
/// everything else as it was, so switching it on again needs nothing retyped.
/// </para>
/// <para>
/// Both texts are the user's own writing and are never logged.
/// </para>
/// </remarks>
public sealed record AutomaticReply
{
    /// <summary>The longest text either side of the organization may be answered with.</summary>
    /// <remarks>A few screens of prose, which is far beyond what anybody writes in an absence notice and well inside the body a composed message is allowed.</remarks>
    public const int MaximumTextLength = 8000;

    /// <summary>The most accounts one reply may answer on.</summary>
    /// <remarks>Above the number of mailboxes anybody is assigned, and the bound on how large the stored list and every check against it can become.</remarks>
    public const int MaximumAccountCount = 64;

    private AutomaticReply(
        string? insideText,
        string outsideText,
        IReadOnlyList<MailAccountId> accounts,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        bool isOn)
    {
        this.InsideText = insideText;
        this.OutsideText = outsideText;
        this.Accounts = accounts;
        this.StartsAt = startsAt;
        this.EndsAt = endsAt;
        this.IsOn = isOn;
    }

    /// <summary>Gets the text a sender inside the user's organization is answered with, or <see langword="null" /> where they are answered with <see cref="OutsideText" /> too.</summary>
    public string? InsideText { get; }

    /// <summary>Gets the text every other sender is answered with.</summary>
    public string OutsideText { get; }

    /// <summary>Gets the accounts whose arriving mail is answered, each named once.</summary>
    public IReadOnlyList<MailAccountId> Accounts { get; }

    /// <summary>Gets the instant answering starts.</summary>
    public DateTimeOffset StartsAt { get; }

    /// <summary>Gets the instant answering stops, which is after <see cref="StartsAt" />.</summary>
    public DateTimeOffset EndsAt { get; }

    /// <summary>Gets whether the reply is switched on; one switched off answers nothing whatever the period says.</summary>
    public bool IsOn { get; }

    /// <summary>Gets when the reply answers, without what it answers with.</summary>
    public AutomaticReplyPeriod Period => new(this.StartsAt, this.EndsAt, this.IsOn);

    /// <summary>Reads a reply out of what somebody wrote, or says why it is not one.</summary>
    /// <param name="insideText">The text for senders inside the organization, or <see langword="null" /> or blank for none.</param>
    /// <param name="outsideText">The text for every other sender.</param>
    /// <param name="accounts">The accounts it answers on.</param>
    /// <param name="startsAt">The instant answering starts.</param>
    /// <param name="endsAt">The instant answering stops.</param>
    /// <param name="isOn">Whether it is switched on.</param>
    /// <param name="reply">The reply, when the answer is <see langword="true" />.</param>
    /// <param name="refusal">Why it is not one, when the answer is <see langword="false" />.</param>
    /// <returns><see langword="true" /> when what was written is a reply.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="accounts" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Refused rather than repaired, for the reason every write on the client surface is: a person told their reply was
    /// saved and handed back a different one has a reply they did not write. A text is kept as written, line breaks and
    /// all, apart from the whitespace around it.
    /// </remarks>
    public static bool TryCreate(
        string? insideText,
        string? outsideText,
        IReadOnlyList<MailAccountId> accounts,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        bool isOn,
        out AutomaticReply? reply,
        out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        reply = null;
        refusal = Refuse(insideText, outsideText, accounts, startsAt, endsAt);

        if (refusal is not null)
        {
            return false;
        }

        reply = new AutomaticReply(
            string.IsNullOrWhiteSpace(insideText) ? null : insideText.Trim(),
            outsideText!.Trim(),
            [.. accounts],
            startsAt.ToUniversalTime(),
            endsAt.ToUniversalTime(),
            isOn);

        return true;
    }

    /// <summary>Answers whether mail arriving on an account at an instant is answered.</summary>
    /// <param name="account">The account the mail arrived on.</param>
    /// <param name="at">The instant it is answered at.</param>
    /// <returns><see langword="true" /> while the reply is switched on, in its period, and names the account.</returns>
    public bool IsAnswering(MailAccountId account, DateTimeOffset at) =>
        this.Period.Covers(at) && this.Accounts.Contains(account);

    /// <summary>Chooses the text a sender is answered with.</summary>
    /// <param name="senderIsInsideOrganization">Whether the sender was proven a member of the organization.</param>
    /// <returns>The text for senders inside it where they are and one was written, and the other text otherwise.</returns>
    public string TextFor(bool senderIsInsideOrganization) =>
        senderIsInsideOrganization && this.InsideText is { } inside ? inside : this.OutsideText;

    private static string? Refuse(
        string? insideText,
        string? outsideText,
        IReadOnlyList<MailAccountId> accounts,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt)
    {
        if (string.IsNullOrWhiteSpace(outsideText))
        {
            return "An automatic reply carries a text for senders outside your organization.";
        }

        if (outsideText.Trim().Length > MaximumTextLength
            || (insideText is not null && insideText.Trim().Length > MaximumTextLength))
        {
            return $"An automatic reply's text is at most {MaximumTextLength} characters long.";
        }

        if (accounts.Count == 0)
        {
            return "An automatic reply answers on at least one account.";
        }

        if (accounts.Count > MaximumAccountCount)
        {
            return $"An automatic reply answers on at most {MaximumAccountCount} accounts.";
        }

        if (accounts.Distinct().Count() != accounts.Count)
        {
            return "An automatic reply names each account once.";
        }

        return endsAt <= startsAt ? "An automatic reply's period ends after it starts." : null;
    }
}
