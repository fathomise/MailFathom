// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Spam.Actions;

namespace MailFathom.Application.Spam.Following;

/// <summary>Decides one stored email again because another mail client changed its junk marks.</summary>
/// <remarks>
/// A job rather than a step of the run because the decision may call the model. Running it twice with one payload is
/// running it once: the same marks reach the same verdict, and the one change it may ask of the mailbox is asked once per
/// message. Unlike an arrival's classification it takes no other spam action, for the reason
/// <see cref="SpamActionRecorder.ClearJunkMarksAsync" /> gives.
/// </remarks>
public sealed class FollowJunkMarksHandler : IJobHandler
{
    private readonly EmailSpamClassifier classifier;
    private readonly SpamActionRecorder actionRecorder;

    /// <summary>Initializes the handler over the work one job describes.</summary>
    /// <param name="classifier">Decides the email again and records the verdict.</param>
    /// <param name="actionRecorder">Takes off the junk marks RFC 9051 reads as no statement.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public FollowJunkMarksHandler(EmailSpamClassifier classifier, SpamActionRecorder actionRecorder)
    {
        ArgumentNullException.ThrowIfNull(classifier);
        ArgumentNullException.ThrowIfNull(actionRecorder);

        this.classifier = classifier;
        this.actionRecorder = actionRecorder;
    }

    /// <inheritdoc />
    public JobType JobType => JobType.FollowJunkMarks;

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when the payload is not the contract this job type names.</exception>
    public async Task RunAsync(IJobPayload payload, CancellationToken cancellationToken)
    {
        if (payload is not FollowJunkMarksJobPayload change)
        {
            throw new ArgumentException(
                $"A '{JobType.FollowJunkMarks}' job carries a payload naming one stored email and when its marks changed.",
                nameof(payload));
        }

        var result = await this.classifier.FollowSourceAsync(
            change.ToAccountId(),
            change.ToStoredEmailId(),
            change.ChangeObservedAt,
            cancellationToken);

        if (result is { Outcome: SpamClassificationOutcome.Classified, Classification: { } classification })
        {
            await this.actionRecorder.ClearJunkMarksAsync(classification, cancellationToken);
        }
    }
}
