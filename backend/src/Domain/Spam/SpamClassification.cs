// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Domain.Spam;

/// <summary>What classification concluded about one stored message occurrence, and everything it concluded it from.</summary>
/// <remarks>
/// <para>
/// This is derived data of the same kind as an embedding: computed from a local copy, never mirrored back to the mail
/// server, and never a statement about which folder the message is in or which flags it carries. It inherits the
/// classification, retention, access, and erasure constraints of the message it describes, and it is reachable by
/// whatever reaches that message — the record hangs off the occurrence rather than living beside it.
/// </para>
/// <para>
/// It carries no subject, no address, and no body. What a signal's observation can carry is a sending domain and an
/// authentication outcome, which is why nothing here is loggable and only counts, kinds, scores, and rule names may be
/// reported in telemetry. The subcategory and the phishing detail are closed values rather than text, and they are
/// still a judgement about one person's message, so they are not reported beside its identifier either.
/// </para>
/// <para>
/// The spam subcategory is not a mail category. It says what kind of spam a spam message is, and nothing about mail that
/// is not spam.
/// </para>
/// </remarks>
public sealed record SpamClassification
{
    /// <summary>The greatest number of signals one classification records.</summary>
    /// <remarks>
    /// A message can carry several authentication results and a provider can write several headers, so the bound is
    /// generous against both while keeping one message's derived data bounded. Whatever produces more signals than this
    /// keeps the first of them, because a record that refused the message outright would lose the verdict as well.
    /// </remarks>
    public const int MaximumSignals = 64;

    private SpamClassification(
        StoredEmailId emailId,
        SpamVerdict verdict,
        SpamClassificationStage decidedBy,
        SpamAssessment? assessment,
        SpamClassificationProfile profile,
        IReadOnlyList<SpamSignal> signals,
        DateTimeOffset evaluatedAt,
        SpamSubcategory? subcategory,
        SpamPhishingRequest? phishingRequest,
        SpamImpersonatedParty? impersonatedParty)
    {
        this.EmailId = emailId;
        this.Verdict = verdict;
        this.DecidedBy = decidedBy;
        this.Assessment = assessment;
        this.Profile = profile;
        this.Signals = signals;
        this.EvaluatedAt = evaluatedAt;
        this.Subcategory = subcategory;
        this.PhishingRequest = phishingRequest;
        this.ImpersonatedParty = impersonatedParty;
    }

    /// <summary>Gets the stored occurrence this classification is about.</summary>
    public StoredEmailId EmailId { get; }

    /// <summary>Gets what the classification concluded.</summary>
    public SpamVerdict Verdict { get; }

    /// <summary>Gets which stage reached the verdict.</summary>
    public SpamClassificationStage DecidedBy { get; }

    /// <summary>Gets the score and the threshold it was judged against, or <see langword="null" /> when no stage produced a number.</summary>
    public SpamAssessment? Assessment { get; }

    /// <summary>Gets the terms the verdict was reached under, or the unspecified value for a record that names none.</summary>
    /// <remarks>
    /// MailFathom's identity for the terms it decided under. It is what makes "already decided under the terms in force
    /// now" answerable from the record rather than guessed at from its age, which is what a run over a whole mailbox
    /// skips mail on. A record written before the profile was part of one names none.
    /// </remarks>
    public SpamClassificationProfile Profile { get; }

    /// <summary>Gets every fact the verdict rests on, in the order the stages produced them.</summary>
    public IReadOnlyList<SpamSignal> Signals { get; }

    /// <summary>Gets when the classification was evaluated.</summary>
    public DateTimeOffset EvaluatedAt { get; }

    /// <summary>Gets what kind of spam the message is, or <see langword="null" /> where no stage named one.</summary>
    /// <remarks>
    /// Present only on a <see cref="SpamVerdict.Spam" /> verdict, and only where the model or the person named it. A spam
    /// verdict without one is whole: it is what the deterministic stage reaches on its own.
    /// </remarks>
    public SpamSubcategory? Subcategory { get; }

    /// <summary>Gets which stage named the subcategory, or <see langword="null" /> where there is none.</summary>
    /// <remarks>
    /// Derived rather than recorded, because the deciding stage already settles it: the person names the subcategory of
    /// their own verdict, and every other subcategory is the model's — including one it named for a verdict the
    /// deterministic stage reached, which is the case where this and <see cref="DecidedBy" /> differ.
    /// </remarks>
    public SpamClassificationStage? SubcategoryNamedBy => StageNamingSubcategory(this.DecidedBy, this.Subcategory);

