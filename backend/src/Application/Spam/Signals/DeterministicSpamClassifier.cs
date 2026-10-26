// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;

namespace MailFathom.Application.Spam.Signals;

/// <summary>Reaches a verdict from what a message already carried, with no network and no model.</summary>
/// <remarks>
/// <para>
/// This is the whole of the working feature. Mail that reaches an IMAP mailbox has usually been scored already: the
/// receiving server recorded SPF, DKIM, and DMARC outcomes in <c>Authentication-Results</c>, ARC preserved them across
/// the forwarding hops where the first two legitimately break, the provider wrote its own verdict into an
/// <c>X-Spam-*</c> header, and where it decided the message was junk it filed it in the folder it advertises for that.
/// Every one of those is a fact from the moment it mattered, with network context nothing after delivery has.
/// </para>
/// <para>
/// What decides the verdict is deliberately narrower than what is recorded. A provider's own verdict decides, and a
/// junk-folder placement or a junk keyword outranks it because that is a decision somebody already acted on — a person's
/// <c>$NotJunk</c> above all of them. An authentication failure
/// does not: a DMARC failure is something the receiving server saw and chose to deliver anyway, and turning it into a
/// spam verdict here would file mail the operator's own provider decided to accept. It is recorded as its own signal so
/// a reader can see it, which is what the record is for.
/// </para>
/// </remarks>
public sealed class DeterministicSpamClassifier
{
    /// <summary>The header the receiving server writes its own outcomes into.</summary>
    private const string AuthenticationResultsField = "Authentication-Results";

    /// <summary>The header an ARC set preserves a previous hop's outcomes in.</summary>
    private const string ForwardedAuthenticationResultsField = "ARC-Authentication-Results";

    /// <summary>The word <see cref="ProviderSpamHeaderFields.SpamFlag" /> carries for a spam verdict.</summary>
    private const string AffirmativeFlag = "YES";

    /// <summary>The word <see cref="ProviderSpamHeaderFields.SpamStatus" /> opens with for a spam verdict.</summary>
    private const string AffirmativeStatus = "Yes";

    /// <summary>The word <see cref="ProviderSpamHeaderFields.SpamStatus" /> opens with for a clean verdict.</summary>
    private const string NegativeStatus = "No";

    private const string ScoreProperty = "score=";

    private const string ThresholdProperty = "required=";

    /// <summary>Reads a verdict out of one message's headers, its junk keywords, and where the mailbox filed it.</summary>
    /// <param name="facts">What the message's headers carried.</param>
    /// <param name="folderAlias">MailFathom's own name for the folder the occurrence was stored from.</param>
    /// <param name="isJunkFolder">Whether that folder is the one the account advertises as its junk folder.</param>
    /// <param name="keywords">The keywords the message carries in its mailbox.</param>
    /// <param name="ownActs">What this deployment's own verdicts already did to the message, none of which is evidence.</param>
    /// <returns>The verdict, the assessment when a source carried both numbers, and every observed fact.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="facts" />, <paramref name="keywords" />, or <paramref name="ownActs" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Nothing here reads the body, and nothing here writes. The same facts produce the same reading whenever it runs,
    /// which is what lets a classification be recomputed for an occurrence without the message being fetched again.
    /// </para>
    /// <para>
    /// The order is the order of who said it. <c>$NotJunk</c> is the person's own correction and decides
    /// <see cref="SpamVerdict.NotSpam" /> over everything else. <c>$Junk</c> or <c>$Phishing</c> somebody else set, and a
    /// junk-folder placement somebody else made, decide <see cref="SpamVerdict.Spam" />, because each is a decision already
    /// acted on. A provider's header decides only where none of those spoke. <c>$Junk</c> and <c>$NotJunk</c> together
    /// cancel, as RFC 9051 requires, and the verdict is reached as if no junk keyword were there.
    /// </para>
    /// <para>
    /// A keyword or a placement this deployment's verdict produced is left out entirely, signal and all. Counting it would
    /// turn a model's verdict into a deterministic one on the next run, and changing the model, its threshold, or its
    /// instructions could then never undo it.
    /// </para>
    /// </remarks>
    public DeterministicSpamReading Read(
        SpamHeaderFacts facts,
        MailFolderAlias folderAlias,
        bool isJunkFolder,
        RemoteEmailKeywords keywords,
        IReadOnlySet<SpamVerdictAct> ownActs)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(keywords);
        ArgumentNullException.ThrowIfNull(ownActs);

