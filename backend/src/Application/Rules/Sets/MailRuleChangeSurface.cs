// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Rules.Sets;

/// <summary>Which surface a committed change to an account's rules came through, as its archive row records it.</summary>
/// <remarks>
/// The client API adds its own value when it starts writing rules; MCP never will, because no MCP tool reads or changes
/// a rule.
/// </remarks>
public enum MailRuleChangeSurface
{
    /// <summary>The administrative endpoint, which <c>mfctl</c> reaches.</summary>
    Administration = 0,
}
