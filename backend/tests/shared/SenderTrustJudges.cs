// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Trust;
using MailFathom.Application.Mail;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails.Authentication;
using NSubstitute;

namespace MailFathom.TestSupport;

/// <summary>Builds the judge a storing path reaches each message's trust verdict through.</summary>
/// <remarks>
/// The judge is a concrete type rather than a port, so a test that is not about trust builds a real one over a policy
/// that states nothing: every message it stores is then judged unknown, exactly as a deployment with no owner's
/// addresses would judge it.
/// </remarks>
public static class SenderTrustJudges
{
    /// <summary>Builds a judge over the policies a test supplies, recognizing nobody where it supplies none.</summary>
    /// <param name="policies">Resolves each account's policy; every account recognizes nobody where none is given.</param>
    /// <returns>The judge.</returns>
    public static SenderTrustJudge Over(ISenderTrustPolicyReader? policies = null)
    {
        if (policies is null)
        {
            policies = Substitute.For<ISenderTrustPolicyReader>();
            policies.GetTrustPolicy(Arg.Any<MailAccountId>()).Returns(SenderTrustPolicy.RecognizingNobody);
        }

        return new SenderTrustJudge(policies);
    }
}
