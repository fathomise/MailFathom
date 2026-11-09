// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using System.Text;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Signing;
using MailFathom.Application.Mail.Delivery.Transmission;
using MailFathom.Domain.Accounts;
using NSubstitute;

namespace MailFathom.TestSupport;

/// <summary>Builds the derivation a send's transmitted bytes and its sent copy are composed through.</summary>
/// <remarks>
/// Most tests of a send are about something other than its date, so the default shape hands every message back as it
/// was stored. The dating shape writes a recognizable header above it naming the instant, which is what a test asserting
/// that a transmission or a sent copy was dated reads back; the real header rewriting is the infrastructure adapter's
/// own tests' subject.
/// </remarks>
internal static class TransmittedEmailForms
{
    /// <summary>The header the dating shape writes above each message.</summary>
    internal const string DatedHeaderName = "X-Test-Dated-At";

    /// <summary>Builds a derivation that leaves every message as it was stored.</summary>
    /// <returns>The derivation.</returns>
    internal static TransmittedEmailForm Unchanged() => new(Composer(dates: false), PassingSigner());

    /// <summary>Builds a derivation that writes <see cref="DatedHeaderName" /> above each message, naming the instant in round-trip form.</summary>
    /// <param name="signer">The signer the dated message reaches, or one that hands it back unchanged.</param>
    /// <returns>The derivation.</returns>
    internal static TransmittedEmailForm Dating(IOrganizationMailSigner? signer = null) =>
        new(Composer(dates: true), signer ?? PassingSigner());

    /// <summary>Reads the bytes the dating shape produces for a message and an instant.</summary>
    /// <param name="rawMime">The stored message.</param>
    /// <param name="transmittedAt">The instant it was dated with.</param>
    /// <returns>The dated message.</returns>
    internal static byte[] DatedAt(ReadOnlyMemory<byte> rawMime, DateTimeOffset transmittedAt) =>
        [.. Encoding.ASCII.GetBytes($"{DatedHeaderName}: {transmittedAt.ToString("O", CultureInfo.InvariantCulture)}\r\n"), .. rawMime.Span];

    private static IAuthoredEmailComposer Composer(bool dates)
    {
        var composer = Substitute.For<IAuthoredEmailComposer>();
        composer.DateAt(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<DateTimeOffset>())
            .Returns(call => dates
                ? (ReadOnlyMemory<byte>)DatedAt(call.ArgAt<ReadOnlyMemory<byte>>(0), call.ArgAt<DateTimeOffset>(1))
                : call.ArgAt<ReadOnlyMemory<byte>>(0));

        return composer;
    }

    private static IOrganizationMailSigner PassingSigner()
    {
        var signer = Substitute.For<IOrganizationMailSigner>();
        signer.SignAsync(Arg.Any<MailAccountId>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.ArgAt<ReadOnlyMemory<byte>>(1)));

        return signer;
    }
}
