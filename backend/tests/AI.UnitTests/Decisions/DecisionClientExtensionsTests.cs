// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Decisions;
using MailFathom.AI.UnitTests.TestDoubles;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace MailFathom.AI.UnitTests.Decisions;

/// <summary>Covers a decision client offered to an agent as a tool, which is the shape an agent reaches a closed question through.</summary>
public sealed class DecisionClientExtensionsTests
{
    private const string ToolName = "judge_spam";

    private static readonly DecisionQuestion[] Questions = [DecisionQuestion.YesNo("spam", "Is this spam?")];

    [Fact]
    public async Task AsAIFunction_CalledByAnAgent_AsksTheFixedQuestionsAboutTheTextTheAgentPassed()
    {
        // Arrange
        using var decisions = ScriptedDecisionClient.AnsweringFirstOption();
        var tool = decisions.AsAIFunction(ToolName, "Says whether a text is spam.", Questions);
        using var chat = ScriptedChatClient.CallingTool(
            ToolName,
            new Dictionary<string, object?> { ["input"] = "Claim your prize now." },
            "It is spam.");
        var agent = new ChatClientAgent(chat, new ChatClientAgentOptions
        {
            Name = "triage",
            ChatOptions = new ChatOptions { Tools = [tool] },
        });

        // Act
        var answer = await agent.RunAsync("Is the newest message spam?", session: null, options: null, TestContext.Current.CancellationToken);

        // Assert
        var call = Assert.Single(decisions.Calls);
        Assert.Equal("Claim your prize now.", call.Input.Single().Text);
        Assert.Same(Questions[0], Assert.Single(call.Questions));
        var result = Assert.Single(chat.Calls[1].Messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>());
        Assert.Contains(DecisionQuestion.Yes, result.Result?.ToString(), StringComparison.Ordinal);
        Assert.Equal("It is spam.", answer.Text);
    }

    [Fact]
    public void AsAIFunction_NoQuestions_IsRefusedWhenTheToolIsMade()
    {
        // Arrange
        using var decisions = ScriptedDecisionClient.AnsweringFirstOption();

        // Act, Assert
        Assert.Throws<ArgumentException>(() => decisions.AsAIFunction(ToolName, "Says nothing.", []));
    }
}
