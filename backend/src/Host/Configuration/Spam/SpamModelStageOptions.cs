// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Spam.Model;

namespace MailFathom.Host.Configuration.Spam;

/// <summary>Bounds what the spam model stage spends, for every account that consults it.</summary>
/// <remarks>
/// <para>
/// The deployment's half of the model stage. Whether the model is asked, whether it names the subcategory of
/// deterministic spam, and its threshold are each account's, on its own record. What is here bounds a resource the
/// process bears: how many calls run at once, and how much of a message one call carries. The endpoint, the credential,
/// and the per-call timeout belong to the decision model's own block under <c>Decisions:Models</c>.
/// </para>
/// <para>
/// Both bounds hold with no key written, so a deployment that never names the block still runs the stage within them.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class SpamModelStageOptions
{
    /// <summary>The property the deployment's section holds this block under.</summary>
    internal const string SectionProperty = "ModelStage";

    /// <summary>The fewest body characters one call may be bounded to.</summary>
    internal const int MinimumBodyCharacters = 500;

    /// <summary>The most body characters one call may be bounded to.</summary>
    internal const int MaximumBodyCharacters = 100_000;

    /// <summary>The most calls one replica may be allowed to run at once.</summary>
    internal const int MaximumConcurrentCallsBound = 64;

    /// <summary>Gets or sets how many model calls one replica runs at once.</summary>
    /// <remarks>
    /// One replica's bound, so a deployment of several replicas runs this many on each. Calls past it wait for a slot
    /// rather than failing, which holds a classification job for as long as the wait lasts.
    /// </remarks>
    [Range(1, MaximumConcurrentCallsBound)]
    public int MaxConcurrentCalls { get; set; } = SpamModelStageBounds.DefaultMaximumConcurrentCalls;

    /// <summary>Gets or sets the most characters of a message's plain-text body one call carries.</summary>
    /// <remarks>
    /// The rest of the body is cut, and the model is told it was. A longer bound costs input tokens on every message and
    /// sends more of somebody's mail; a shorter one hides what a long scam says after its first paragraphs.
    /// </remarks>
    [Range(MinimumBodyCharacters, MaximumBodyCharacters)]
    public int MaxBodyCharacters { get; set; } = SpamModelStageBounds.DefaultMaximumBodyCharacters;

    /// <summary>Turns the declared bounds into the ones the stage reads.</summary>
    /// <returns>The bounds.</returns>
    internal SpamModelStageBounds ToBounds() => new(this.MaxConcurrentCalls, this.MaxBodyCharacters);

    /// <summary>Finds every bound written outside its range, naming the key.</summary>
    /// <returns>One result per refusal.</returns>
    /// <remarks>Asked by the section around it, because the options framework does not validate a nested block's annotations on its own.</remarks>
    internal IEnumerable<ValidationResult> FindErrors()
    {
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(this, new ValidationContext(this), results, validateAllProperties: true);

        return results.Select(result => new ValidationResult(
            $"{SpamClassificationOptions.SectionName}:{SectionProperty}:{result.MemberNames.FirstOrDefault()} — {result.ErrorMessage}",
            [SectionProperty]));
    }
}
