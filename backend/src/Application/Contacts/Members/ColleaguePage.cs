// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Contacts.Members;

/// <summary>One bounded page of a reader's colleagues, and where the walk continues.</summary>
/// <param name="Colleagues">The colleagues this page holds, ordered by the name's comparison form and then by the member's identity.</param>
/// <param name="NextCursor">The boundary the following page reads beyond, or <see langword="null" /> when this page reached the end of the directory.</param>
public sealed record ColleaguePage(IReadOnlyList<MemberContact> Colleagues, ContactCursor? NextCursor);
