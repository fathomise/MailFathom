// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Spam.Model;

/// <summary>What one account decided about asking a decision model whether its mail is spam.</summary>
/// <remarks>
/// <para>
/// All three are the account's, because each moves what a verdict about its mail says, and each joins the profile a
/// verdict is recorded under for that reason. Where the model is not consulted the other two say nothing, so
/// <see cref="NotConsulted" /> carries the defaults rather than values somebody chose.
/// </para>
/// <para>
/// Naming the subcategory of mail the deterministic stage already called spam is a switch of its own because it is the
/// half that costs most: it is a call per junk message, and junk is often most of a mailbox.
/// </para>
/// </remarks>
public sealed record SpamModelStageSettings
{
    /// <summary>The threshold an account that states none is judged against.</summary>
    /// <remarks>
    /// The point at which the model's spam probability makes spam more likely than not. It is a starting point rather
    /// than a calibration: a threshold belongs to one model build, and the evaluation's per-subcategory figures are what
    /// an operator moves it from.
    /// </remarks>
    public const double DefaultThreshold = 0.5;

    private SpamModelStageSettings(bool isConsulted, bool namesSubcategoriesOfDeterministicSpam, double threshold)
    {
        this.IsConsulted = isConsulted;
        this.NamesSubcategoriesOfDeterministicSpam = namesSubcategoriesOfDeterministicSpam;
        this.Threshold = threshold;
    }

    /// <summary>Gets the settings of an account that does not consult the model.</summary>
    public static SpamModelStageSettings NotConsulted { get; } = new(
        isConsulted: false,
        namesSubcategoriesOfDeterministicSpam: false,
        DefaultThreshold);

    /// <summary>Gets whether the model is asked about this account's mail at all.</summary>
    public bool IsConsulted { get; }

    /// <summary>Gets whether the model also names the subcategory of a verdict the deterministic stage reached.</summary>
    public bool NamesSubcategoriesOfDeterministicSpam { get; }

    /// <summary>Gets the spam probability at or above which the model's judgement is a spam verdict.</summary>
    public double Threshold { get; }

    /// <summary>Builds the settings of an account that consults the model.</summary>
    /// <param name="namesSubcategoriesOfDeterministicSpam">Whether the model also names the subcategory of deterministic spam.</param>
    /// <param name="threshold">The spam probability at or above which the model's judgement is a spam verdict.</param>
    /// <returns>The settings.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="threshold" /> is not a probability above zero.</exception>
    /// <remarks>
    /// Zero is refused because a threshold of zero calls every message spam, which no operator means and the profile
    /// refuses for the same reason.
    /// </remarks>
    public static SpamModelStageSettings Consulted(bool namesSubcategoriesOfDeterministicSpam, double threshold)
    {
        if (!double.IsFinite(threshold) || threshold is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(threshold),
                threshold,
                "A model threshold is a probability above zero and at most one.");
        }

        return new SpamModelStageSettings(isConsulted: true, namesSubcategoriesOfDeterministicSpam, threshold);
    }
}
