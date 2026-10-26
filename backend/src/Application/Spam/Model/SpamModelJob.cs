// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Spam.Model;

/// <summary>Names which of its two jobs the model stage asks the model to do, which the deterministic stage's verdict decides.</summary>
public enum SpamModelJob
{
    /// <summary>The deterministic stage reached no spam verdict, so the model decides whether the message is spam and, if it is, which kind.</summary>
    DecideVerdict = 0,

    /// <summary>The deterministic stage reached a spam verdict, which stands, and the model only names which kind of spam it is.</summary>
    NameSubcategory = 1,
}
