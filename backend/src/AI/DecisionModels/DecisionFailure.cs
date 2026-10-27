// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.AI.DecisionModels;

/// <summary>Names why a decision request produced no answers.</summary>
/// <remarks>
/// The provider-facing members mirror the chat failure's, because what an HTTP status means about the remote party does
/// not depend on what was asked of it. The last member is the decision's own: an answer that came back and could not be
/// read as one probability per offered option.
/// </remarks>
public enum DecisionFailure
{
    /// <summary>The provider refused the credential the deployment presented.</summary>
    CredentialRejected = 0,

    /// <summary>The provider refused the request because the deployment is over its allowed rate.</summary>
    RateLimited = 1,

    /// <summary>The request outlived the time allowed for it.</summary>
    RequestTimedOut = 2,

    /// <summary>The request never reached an answer: the endpoint was unreachable, the connection dropped, or the response was cut off.</summary>
    TransportFaulted = 3,

    /// <summary>The provider rejected the request itself, for a reason repeating it cannot change — an exhausted balance among them.</summary>
    RequestRefused = 4,

    /// <summary>The provider answered with something that is not one probability for every option each question offered.</summary>
    AnswerUnreadable = 5,
}
