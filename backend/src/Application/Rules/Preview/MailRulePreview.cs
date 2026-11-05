// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Folders;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Editing;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Application.Rules.Facts;
using MailFathom.Application.Rules.Sets;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Rules.Preview;

/// <summary>Evaluates one rule over an account's stored mail and reports what it would match, without changing anything.</summary>
/// <remarks>
/// <para>
/// A rule acting on real mail is worth seeing before it runs, and a created rule is disabled for exactly that reason. A
/// preview evaluates a candidate a person is still writing, or a stored rule by its identifier, over the mail the
/// deployment holds for the account, and answers which messages match and what each would be asked for. It requests no
/// mutation, records no evaluation and no execution, and writes nothing at all.
/// </para>
/// <para>
/// It is bounded in what it reads and what it returns: one page reads at most <see cref="MaximumExaminedEmails" />
/// messages and reports at most <see cref="MaximumReportedMatches" />, and says where the next page continues, so a
/// preview over a large mailbox is a sequence of bounded requests rather than one walk of the whole of it.
/// </para>
/// <para>
/// What it reports names messages, so it asks for the audit grant over the account, as the history does.
/// </para>
/// </remarks>
public sealed class MailRulePreview
{
    /// <summary>The most messages one page of a preview evaluates the rule against.</summary>
    public const int MaximumExaminedEmails = 1_000;

    /// <summary>The most matching messages one page of a preview reports.</summary>
    public const int MaximumReportedMatches = 100;

    private const int BatchSize = 200;

    private readonly IMailRuleSetStore ruleSets;
    private readonly IMailRuleEvaluationStore mail;
    private readonly MailRuleSetValidator validator;
    private readonly IMailRuleConditionCompiler compiler;
    private readonly MailRuleSetEvaluator evaluator;
    private readonly IMailFolderMappingReader folderMappings;
    private readonly MailRuleConditionBounds bounds;
    private readonly AccessAuthorization authorization;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the preview.</summary>
    /// <param name="ruleSets">Keeps each account's rules, which a stored rule is read from.</param>
    /// <param name="mail">Reads the account's stored mail as an evaluation pass does.</param>
    /// <param name="validator">Judges a candidate before it is evaluated.</param>
    /// <param name="compiler">Reads the rule's condition.</param>
    /// <param name="evaluator">Evaluates the rule over one message.</param>
    /// <param name="folderMappings">Answers what the folder a message is in is configured for.</param>
    /// <param name="bounds">The condition bounds in force.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <param name="timeProvider">Supplies the instant each message is evaluated at.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public MailRulePreview(
        IMailRuleSetStore ruleSets,
        IMailRuleEvaluationStore mail,
        MailRuleSetValidator validator,
        IMailRuleConditionCompiler compiler,
        MailRuleSetEvaluator evaluator,
        IMailFolderMappingReader folderMappings,
        MailRuleConditionBounds bounds,
        AccessAuthorization authorization,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(ruleSets);
        ArgumentNullException.ThrowIfNull(mail);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentNullException.ThrowIfNull(folderMappings);
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.ruleSets = ruleSets;
        this.mail = mail;
        this.validator = validator;
        this.compiler = compiler;
        this.evaluator = evaluator;
        this.folderMappings = folderMappings;
        this.bounds = bounds;
        this.authorization = authorization;
        this.timeProvider = timeProvider;
    }

    /// <summary>Evaluates the rule over one page of the account's stored mail.</summary>
    /// <param name="request">The account, the rule, and where the page continues from.</param>
    /// <param name="cancellationToken">Cancels the preview.</param>
    /// <returns>What the rule would match on this page.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller is not granted <see cref="MailFathomPermission.AdminAuditRead" /> over the account.</exception>
    public async Task<MailRulePreviewResult> PreviewAsync(MailRulePreviewRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await this.authorization.RequirePermissionOverAsync(
            MailFathomPermission.AdminAuditRead,
            request.Account,
            cancellationToken);

        if (await this.FindDeclarationAsync(request, cancellationToken) is not { } declaration)
        {
            return MailRulePreviewResult.Unevaluated(MailRulePreviewOutcome.RuleNotFound, []);
        }

        var errors = this.validator.FindErrors(request.Account, [declaration], this.bounds);

        if (errors.Count > 0)
        {
            return MailRulePreviewResult.Unevaluated(MailRulePreviewOutcome.Refused, errors);
        }

        var ruleSet = StoredMailRuleSetSource.Compile(
            [declaration with { Enabled = true }],
            MailRuleSetRevision.Create([declaration with { Enabled = true }]),
            this.compiler,
            this.bounds);

        return await this.EvaluatePageAsync(request, ruleSet, cancellationToken);
    }

    private async Task<MailRuleDeclaration?> FindDeclarationAsync(MailRulePreviewRequest request, CancellationToken cancellationToken)
    {
        if (request.Candidate is { } candidate)
        {
            return candidate.Declare(MailRuleId.Mint(this.timeProvider.GetUtcNow()), enabled: true);
        }

        var stored = await this.ruleSets.ReadAsync(request.Account, cancellationToken);

        return stored.Find(request.StoredRule!.Value);
    }

    private async Task<MailRulePreviewResult> EvaluatePageAsync(
        MailRulePreviewRequest request,
        MailRuleSet ruleSet,
        CancellationToken cancellationToken)
    {
        var matches = new List<StoredEmailId>();
        var examined = 0;
        var failed = 0;
        var position = request.After;
        var mailRemains = true;

        while (mailRemains && examined < MaximumExaminedEmails && matches.Count < MaximumReportedMatches)
        {
            var requested = Math.Min(BatchSize, MaximumExaminedEmails - examined);
            var batch = await this.mail.GetStoredEmailsAsync(request.Account, position, requested, cancellationToken);
            var reachedMatchBound = false;

            foreach (var candidate in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var evaluation = await this.EvaluateAsync(ruleSet, candidate, cancellationToken);

                examined++;
                position = candidate.StoredEmailId;
                failed += evaluation.Evaluations.Count(rule => rule.Outcome == MailRuleOutcome.Failed);

                if (evaluation.Evaluations.Any(rule => rule.Outcome == MailRuleOutcome.Matched))
                {
                    matches.Add(candidate.StoredEmailId);

                    if (matches.Count == MaximumReportedMatches)
                    {
                        reachedMatchBound = true;

                        break;
                    }
                }
            }

            mailRemains = reachedMatchBound || batch.Count == requested;
        }

        return new MailRulePreviewResult(
            MailRulePreviewOutcome.Previewed,
            matches,
            ruleSet.Rules[0].Actions.Actions,
            examined,
            failed,
            mailRemains ? position : null,
            []);
    }

    private Task<MailRuleSetEvaluation> EvaluateAsync(
        MailRuleSet ruleSet,
        StoredEmailAwaitingRuleEvaluation candidate,
        CancellationToken cancellationToken)
    {
        var facts = new MailRuleFacts(
            candidate.Facts,
            new StoredEmailBodyTextReader(this.mail, candidate.StoredEmailId),
            this.folderMappings,
            this.timeProvider.GetUtcNow());

        return this.evaluator.EvaluateAsync(ruleSet, facts, MailRuleReach.EveryRule, cancellationToken);
    }
}
