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

    /// <summary>
    /// Records that a message an organization's account sent left unsigned: the organization held no active key, its
    /// private half did not open, the message named no signable author domain, or the signature would have carried it
    /// past the deployment's size bound.
    /// </summary>
    /// <remarks>
    /// Counted each time a send's transmitted form is composed unsigned: once per transmission attempt, so a send
    /// deferred and attempted again counts again, and once more each time its sent copy is derived for filing. It
    /// rises with the mail an organization sends for as long as nothing signs it, and is not a count of messages.
    /// </remarks>
    void MessageSentUnsigned();
}
