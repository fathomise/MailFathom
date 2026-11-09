// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MailFathom.Application.Jobs.Scheduling;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>Declares when the nightly retention walk runs.</summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class MailRetentionOptions
{
    /// <summary>The configuration path this block is bound from, used to name a faulty setting.</summary>
    internal const string SectionPath = $"{MailSynchronizationOptions.SectionName}:{nameof(MailSynchronizationOptions.Retention)}";

    /// <summary>Gets or sets the occasions a walk is dispatched on, in the schedule syntax the job queue reads.</summary>
    /// <remarks>Without a zone a daily time is read in UTC, so an operator names one where the night is somewhere else.</remarks>
    public string Schedule { get; set; } = "Daily at 02:00";

    /// <summary>Reports every reason the walk could not be run as declared.</summary>
    /// <returns>One message per faulty setting, each naming its configuration path.</returns>
    public IEnumerable<string> FindConfigurationErrors()
    {
        if (!JobRecurrence.TryParse(this.Schedule, out _, out var scheduleError))
        {
            yield return string.Format(CultureInfo.InvariantCulture, "{0}:{1} {2}", SectionPath, nameof(this.Schedule), scheduleError);
        }
    }

    /// <summary>Builds the recurrence the walk is dispatched on.</summary>
    /// <returns>The recurrence.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the declaration was not validated first.</exception>
    public JobRecurrence ToRecurrence() => JobRecurrence.TryParse(this.Schedule, out var recurrence, out _)
        ? recurrence!
        : throw new InvalidOperationException(
            "The retention schedule names no recurrence this system runs, which configuration validation reports before composition reaches this.");
}
