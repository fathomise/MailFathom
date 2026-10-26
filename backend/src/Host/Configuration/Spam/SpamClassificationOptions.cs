// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Spam;
using MailFathom.Application.Spam.Runs;
using MailFathom.Domain.Folders;

namespace MailFathom.Host.Configuration.Spam;

/// <summary>Configures whether and where mail is classified as spam.</summary>
/// <remarks>
/// <para>
/// Every switch here is off when the section is absent, so a deployment that configures nothing classifies nothing and
/// adds no path. The section binds successfully with none of its keys set, which is what makes
/// the feature genuinely opt-in rather than merely defaulted off.
/// </para>
/// <para>
/// It is bound strictly, so a misspelled key fails startup naming itself instead of being ignored while the operator
/// reads their own file as proof of something that is switched off.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class SpamClassificationOptions : IValidatableObject
{
    /// <summary>The configuration section this binds from.</summary>
    public const string SectionName = "SpamClassification";

    /// <summary>Gets or sets whether mail is classified at all.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the folder aliases classification runs over, or <see langword="null" /> to take each account's inbox.</summary>
    /// <remarks>
    /// Nullable so that leaving the key out and writing an empty list stay distinguishable, which an
    /// <c>IList</c>-typed property could not express: the binder builds an empty list for both. Absent is the default
    /// scope — every account's inbox mapping — and an explicitly empty list is an operator saying no folder, which is a
    /// legitimate way to switch the work off without switching the section off.
    /// </remarks>
    public string[]? ScannedFolders { get; set; }

    /// <summary>Gets or sets how long a message waits on a verdict before chunking, embedding, and rules run for it unclassified.</summary>
    /// <remarks>
    /// The bound on the ordering rather than a bound on classification. Derived work is withheld from a message until a
    /// verdict says it is not junk, so a classification that has stalled would otherwise stop the index too — and an
    /// index that quietly stops filling is worse than no ordering at all. What the wait buys is that spam is not
    /// embedded in the ordinary case; what it costs is that a deployment whose classification is wedged indexes without
    /// it after this long, and says so through <c>mailfathom.spam.derived_work.admissions</c>.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:01", "7.00:00:00")]
    public TimeSpan ClassificationWait { get; set; } = SpamClassificationScope.DefaultMaximumClassificationWait;

    /// <summary>Gets or sets how many stored occurrences one batch of an on-demand run classifies and commits.</summary>
    /// <remarks>
    /// A batch is the unit of progress an interrupted pass gives back. Nothing about it is a schedule: when a pass
    /// happens is the account's synchronization interval, and this is how wide one is allowed to be. Smaller than the
    /// rule pass's batch because the work per message is not comparable — a classification reads the stored message out
    /// of the content store and parses its headers.
    /// </remarks>
    [Range(1, 10_000)]
    public int RunBatchSize { get; set; } = 50;

    /// <summary>Gets or sets how many batches one pass may commit before leaving the rest to the next account run.</summary>
    /// <remarks>
    /// What bounds a pass rather than what bounds the run: a mailbox nobody has scored is walked over as many account
    /// runs as its size needs, instead of turning one run into a walk of its whole history while the folders that run
    /// exists to fetch wait behind it.
    /// </remarks>
    [Range(1, 1_000)]
    public int MaxRunBatchesPerPass { get; set; } = 4;

    /// <summary>Gets or sets what happens to mail a verdict calls junk.</summary>
    /// <remarks>
    /// Always present so that a deployment which states none of its keys still binds and validates. Both of its switches
    /// are off, which is what keeps a classification derived data by default: a verdict is recorded and the mailbox is
    /// left exactly as it was.
    /// </remarks>
    public SpamActionOptions Actions { get; set; } = new();

    /// <summary>Gets or sets what the spam model stage may spend, for every account that consults it.</summary>
    /// <remarks>Always present, so the stage runs within its default bounds where the deployment names none.</remarks>
    public SpamModelStageOptions ModelStage { get; set; } = new();

    /// <summary>Turns the declared limits into the bounds one pass of an on-demand run works under.</summary>
    /// <returns>The bounds.</returns>
    public SpamClassificationRunOptions ToRunOptions() => new()
    {
        BatchSize = this.RunBatchSize,
        MaxBatchesPerPass = this.MaxRunBatchesPerPass,
    };

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => this.FindErrors();

    /// <summary>Finds everything about this section that would otherwise be discovered on somebody's mail.</summary>
    internal IEnumerable<ValidationResult> FindErrors() =>
        FindScannedFolderErrors(this.ScannedFolders)
            .Concat(this.Actions.FindErrors(this.Enabled))
            .Concat(this.ModelStage.FindErrors());

    /// <summary>Refuses an alias that is not a value this system could have issued, naming the alias rather than the position.</summary>
    private static IEnumerable<ValidationResult> FindScannedFolderErrors(string[]? scannedFolders) =>
        (scannedFolders ?? [])
            .Where(static alias => !IsUsableAlias(alias))
            .Select(static alias => new ValidationResult(
                $"{SectionName} names scanned folder '{alias}', which is not a usable folder alias.",
                [nameof(ScannedFolders)]));

    private static bool IsUsableAlias(string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return false;
        }

        try
        {
            _ = MailFolderAlias.Create(alias);

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
