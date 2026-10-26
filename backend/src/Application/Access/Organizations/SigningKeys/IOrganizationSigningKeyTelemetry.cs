// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Access.Organizations.SigningKeys;

/// <summary>Publishes what organization signing keys could not do: be minted by the background pass, or sign a message.</summary>
public interface IOrganizationSigningKeyTelemetry
{
    /// <summary>Records that an organization due a key got none because the deployment configures no key ring.</summary>
    /// <remarks>Counted once per organization per pass, so it keeps rising for as long as the ring stays absent rather than recording the fact once.</remarks>
    void MintRefusedWithoutKeyRing();

    /// <summary>Records that a message an organization's account sent left unsigned, because the organization held no key that could sign it.</summary>
    /// <remarks>
    /// Counted once per send the outbox is asked to queue, before it knows whether the request is new: a repeat of a
    /// request already queued, and an enqueue that then fails to commit, count again although no further message
    /// leaves. It rises with the mail an organization sends for as long as nothing signs it.
    /// </remarks>
    void MessageSentUnsigned();
}
