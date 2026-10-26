// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using MailFathom.Application.Mail.Delivery.Signing;
using MailFathom.Domain.Accounts;

namespace MailFathom.TestSupport;

/// <summary>Builds the organization signer the outbox is exercised against.</summary>
/// <remarks>
/// Most tests of a send are about something other than its signature, so the default shape is the deployment where the
/// sending account belongs to no organization: the message is stored exactly as it was composed. The recording shape is
/// for the tests that assert what reached the signer and what the outbox stored in its place.
/// </remarks>
internal static class OrganizationMailSigners
{
    /// <summary>Builds a signer for an account in no organization, which hands every message back unchanged.</summary>
    /// <returns>The signer.</returns>
    internal static IOrganizationMailSigner SigningNothing() => new RecordingOrganizationMailSigner(header: null);

    /// <summary>Builds a signer that adds a fixed header above every message and records what it was asked to sign.</summary>
    /// <param name="header">The header line, ending in CRLF, written above each message.</param>
    /// <returns>The signer.</returns>
    internal static RecordingOrganizationMailSigner Adding(string header) => new(header);

    /// <summary>A signer that records each message it was handed and answers with a fixed header written above it.</summary>
    internal sealed class RecordingOrganizationMailSigner(string? header) : IOrganizationMailSigner
    {
        /// <summary>Gets every request, in order: the account and the bytes it was asked to sign.</summary>
        internal List<(MailAccountId Account, byte[] RawMime)> Requests { get; } = [];

        /// <inheritdoc />
        public Task<ReadOnlyMemory<byte>> SignAsync(
            MailAccountId account,
            ReadOnlyMemory<byte> rawMime,
            CancellationToken cancellationToken)
        {
            this.Requests.Add((account, rawMime.ToArray()));

            if (header is null)
            {
                return Task.FromResult(rawMime);
            }

            byte[] signed = [.. Encoding.ASCII.GetBytes(header), .. rawMime.ToArray()];

            return Task.FromResult<ReadOnlyMemory<byte>>(signed);
        }
    }
}