        var providerVerdict = ProviderVerdict(facts.ProviderHeaders);
        var marks = MarksSetByOthers(keywords, ownActs);
        var isPlacedByOthers = isJunkFolder && !ownActs.Contains(SpamVerdictAct.FiledIntoJunk);

        IReadOnlyList<SpamSignal> signals =
        [
            .. marks.Select(static keyword => SpamSignal.Create(
                SpamSignalKind.JunkKeyword,
                keyword,
                observation: null,
                SpamSignalProvenance.FromMailboxKeyword(keyword))),
            .. PlacementSignals(folderAlias, isPlacedByOthers),
            .. AuthenticationSignals(facts.AuthenticationResults),
            .. ProviderSignals(facts.ProviderHeaders),
        ];

        var verdict = KeywordVerdict(marks) switch
        {
            SpamVerdict.Undetermined when isPlacedByOthers => SpamVerdict.Spam,
            SpamVerdict.Undetermined => providerVerdict.Verdict,
            var decided => decided,
        };

        return new DeterministicSpamReading(verdict, providerVerdict.Assessment, signals);
    }

    /// <summary>Reads the junk keywords the message carries that somebody other than this deployment's verdict set.</summary>
    private static IReadOnlyList<string> MarksSetByOthers(RemoteEmailKeywords keywords, IReadOnlySet<SpamVerdictAct> ownActs)
    {
        (string Keyword, SpamVerdictAct? WrittenAs)[] junkKeywords =
        [
            (JunkKeywords.NotJunk, null),
            (JunkKeywords.Junk, SpamVerdictAct.MarkedJunk),
            (JunkKeywords.Phishing, SpamVerdictAct.MarkedPhishing),
        ];

        return
        [
            .. junkKeywords
                .Where(candidate => JunkKeywords.Carries(keywords, candidate.Keyword))
                .Where(candidate => candidate.WrittenAs is not { } act || !ownActs.Contains(act))
                .Select(static candidate => candidate.Keyword),
        ];
    }

    /// <summary>Reads what the junk keywords somebody else set decide, which is nothing where they cancel or are absent.</summary>
    private static SpamVerdict KeywordVerdict(IReadOnlyList<string> marks)
    {
        var junk = marks.Contains(JunkKeywords.Junk);

        if (marks.Contains(JunkKeywords.NotJunk))
        {
            return junk ? SpamVerdict.Undetermined : SpamVerdict.NotSpam;
        }

        return junk || marks.Contains(JunkKeywords.Phishing) ? SpamVerdict.Spam : SpamVerdict.Undetermined;
    }

    private static IEnumerable<SpamSignal> PlacementSignals(MailFolderAlias folderAlias, bool isJunkFolder)
    {
        if (!isJunkFolder)
        {
            return [];
        }

        return
        [
            SpamSignal.Create(
                SpamSignalKind.JunkFolderPlacement,
                folderAlias.Value,
                observation: null,
                SpamSignalProvenance.FromFolderPlacement(folderAlias.Value)),
        ];
    }

    private static IEnumerable<SpamSignal> AuthenticationSignals(
        IReadOnlyList<MessageAuthenticationResult> results) => results.Select(static result => SpamSignal.Create(
            result.IsForwarded
                ? SpamSignalKind.ForwardedSenderAuthentication
                : SpamSignalKind.SenderAuthentication,
            result.Method,
            result.Detail is { Length: > 0 }
                ? string.Concat(result.Result, " ", result.Detail)
                : result.Result,
            SpamSignalProvenance.FromMessageHeader(result.IsForwarded
                ? ForwardedAuthenticationResultsField
                : AuthenticationResultsField)));

    private static IEnumerable<SpamSignal> ProviderSignals(IReadOnlyList<ProviderSpamHeaderValue> headers) =>
        headers.Select(static header => SpamSignal.Create(
            SpamSignalKind.ProviderSpamVerdict,
            header.FieldName,
            header.Value,
            SpamSignalProvenance.FromMessageHeader(header.FieldName)));

    /// <summary>Reads the verdict and, where one source carried both numbers, the assessment the provider reached.</summary>
    /// <remarks>
    /// <para>
    /// The flag is read before the status because it is the unambiguous one: a single word with two accepted values,
    /// where the status field's grammar is a verdict followed by properties whose order and presence vary by server.
    /// A message carrying both and disagreeing with itself is answered by the flag.
    /// </para>
    /// <para>
    /// An assessment is recorded only when <see cref="ProviderSpamHeaderFields.SpamStatus" /> carries the score and the
    /// threshold together. A score without a threshold is a number in an unknown scale — a value of 6 is ordinary mail
    /// under one server's configuration and spam under another's — and pairing it with a threshold from anywhere else
    /// would produce a comparison that reads as a measurement and is not one. The score is still
    /// recorded, as the signal the header itself is.
    /// </para>
    /// </remarks>
    private static (SpamVerdict Verdict, SpamAssessment? Assessment) ProviderVerdict(
        IReadOnlyList<ProviderSpamHeaderValue> headers)
    {
        var flagVerdict = FlagVerdict(Value(headers, ProviderSpamHeaderFields.SpamFlag));
        var status = Value(headers, ProviderSpamHeaderFields.SpamStatus);
        var statusVerdict = StatusVerdict(status);

        var verdict = flagVerdict is SpamVerdict.Undetermined ? statusVerdict : flagVerdict;

        return (verdict, StatusAssessment(status));
    }

    private static string? Value(IReadOnlyList<ProviderSpamHeaderValue> headers, string fieldName) => headers
        .FirstOrDefault(header => StringComparer.OrdinalIgnoreCase.Equals(header.FieldName, fieldName))
        ?.Value;

    private static SpamVerdict FlagVerdict(string? flag) => flag?.Trim() switch
    {
        null or "" => SpamVerdict.Undetermined,
        var value when StringComparer.OrdinalIgnoreCase.Equals(value, AffirmativeFlag) => SpamVerdict.Spam,
        _ => SpamVerdict.NotSpam,
    };

    /// <summary>Reads the verdict word the status field opens with, before its first comma or space.</summary>
    /// <remarks>
    /// Anything the field opens with that is neither word leaves the verdict undetermined rather than defaulting to
    /// either: an unrecognized grammar is a server this reading does not understand, and guessing would either file
    /// ordinary mail or publish a clean verdict nobody reached.
    /// </remarks>
    private static SpamVerdict StatusVerdict(string? status)
    {
        var verdictWord = status?.Trim().Split([',', ' '], 2)[0];

        return verdictWord switch
        {
            null or "" => SpamVerdict.Undetermined,
            var word when StringComparer.OrdinalIgnoreCase.Equals(word, AffirmativeStatus) => SpamVerdict.Spam,
            var word when StringComparer.OrdinalIgnoreCase.Equals(word, NegativeStatus) => SpamVerdict.NotSpam,
            _ => SpamVerdict.Undetermined,
        };
    }

    private static SpamAssessment? StatusAssessment(string? status)
    {
        if (status is null)
        {
            return null;
        }

        return PropertyNumber(status, ScoreProperty) is { } score
            && PropertyNumber(status, ThresholdProperty) is { } threshold
            ? SpamAssessment.Create(score, threshold)
            : null;
    }

    /// <summary>Reads one <c>name=value</c> number out of a status field, where the properties are space separated.</summary>
    private static double? PropertyNumber(string status, string propertyName)
    {
        var property = status
            .Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(part => part.StartsWith(propertyName, StringComparison.OrdinalIgnoreCase));

        return property is not null
            && double.TryParse(
                property.AsSpan(propertyName.Length),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var number)
            && double.IsFinite(number)
                ? number
                : null;
    }
}
