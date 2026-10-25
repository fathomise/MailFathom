// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.AI.Decisions;

/// <summary>Names the shape of answer a question asks for.</summary>
public enum DecisionQuestionKind
{
    /// <summary>A yes-or-no question, answered with the probability of each.</summary>
    YesNo = 0,

    /// <summary>One of several named options, answered with a probability for each.</summary>
    Choice = 1,

    /// <summary>One of several ordered levels, answered with a probability for each.</summary>
    Scale = 2,
}