    /// <summary>Gets what a phishing message asks its reader for, or <see langword="null" /> where the model named nothing.</summary>
    public SpamPhishingRequest? PhishingRequest { get; }

    /// <summary>Gets the kind of party a phishing or business-email-compromise message pretends to come from, or <see langword="null" /> where the model named nothing.</summary>
    public SpamImpersonatedParty? ImpersonatedParty { get; }

    /// <summary>Records what a stage concluded about one occurrence.</summary>
    /// <param name="emailId">The stored occurrence classified.</param>
    /// <param name="verdict">What was concluded.</param>
    /// <param name="decidedBy">Which stage reached the verdict.</param>
    /// <param name="assessment">The score and threshold, or <see langword="null" /> when no number was produced.</param>
    /// <param name="profile">The terms the verdict was reached under, or the unspecified value for a record that names none.</param>
    /// <param name="signals">The facts the verdict rests on.</param>
    /// <param name="evaluatedAt">When the classification was evaluated.</param>
    /// <param name="subcategory">What kind of spam the message is, or <see langword="null" /> where no stage named one.</param>
    /// <param name="phishingRequest">What a phishing message asks for, or <see langword="null" /> where the model named nothing.</param>
    /// <param name="impersonatedParty">The kind of party the message pretends to come from, or <see langword="null" /> where the model named nothing.</param>
    /// <returns>The classification, holding no more than <see cref="MaximumSignals" /> signals.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="signals" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the verdict, the stage, or a named value is not a defined member.</exception>
    /// <exception cref="ArgumentException">Thrown when the record would break one of the invariants the remarks list.</exception>
    /// <remarks>
    /// An unspecified profile is accepted rather than refused, because a record written before the profile was part of
    /// one has to be readable: refusing it here would make a row this system itself wrote unreadable to the code that
    /// replaced it.
    /// <para>
    /// What is refused is a record that says something no stage could have concluded: a subcategory on a verdict other
    /// than spam; a subcategory with no stage able to name it, which is any record but the person's that carries no
    /// model judgement among its kept signals; a verdict the model decided with no model judgement behind it; the person
    /// concluding <see cref="SpamVerdict.Undetermined" />, which is not a verdict anybody states; a phishing request on
    /// anything but <see cref="SpamSubcategory.Phishing" />; an impersonated party on anything but phishing and business
    /// email compromise; and either of the two on a subcategory the model did not name, since only the model names them.
    /// </para>
    /// </remarks>
    public static SpamClassification Create(
        StoredEmailId emailId,
        SpamVerdict verdict,
        SpamClassificationStage decidedBy,
        SpamAssessment? assessment,
        SpamClassificationProfile profile,
        IReadOnlyList<SpamSignal> signals,
        DateTimeOffset evaluatedAt,
        SpamSubcategory? subcategory = null,
        SpamPhishingRequest? phishingRequest = null,
        SpamImpersonatedParty? impersonatedParty = null)
    {
        ArgumentNullException.ThrowIfNull(signals);

        if (!Enum.IsDefined(verdict))
        {
            throw new ArgumentOutOfRangeException(
                nameof(verdict),
                verdict,
                "A classification concludes one of the verdicts this system reaches.");
        }

        if (!Enum.IsDefined(decidedBy))
        {
            throw new ArgumentOutOfRangeException(
                nameof(decidedBy),
                decidedBy,
                "A classification is reached by one of the stages this system runs.");
        }

        DefinedOrNull(subcategory, nameof(subcategory));
        DefinedOrNull(phishingRequest, nameof(phishingRequest));
        DefinedOrNull(impersonatedParty, nameof(impersonatedParty));

        SpamSignal[] keptSignals = [.. signals.Take(MaximumSignals)];
        var hasModelJudgement = keptSignals.Any(static signal => signal.Kind is SpamSignalKind.ModelJudgement);

        RefuseAVerdictNoStageCouldReach(verdict, decidedBy, hasModelJudgement);
        RefuseASubcategoryNoStageCouldName(verdict, decidedBy, subcategory, hasModelJudgement);
        RefuseDetailTheSubcategoryDoesNotCarry(decidedBy, subcategory, phishingRequest, impersonatedParty);

        return new SpamClassification(
            emailId,
            verdict,
            decidedBy,
            assessment,
            profile,
            keptSignals,
            evaluatedAt.ToUniversalTime(),
            subcategory,
            phishingRequest,
            impersonatedParty);
    }

