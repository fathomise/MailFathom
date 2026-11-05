// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;
using MailFathom.Application.Jobs.Scheduling;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Actions;
using MailFathom.Application.Rules.Editing;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;

namespace MailFathom.Host.Api;

/// <summary>One rule as an operator writes it: the rule object the mail-rules page documents.</summary>
/// <param name="Name">The name the rule is read under within its account.</param>
/// <param name="Condition">The condition, as written.</param>
/// <param name="Triggers">The automatic triggers it takes part in, by name.</param>
/// <param name="Schedule">The occasions a scheduled walk happens on, as <c>Every …</c> or <c>Daily at …</c>.</param>
/// <param name="Actions">What a match asks the mailbox for.</param>
/// <param name="StopWhenMatched">Whether a match ends the pass.</param>
/// <remarks>
/// <para>
/// It carries no account and no enabled state, and a body naming either is refused rather than read. A rule belongs to
/// the account the request names, so an <c>Accounts</c> key could only mean a second mailbox, and a rule is enabled by a
/// write of its own after a preview has shown what it does, so an <c>Enabled</c> key would let a create skip that.
/// </para>
/// <para>
/// Everything a write judges about a rule is the validator's, and this reads only what the validator cannot see: a
/// trigger, a schedule, a folder, or a keyword that is not a value at all. Each refusal names the key and what it takes,
/// and none repeats what the key held.
/// </para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record MailRuleDeclarationRequest(
    string? Name = null,
    string? Condition = null,
    IReadOnlyList<string>? Triggers = null,
    string? Schedule = null,
    MailRuleActionsRequest? Actions = null,
    bool StopWhenMatched = false)
{
    private static readonly JsonSerializerOptions Format = new(JsonSerializerDefaults.Web);

    /// <summary>Reads the rule a request carried as the draft a write or a preview takes.</summary>
    /// <param name="declaration">The rule object, as the request carried it.</param>
    /// <param name="refusal">What is wrong with it, when the answer is <see langword="null" />.</param>
    /// <returns>The draft, or <see langword="null" /> when the object is not one this reads.</returns>
    /// <remarks>
    /// Read here rather than bound with the request, because a binding failure answers with no reason at all, and a key
    /// a rule does not have — <c>Accounts</c> above all, which an older configuration carried — is the mistake an
    /// operator most needs named. The refusal names the key by its path and never the value it held.
    /// </remarks>
    internal static MailRuleDraft? Read(JsonElement? declaration, out string? refusal)
    {
        if (declaration is not { ValueKind: JsonValueKind.Object } written)
        {
            refusal = "The request carries no rule object.";

            return null;
        }

        MailRuleDeclarationRequest? request;

        try
        {
            request = written.Deserialize<MailRuleDeclarationRequest>(Format);
        }
        catch (JsonException failure)
        {
            refusal = $"The rule object could not be read at {failure.Path ?? "$"}: that is not a key a rule has, or its value is not the kind that key takes. A rule has Name, Condition, Triggers, Schedule, Actions, and StopWhenMatched, and no Accounts or Enabled.";

            return null;
        }

        return request!.ToDraft(out refusal);
    }

    private MailRuleDraft? ToDraft(out string? refusal)
    {
        var triggers = new List<MailRuleTrigger>();

        foreach (var trigger in this.Triggers ?? [])
        {
            if (!MailRuleTrigger.TryParseName(trigger, out var read))
            {
                refusal = $"The rule's Triggers names a trigger this deployment does not run. A trigger is one of {string.Join(", ", MailRuleTrigger.All.Select(known => known.Name))}.";

                return null;
            }

            triggers.Add(read);
        }

        JobRecurrence? schedule = null;

        if (this.Schedule is not null && !JobRecurrence.TryParse(this.Schedule, out schedule, out var scheduleError))
        {
            refusal = $"The rule's Schedule could not be read: {scheduleError}";

            return null;
        }

        if ((this.Actions ?? new MailRuleActionsRequest()).ToActions(out refusal) is not { } actions)
        {
            return null;
        }

        return new MailRuleDraft(this.Name ?? string.Empty, this.Condition ?? string.Empty, actions, this.StopWhenMatched, triggers, schedule);
    }
}

