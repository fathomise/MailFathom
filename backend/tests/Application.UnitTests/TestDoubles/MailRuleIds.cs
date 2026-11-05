// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.Rules;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Names a test rule's identifier after the rule, so a declaration and a rule arranged apart agree on it.</summary>
internal static class MailRuleIds
{
    /// <summary>Derives the identifier a test rule of this name carries.</summary>
    /// <param name="name">The rule's name.</param>
    /// <returns>The same identifier for the same name on every run.</returns>
    internal static MailRuleId For(string name) =>
        MailRuleId.Create(new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(name)).AsSpan(0, 16)));
}