    /// <summary>Answers whether this classification may replace one the named stage decided.</summary>
    /// <param name="recordedBy">The stage that decided the classification already recorded.</param>
    /// <returns><see langword="false" /> only where the recorded verdict is the person's and this one is not.</returns>
    /// <remarks>
    /// The person's own verdict outranks every stage, so only the person replaces it. This is the predicate the write
    /// is conditional on, read against the row inside the transaction that would replace it, so a person's verdict
    /// recorded after a stage began cannot be overwritten by that stage's result.
    /// </remarks>
    public bool MayReplace(SpamClassificationStage recordedBy) =>
        recordedBy is not SpamClassificationStage.Person || this.DecidedBy is SpamClassificationStage.Person;

    /// <summary>Answers which stage named a recorded subcategory, from the stage that decided the verdict it sits on.</summary>
    /// <param name="decidedBy">The stage that reached the verdict.</param>
    /// <param name="subcategory">The subcategory recorded beside it, or <see langword="null" /> where there is none.</param>
    /// <returns>The person for the person's own verdict, the model for every other subcategory, and <see langword="null" /> where none was named.</returns>
    /// <remarks>
    /// Public so a reading that projects a record's columns rather than rebuilding the record answers the question the
    /// same way <see cref="SubcategoryNamedBy" /> does.
    /// </remarks>
    public static SpamClassificationStage? StageNamingSubcategory(
        SpamClassificationStage decidedBy,
        SpamSubcategory? subcategory) => subcategory is null
        ? null
        : decidedBy is SpamClassificationStage.Person
            ? SpamClassificationStage.Person
            : SpamClassificationStage.Model;

    private static void DefinedOrNull<TValue>(TValue? value, string parameterName)
        where TValue : struct, Enum
    {
        if (value is { } defined && !Enum.IsDefined(defined))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                defined,
                "A classification names only values this system declares.");
        }
    }

    private static void RefuseAVerdictNoStageCouldReach(
        SpamVerdict verdict,
        SpamClassificationStage decidedBy,
        bool hasModelJudgement)
    {
        if (decidedBy is SpamClassificationStage.Model && !hasModelJudgement)
        {
            throw new ArgumentException(
                "A verdict the model decided carries the model judgement it rests on.",
                nameof(decidedBy));
        }

        if (decidedBy is SpamClassificationStage.Person && verdict is SpamVerdict.Undetermined)
        {
            throw new ArgumentException(
                "The person's own verdict is spam or not spam; undetermined is not a verdict anybody states.",
                nameof(verdict));
        }
    }

    private static void RefuseASubcategoryNoStageCouldName(
        SpamVerdict verdict,
        SpamClassificationStage decidedBy,
        SpamSubcategory? subcategory,
        bool hasModelJudgement)
    {
        if (subcategory is null)
        {
            return;
        }

        if (verdict is not SpamVerdict.Spam)
        {
            throw new ArgumentException("A subcategory is named only on a spam verdict.", nameof(subcategory));
        }

        if (decidedBy is not SpamClassificationStage.Person && !hasModelJudgement)
        {
            throw new ArgumentException(
                "A subcategory is named by the model or the person; the deterministic stage names none.",
                nameof(subcategory));
        }
    }

    private static void RefuseDetailTheSubcategoryDoesNotCarry(
        SpamClassificationStage decidedBy,
        SpamSubcategory? subcategory,
        SpamPhishingRequest? phishingRequest,
        SpamImpersonatedParty? impersonatedParty)
    {
        if (phishingRequest is null && impersonatedParty is null)
        {
            return;
        }

        if (subcategory is null || decidedBy is SpamClassificationStage.Person)
        {
            throw new ArgumentException(
                "Phishing detail is named only by the model, beside the subcategory it named.",
                phishingRequest is null ? nameof(impersonatedParty) : nameof(phishingRequest));
        }

        if (phishingRequest is not null && subcategory is not SpamSubcategory.Phishing)
        {
            throw new ArgumentException("What a message asks for is named only on phishing.", nameof(phishingRequest));
        }

        if (impersonatedParty is not null
            && subcategory is not (SpamSubcategory.Phishing or SpamSubcategory.BusinessEmailCompromise))
        {
            throw new ArgumentException(
                "An impersonated party is named only on phishing and business email compromise.",
                nameof(impersonatedParty));
        }
    }
}
