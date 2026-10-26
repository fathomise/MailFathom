// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Rules;

namespace MailFathom.Host.Configuration.Spam;

/// <summary>How one account's mail is classified, and what is to happen to the junk in it.</summary>
/// <remarks>
/// <para>
/// The account's half of <see cref="SpamClassificationOptions" />, and deliberately only that half. Junk is a judgement
/// about one mailbox and the actions it triggers write to that mailbox's mail server, so whether its mail is classified
/// at all, over which folders, and what becomes of the result belong to the account — including the
/// decision not to classify at all, which no deployment setting requires of it.
/// </para>
/// <para>
/// It is the account rather than each assigned user because the mail is one copy: a mailbox two people are assigned is
/// classified once, and a verdict that filed a message moved it for both of them whatever either would have asked for.
/// A deployment whose two people want a mailbox classified differently is describing two mailboxes.
/// </para>
/// <para>
/// What is not here is what costs the deployment a resource: how fast a run may commit, and how long the index may be
/// held back waiting on a verdict. Those stay
/// in the deployment's own section, and there is no key here that shadows one — an account record layers over nothing.
/// </para>
/// <para>
/// Every switch is off when the block is absent, so an account whose record states none of it is classified as nothing,
/// which is the same answer a deployment that configured nothing gives.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The configuration binder materializes this type when a user's document is read.")]
internal sealed class MailAccountSpamClassificationOptions
{
    /// <summary>The property an account's record holds this block under.</summary>
    internal const string RecordProperty = "SpamClassification";

    /// <summary>The path an account's record holds the junk actions under, as a refusal names it.</summary>
    internal const string ActionsPath = $"{RecordProperty}:{nameof(Actions)}";

    /// <summary>Gets or sets whether this account's mail is classified at all.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the folder aliases this account's mail is classified over, or <see langword="null" /> to take its inbox.</summary>
    /// <remarks>
    /// Nullable so that leaving the key out and writing an empty list stay distinguishable, which an <c>IList</c>-typed
    /// property could not express. Each alias is resolved within this account's own folders, so one only another
    /// account maps reaches no mail at all — the same answer a folder this deployment does not serve gets.
    /// </remarks>
    public string[]? ScannedFolders { get; set; }

    /// <summary>Gets or sets what happens to mail a verdict calls this account's junk.</summary>
    /// <remarks>
    /// Always present so that a record stating none of its keys still binds. Both of its switches are off, which is what
    /// keeps a classification derived data by default: a verdict is recorded and the mailbox is left exactly as it was.
    /// </remarks>
    public MailAccountSpamActionOptions Actions { get; set; } = new();

    /// <summary>Gets or sets whether this account's mail is put to a decision model, and on which terms.</summary>
    /// <remarks>Always present, so a record stating none of its keys binds with the model stage off.</remarks>
    public MailAccountSpamModelStageOptions ModelStage { get; set; } = new();

    /// <summary>Finds everything about this block that would otherwise be discovered on the account's own mail.</summary>
    /// <param name="account">The account this block was written on, which is the only one a folder may resolve within.</param>
    /// <returns>One result per refusal, each naming the setting that carries it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="account" /> is <see langword="null" />.</exception>
    internal IEnumerable<ValidationResult> FindRefusals(DeclaredMailAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return this.FindPostureRefusals()
            .Concat(this.Actions.FindRefusals(this.Enabled, account))
            .Concat(this.ModelStage.FindRefusals(this.Enabled, this.ClassifiedAliasesOf(account), account));
    }

    /// <summary>Resolves the folders this block classifies, within the account it was written on.</summary>
    /// <remarks>
    /// The same answer the settings reader gives: the aliases written, or every folder the account maps to its inbox
    /// where none were. An alias that is not usable is refused above and selects nothing here.
    /// </remarks>
    private IReadOnlyCollection<MailFolderAlias> ClassifiedAliasesOf(DeclaredMailAccount account) => this.ScannedFolders is { } written
        ? [.. written.Select(static alias => MailFolderAlias.TryCreate(alias, out var usable) ? usable : (MailFolderAlias?)null).OfType<MailFolderAlias>()]
        : [.. account.MappedFolders.Where(static folder => folder.Role is MailFolderSpecialUse.Inbox).Select(static folder => folder.Alias)];

    private IEnumerable<ValidationResult> FindPostureRefusals() =>
        (this.ScannedFolders ?? [])
            .Where(static alias => !MailFolderAlias.TryCreate(alias, out _))
            .Select(alias => new ValidationResult(
                $"The account record names scanned folder '{alias}', which is not a usable folder alias.",
                [nameof(this.ScannedFolders)]));
}
