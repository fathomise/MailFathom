// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Screening;
using MailFathom.Application.Mail.Delivery.Tracking;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace MailFathom.Mcp.UnitTests.TestDoubles;

/// <summary>Assembles the real outbox reading over stores a test arranges, so a tool test exercises the use case rather than a substitute for it.</summary>
/// <remarks>
/// Every store left unnamed answers with nothing: no message content is held, so a preview carries no body, and no
/// page or count is held, so a reading answers empty. Which sends a caller may reach, and under which grants, are the
/// use case's own and are proven in <c>Application.UnitTests</c>; what a tool test needs is that the decision is
/// really taken.
/// </remarks>
internal static class UserOutboxes
{
    /// <summary>Builds the outbox reading a tool calls.</summary>
    /// <param name="accountCatalog">Says which accounts the caller's user is assigned.</param>
    /// <param name="authorization">Answers which grants the caller holds.</param>
    /// <param name="outgoingEmails">Holds the sends a preview reads, or <see langword="null" /> for none.</param>
    /// <param name="operations">Answers pages and counts, or <see langword="null" /> for a substitute answering nothing.</param>
    /// <param name="timeProvider">Decides whether an attempt still holds a send, or <see langword="null" /> for a clock fixed at <see cref="Now" />.</param>
    /// <returns>The outbox reading.</returns>
    public static UserOutbox Over(
        ICallerMailAccountCatalog accountCatalog,
        AccessAuthorization authorization,
        IOutgoingEmailStore? outgoingEmails = null,
        IOutboxOperationStore? operations = null,
        TimeProvider? timeProvider = null)
    {
        var languages = Substitute.For<IUserLanguages>();
        languages.LanguageOf(Arg.Any<UserId>()).Returns(UserLanguage.English);

        return new UserOutbox(
            accountCatalog,
            outgoingEmails ?? Substitute.For<IOutgoingEmailStore>(),
            operations ?? Substitute.For<IOutboxOperationStore>(),
            Substitute.For<IEmailContentStore>(),
            Substitute.For<IEmailContentRenderer>(),
            Substitute.For<IOutgoingMailTextReader>(),
            SensitiveContentEgressGuards.Inactive(),
            new EmailContentReadOptions(),
            languages,
            authorization,
            ClientSignalPublishers.ReachingNobody,
            timeProvider ?? new FakeTimeProvider(Now));
    }

    /// <summary>The instant the default clock reads, so a fixture can place an attempt lease on either side of it.</summary>
    public static DateTimeOffset Now { get; } = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
}
