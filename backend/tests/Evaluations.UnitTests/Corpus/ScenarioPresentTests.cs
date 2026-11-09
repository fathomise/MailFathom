// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Delivery.Receipts;
using MailFathom.Evaluations.AgentConversations;
using MailFathom.Evaluations.Answering;
using MailFathom.Evaluations.Corpus;
using MailFathom.Evaluations.Discovery;
using Xunit;

namespace MailFathom.Evaluations.UnitTests.Corpus;

/// <summary>Proves that nothing a question is asked over, nor any report recorded on it, is dated after the instant the question is asked.</summary>
public sealed class ScenarioPresentTests
{
    [Theory]
    [InlineData("MailAnswering")]
    [InlineData("Agent")]
    [InlineData("DiscoveryComposition")]
    public void Mailbox_AScenarioAskingOverIt_HoldsNothingDatedAfterThatScenariosPresent(string scenario)
    {
        // Arrange
        var (present, mailbox) = PresentAndMailboxOf(scenario);

        // Act
        var later = mailbox
            .DistinctBy(static message => message.Id)
            .Where(message => DatesOf(message).Any(date => date > present))
            .Select(static message => message.Subject);

        // Assert
        Assert.Empty(later);
    }

    private static (DateTimeOffset Present, IEnumerable<CorpusMessage> Mailbox) PresentAndMailboxOf(string scenario) => scenario switch
    {
        "MailAnswering" => (MailAnsweringScenario.AskedAt, MailAnsweringScenario.All.SelectMany(static asked => asked.Mailbox)),
        "Agent" => (AgentConversationScenario.AskedAt, AgentConversationScenario.Mailbox),
        "DiscoveryComposition" => (DiscoveryCompositionScenario.ObservedAt, DiscoveryCompositionScenario.All.SelectMany(static asked => asked.Mailbox)),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "No scenario of that name asks over the mailbox."),
    };

    private static IEnumerable<DateTimeOffset> DatesOf(CorpusMessage message) =>
    [
        message.ReceivedAt,
        .. (message.Receipts?.Recipients ?? [])
            .SelectMany(static recipient => new[] { recipient.Delivery, recipient.Read })
            .OfType<RecordedReceiptOutcome>()
            .Select(static outcome => outcome.ReportedAt),
    ];
}
