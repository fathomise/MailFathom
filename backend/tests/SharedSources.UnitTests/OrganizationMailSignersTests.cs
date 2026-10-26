// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the organization signer the outbox is constructed with in every suite that builds one.</summary>
public sealed class OrganizationMailSignersTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly byte[] RawMime = Encoding.ASCII.GetBytes("Subject: synthetic\r\n\r\nbody\r\n");

    /// <summary>Most tests of a send are about something else, so the default shape has to store the message as composed.</summary>
    [Fact]
    public async Task SigningNothing_AMessage_ComesBackUnchanged()
    {
        // Arrange
        var signer = OrganizationMailSigners.SigningNothing();

        // Act
        var signed = await signer.SignAsync(Account, RawMime, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(RawMime, signed.ToArray());
    }

    /// <summary>A test asserting what the outbox stored needs the header above the message and the request it answered.</summary>
    [Fact]
    public async Task Adding_AMessage_WritesTheHeaderAboveItAndRecordsTheRequest()
    {
        // Arrange
        const string header = "MailFathom-Organization-Signature: v=1\r\n";
        var signer = OrganizationMailSigners.Adding(header);
        byte[] expected = [.. Encoding.ASCII.GetBytes(header), .. RawMime];

        // Act
        var signed = await signer.SignAsync(Account, RawMime, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expected, signed.ToArray());
        var (account, handedOver) = Assert.Single(signer.Requests);
        Assert.Equal(Account, account);
        Assert.Equal(RawMime, handedOver);
    }
}
