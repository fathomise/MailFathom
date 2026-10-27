// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Failures;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.DecisionModels;

/// <summary>Raised when a decision request produced no answers, naming which kind of failure ended it.</summary>
/// <remarks>
/// An exception rather than a result, because the fact travels through every middleware a client is composed of before
/// it reaches the work that asked, and none of them can decide what it means for that work. The message names the
/// model's alias and the failure, and nothing the provider sent: an error body can quote the request, and a request
/// carries somebody's mail.
/// </remarks>
public sealed class DecisionFailedException : MailFathomException
{
    /// <summary>Initializes the failure with what caused it.</summary>
    /// <param name="modelAlias">The deployment's name for the model that was asked.</param>
    /// <param name="failure">What ended the request.</param>
    /// <param name="cause">The failure underneath, kept for diagnosis.</param>
    public DecisionFailedException(string modelAlias, DecisionFailure failure, Exception cause)
        : base(DescribeFailure(modelAlias, failure), cause)
    {
        this.ModelAlias = modelAlias;
        this.Failure = failure;
    }

    /// <summary>Initializes the failure.</summary>
    /// <param name="modelAlias">The deployment's name for the model that was asked.</param>
    /// <param name="failure">What ended the request.</param>
    public DecisionFailedException(string modelAlias, DecisionFailure failure)
        : base(DescribeFailure(modelAlias, failure))
    {
        this.ModelAlias = modelAlias;
        this.Failure = failure;
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => this.Failure switch
    {
        DecisionFailure.CredentialRejected => MailFathomErrorCode.DecisionProviderCredentialRejected,
        DecisionFailure.AnswerUnreadable => MailFathomErrorCode.DecisionAnswerUnreadable,
        _ => MailFathomErrorCode.DecisionProviderUnavailable,
    };

    /// <summary>Gets the deployment's name for the model that was asked.</summary>
    public string ModelAlias { get; }

    /// <summary>Gets what ended the request.</summary>
    public DecisionFailure Failure { get; }

    /// <summary>Gets the tokens the provider reported for the request, when it reported any before the failure.</summary>
    /// <remarks>
    /// Present only on an answer refused as unreadable whose usage could still be read: the model ran and the provider
    /// billed it, so the period and the run are charged for it as they would be for a readable answer. Absent on every
    /// other failure, because a request that never reached a model was never billed.
    /// </remarks>
    public UsageDetails? Usage { get; init; }

    /// <summary>Gets whether the same request may succeed if it is sent again later.</summary>
    /// <remarks>An unreadable answer is not, because the same model asked the same question answers in the same shape.</remarks>
    public bool IsWorthRepeating => this.Failure
        is DecisionFailure.RateLimited
        or DecisionFailure.RequestTimedOut
        or DecisionFailure.TransportFaulted;

    private static string DescribeFailure(string modelAlias, DecisionFailure failure) => failure switch
    {
        DecisionFailure.CredentialRejected =>
            $"Decision model '{modelAlias}' refused the configured credential. Rotate or correct it; repeating the request cannot change the answer.",
        DecisionFailure.RateLimited =>
            $"Decision model '{modelAlias}' refused the request because the deployment is over its allowed rate.",
        DecisionFailure.RequestTimedOut =>
            $"Decision model '{modelAlias}' did not answer within the time configured for one request.",
        DecisionFailure.TransportFaulted =>
            $"Decision model '{modelAlias}' could not be reached, or its answer was cut off.",
        DecisionFailure.RequestRefused =>
            $"Decision model '{modelAlias}' rejected the request itself. Check the declared model, the account's balance, and the size of what was sent.",
        DecisionFailure.AnswerUnreadable =>
            $"Decision model '{modelAlias}' answered with something other than one probability for every option each question offered.",
        _ => $"Decision model '{modelAlias}' produced no answers.",
    };
}
