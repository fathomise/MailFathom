// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Emails.Signatures;

/// <summary>What one step of a signature verification found.</summary>
/// <remarks>
/// A reader is shown the steps rather than only the outcome they add up to, so a step that could not run is kept apart
/// from a step nothing reached: the first says the network or the key was missing, the second that an earlier step
/// already decided the question.
/// </remarks>
public enum SignatureCheckResult
{
    /// <summary>The step ran and passed.</summary>
    Passed = 0,

    /// <summary>The step ran and failed.</summary>
    Failed = 1,

    /// <summary>The step could not run: there was no key to check with, or no answer to check against.</summary>
    NotChecked = 2,

    /// <summary>The step was not reached, because an earlier step already decided it could not pass.</summary>
    Skipped = 3,
}
