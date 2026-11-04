// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Application.UnitTests.Accounts.Custody;

/// <summary>Covers which custody phases store a draft revision before appending it, which is what decides whether it is shown before the server has it.</summary>
public sealed class OutgoingCopyPreparationTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("primary");

    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A held account files every revision locally. A mirrored or restoring account stores one first only where its
    /// server was last observed naming where an appended message lands, because only then can the appended copy be bound
    /// onto the stored row rather than downloaded as a second one.
    /// </summary>
    [Theory]
    [InlineData(MailAccountCustodyPhase.Held, MailboxServerCapabilities.None, true)]
    [InlineData(MailAccountCustodyPhase.Mirrored, MailboxServerCapabilities.UidPlus, true)]
    [InlineData(MailAccountCustodyPhase.Mirrored, MailboxServerCapabilities.Imap4Rev2, true)]
    [InlineData(MailAccountCustodyPhase.Mirrored, MailboxServerCapabilities.None, false)]
    [InlineData(MailAccountCustodyPhase.Restoring, MailboxServerCapabilities.UidPlus, true)]
    [InlineData(MailAccountCustodyPhase.Restoring, MailboxServerCapabilities.None, false)]
    public async Task PrepareDraftCopyAsync_EachPhaseAndWhatItsServerAdvertised_PreparesALocalCopyOnlyWhereItCanBeBound(
        MailAccountCustodyPhase phase,
        MailboxServerCapabilities advertised,
        bool prepared)
    {
        // Arrange
        var mailbox = new HeldLocalMailbox(Account, new FakeTimeProvider(Now))
        {
            ServerCapabilities = MailboxServerCapabilityObservation.Observed(advertised, Now),
        };
        mailbox.Folders.Phase = phase;
        mailbox.MapRole(MailFolderSpecialUse.Drafts, "drafts");
        var custodian = await mailbox.Composing(ContentStores.Substituted()).Custodians.ResolveAsync(
            Account,
            TestContext.Current.CancellationToken);

        // Act
        var copy = await custodian.PrepareDraftCopyAsync(
            Account,
            "Subject: draft\r\n\r\nbody"u8.ToArray(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(prepared, copy is not null);
    }
}