/// <summary>What a match asks the mailbox for, as a rule object writes it: one key per kind of change.</summary>
/// <param name="MoveTo">The folder a match is filed into, by alias or as <c>role:&lt;role&gt;</c>.</param>
/// <param name="CopyTo">The folder a copy is placed in, written the same way.</param>
/// <param name="Delete">Whether a match is removed.</param>
/// <param name="MarkAsRead">Whether <c>\Seen</c> is set or cleared; absent leaves it alone.</param>
/// <param name="MarkAsFlagged">Whether <c>\Flagged</c> is set or cleared; absent leaves it alone.</param>
/// <param name="AddKeywords">The keywords put on a match.</param>
/// <param name="RemoveKeywords">The keywords taken off a match.</param>
/// <param name="SetKeywords">The keywords a match ends up carrying and no others; empty clears every keyword.</param>
/// <remarks>Which of these may be declared together is the validator's to judge, so a pair it refuses is read here and refused there, naming the rule.</remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record MailRuleActionsRequest(
    string? MoveTo = null,
    string? CopyTo = null,
    bool? Delete = null,
    bool? MarkAsRead = null,
    bool? MarkAsFlagged = null,
    IReadOnlyList<string>? AddKeywords = null,
    IReadOnlyList<string>? RemoveKeywords = null,
    IReadOnlyList<string>? SetKeywords = null)
{
    /// <summary>Reads the declared keys as the changes they name, in the order the keys are listed above.</summary>
    /// <param name="refusal">Which key held something that is not a value it takes, when the answer is <see langword="null" />.</param>
    /// <returns>The changes, empty for a rule that selects mail and changes nothing.</returns>
    internal IReadOnlyList<MailRuleAction>? ToActions(out string? refusal)
    {
        var actions = new List<MailRuleAction>();

        refusal = AddDestination(actions, nameof(this.MoveTo), this.MoveTo, MailRuleAction.Relocate)
            ?? AddDestination(actions, nameof(this.CopyTo), this.CopyTo, MailRuleAction.Copy);

        if (this.Delete == true)
        {
            actions.Add(MailRuleAction.Delete());
        }

        if (this.MarkAsRead is { } isSeen)
        {
            actions.Add(MailRuleAction.SetSeen(isSeen));
        }

        if (this.MarkAsFlagged is { } isFlagged)
        {
            actions.Add(MailRuleAction.SetFlagged(isFlagged));
        }

        refusal ??= AddKeywordChange(actions, nameof(this.AddKeywords), this.AddKeywords, permitsNone: false, MailRuleAction.AddKeywords)
            ?? AddKeywordChange(actions, nameof(this.RemoveKeywords), this.RemoveKeywords, permitsNone: false, MailRuleAction.RemoveKeywords)
            ?? AddKeywordChange(actions, nameof(this.SetKeywords), this.SetKeywords, permitsNone: true, MailRuleAction.SetKeywords);

        return refusal is null ? actions : null;
    }

    private static string? AddDestination(
        List<MailRuleAction> actions,
        string key,
        string? destination,
        Func<MailFolderReference, MailRuleAction> toAction)
    {
        if (destination is null)
        {
            return null;
        }

        if (!MailFolderReference.TryCreate(destination, out var reference))
        {
            return $"The rule's {key} names no folder. A folder is named by its alias, or by its role as role:<role>.";
        }

        actions.Add(toAction(reference));

        return null;
    }

    /// <summary>Reads one keyword list; only the replacement may be empty, because an empty addition or removal asks for nothing.</summary>
    private static string? AddKeywordChange(
        List<MailRuleAction> actions,
        string key,
        IReadOnlyList<string>? keywords,
        bool permitsNone,
        Func<AuthoredMailKeywords, MailRuleAction> toAction)
    {
        if (keywords is null)
        {
            return null;
        }

        if (keywords.Any(keyword => keyword is null) || !AuthoredMailKeywords.TryCreate(keywords, out var authored))
        {
            return $"The rule's {key} names a keyword a mail server cannot store. A keyword is one unbroken token with no space or control character that does not begin with a backslash, and at most {RemoteEmailKeywords.MaximumKeywords} may be named.";
        }

        if (authored.IsEmpty && !permitsNone)
        {
            return $"The rule's {key} names no keyword, which asks for nothing. Leave the key out instead.";
        }

        actions.Add(toAction(authored));

        return null;
    }
}
