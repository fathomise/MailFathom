// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Rules.Conditions;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Reads a condition the way a test scripts it, without the condition language.</summary>
/// <remarks>
/// A condition naming <see cref="UnknownFact" /> is refused, as the real compiler refuses a fact it does not know; a
/// condition reading exactly <c>false</c> never matches; every other one matches every message.
/// </remarks>
internal sealed class ScriptedMailRuleConditionCompiler : IMailRuleConditionCompiler
{
    /// <summary>The text whose presence makes a condition one this compiler refuses.</summary>
    internal const string UnknownFact = "NoSuchFact";

    /// <summary>Gets how many conditions have been read.</summary>
    internal int CompileCount { get; private set; }

    /// <inheritdoc />
    public MailRuleConditionCompilation Compile(string ruleName, string? conditionText, MailRuleConditionBounds bounds)
    {
        this.CompileCount++;

        return conditionText switch
        {
            null => MailRuleConditionCompilation.Refused([$"Rule {ruleName} has no condition."]),
            _ when conditionText.Contains(UnknownFact, StringComparison.Ordinal) =>
                MailRuleConditionCompilation.Refused([$"Rule {ruleName} names a fact this deployment does not know."]),
            _ when conditionText.Length > bounds.MaxLength =>
                MailRuleConditionCompilation.Refused([$"Rule {ruleName} has a condition longer than the bound."]),
            "false" => MailRuleConditionCompilation.Compiled(ScriptedMailRuleCondition.Answering(matches: false)),
            _ => MailRuleConditionCompilation.Compiled(ScriptedMailRuleCondition.Answering(matches: true)),
        };
    }
}
