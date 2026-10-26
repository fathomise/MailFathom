// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MailFathom.Application.Spam.Model;
using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Rules;

namespace MailFathom.Host.Configuration.Spam;

/// <summary>Whether one account's mail is put to a decision model, and on which terms.</summary>
/// <remarks>
/// <para>
/// The account's, because each of the three moves what a verdict about its mail says, and each joins the profile a
/// verdict is recorded under: changing any of them re-decides the account's mail on the next classification run.
/// </para>
/// <para>
/// <b>Switching it on sends mail to two third parties</b> — the router and the provider it picks — which is why it is off
/// unless a record says otherwise. ADR 0041 decides what is sent: the sender's address and display name, a differing
/// <c>Reply-To</c> address, the subject, the plain-text body cut to the deployment's bound, each link's hosts, each
/// attachment's declared type and name, and the authentication outcomes and the deployment's trust in the sender.
/// </para>
/// <para>
/// The model stage is an AI function, so every folder it classifies must be one AI may read: an account whose stage is on
/// while a classified folder is mapped <c>VisibleToAI: false</c> is refused, from whichever side the write came.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The configuration binder materializes this type when a user's document is read.")]
internal sealed class MailAccountSpamModelStageOptions
{
    /// <summary>The property the account's spam classification block holds this under.</summary>
    internal const string BlockProperty = "ModelStage";

    /// <summary>The path an account's record holds the stage's switch under, as a refusal names it.</summary>
    internal const string EnabledPath = $"{MailAccountSpamClassificationOptions.RecordProperty}:{BlockProperty}:{nameof(Enabled)}";

    /// <summary>Gets or sets whether a decision model is asked about this account's mail.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets whether the model also names the subcategory of mail the deterministic stage already called spam.</summary>
    /// <remarks>
    /// Off by default, because it costs a call per junk message and junk is often most of a mailbox. With it off, a spam
    /// verdict the deterministic stage reached carries no subcategory, and the model is asked only about the mail the mail
    /// server let through.
    /// </remarks>
    public bool NameSubcategoriesOfDeterministicSpam { get; set; }

    /// <summary>Gets or sets the spam probability at or above which the model's judgement is a spam verdict.</summary>
    /// <remarks>
    /// A threshold belongs to one model build: probabilities from two models on the same message are not comparable, so a
    /// threshold moved with the model rather than kept from the last one. The evaluation's per-subcategory figures and its
    /// false-positive rate on legitimate mail are what to set it from.
    /// </remarks>
    public double Threshold { get; set; } = SpamModelStageSettings.DefaultThreshold;

    /// <summary>Builds the settings the stage reads, or none where the model is not consulted.</summary>
    /// <param name="classificationEnabled">Whether the account classifies at all.</param>
    /// <returns>The settings.</returns>
    internal SpamModelStageSettings ToSettings(bool classificationEnabled) => classificationEnabled && this.Enabled
        ? SpamModelStageSettings.Consulted(this.NameSubcategoriesOfDeterministicSpam, this.Threshold)
        : SpamModelStageSettings.NotConsulted;

    /// <summary>Finds everything about this block the account's own record can be refused over.</summary>
    /// <param name="classificationEnabled">Whether the account switched classification on at all.</param>
    /// <param name="classifiedAliases">The folders the account's mail is classified over, already resolved to its inbox where it named none.</param>
    /// <param name="account">The account this block was written on, whose folder mappings the classified folders are judged against.</param>
    /// <returns>One result per refusal, each naming the setting that carries it.</returns>
    internal IEnumerable<ValidationResult> FindRefusals(
        bool classificationEnabled,
        IReadOnlyCollection<MailFolderAlias> classifiedAliases,
        DeclaredMailAccount account)
    {
        if (!double.IsFinite(this.Threshold) || this.Threshold is <= 0 or > 1)
        {
            yield return new ValidationResult(
                $"The account record sets {MailAccountSpamClassificationOptions.RecordProperty}:{BlockProperty}:{nameof(this.Threshold)} to {this.Threshold.ToString(CultureInfo.InvariantCulture)}, and a threshold is a probability above 0 and at most 1.",
                [nameof(this.Threshold)]);
        }

        if (!this.Enabled)
        {
            yield break;
        }

        if (!classificationEnabled)
        {
            yield return new ValidationResult(
                $"The account record switches {EnabledPath} on while {nameof(MailAccountSpamClassificationOptions.Enabled)} is false, and nothing would be classified for the model to judge. Set it to true, or switch the model stage off.",
                [nameof(this.Enabled)]);

            yield break;
        }

        foreach (var withheld in account.MappedFolders
            .Where(folder => !folder.IsVisibleToAI && classifiedAliases.Contains(folder.Alias))
            .Select(static folder => folder.Alias.Value))
        {
            yield return new ValidationResult(
                $"The account record switches {EnabledPath} on while folder '{withheld}' of account '{account.AccountId}', which its mail is classified over, is mapped with VisibleToAI false. The model stage is an AI function and never reads a folder withheld from AI: map the folder VisibleToAI true, take it out of ScannedFolders, or switch the model stage off.",
                [nameof(this.Enabled)]);
        }
    }
}
