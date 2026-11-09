// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

public sealed class TransmittedEmailFormsTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly DateTimeOffset TransmittedAt = new(2026, 11, 9, 9, 0, 0, TimeSpan.Zero);

    private static readonly byte[] RawMime = Encoding.ASCII.GetBytes("Subject: synthetic\r\n\r\nbody\r\n");

    /// <summary>Most tests of a send are about something else, so the default shape has to transmit the message as stored.</summary>
    [Fact]
    public async Task Unchanged_AMessage_ComesBackAsStored()
    {
        // Act
        var transmitted = await TransmittedEmailForms.Unchanged().ComposeAsync(Account, RawMime, TransmittedAt, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(RawMime, transmitted.ToArray());
    }

    /// <summary>A test asserting that a send was dated reads back exactly the bytes the dating shape names for its instant.</summary>
    [Fact]
    public async Task Dating_AMessage_WritesTheHeaderNamingTheInstantAboveIt()
    {
        // Act
        var transmitted = await TransmittedEmailForms.Dating().ComposeAsync(Account, RawMime, TransmittedAt, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TransmittedEmailForms.DatedAt(RawMime, TransmittedAt), transmitted.ToArray());
        Assert.StartsWith($"{TransmittedEmailForms.DatedHeaderName}: 2026-11-09T09:00:00.0000000+00:00\r\n", Encoding.ASCII.GetString(transmitted.Span), StringComparison.Ordinal);
    }
}
