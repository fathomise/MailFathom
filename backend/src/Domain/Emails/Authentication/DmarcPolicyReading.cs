// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>What the displayed domain's DMARC record asks a receiver to do with mail that fails, as far as it was read.</summary>
/// <remarks>
/// <para>
/// It is read for one purpose: a relaxed account recognizes a sender by their <c>From</c> address even where the
/// receiving server reported DMARC <c>fail</c>, but only where the domain itself publishes a monitoring-only policy —
/// its owner admitting that some of their legitimate mail does not pass. Anything else the record says about failures,
/// and anything nobody could read, keeps the failure a failure.
/// </para>
/// <para>
/// It is read once, when the message is stored, and kept with its authentication verdict, so a later judgment of the
/// same message — after a contact is written, or a re-derivation — never asks DNS again.
/// </para>
/// </remarks>
public enum DmarcPolicyReading
{
    /// <summary>No policy is known: nothing was looked up, the lookup found no record, or the record could not be read.</summary>
    NotEstablished = 0,

    /// <summary>Every policy tag that applies to the displayed domain reads <c>none</c>.</summary>
    MonitoringOnly = 1,

    /// <summary>A tag that applies asks for <c>quarantine</c> or <c>reject</c>, or states no policy this reading recognizes.</summary>
    Enforcing = 2,
}
