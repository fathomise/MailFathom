// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Contacts.Failures;

/// <summary>The failure raised when a member asks to change a contact their organization keeps.</summary>
/// <remarks>
/// <para>
/// An organization's book is written by its administrators alone, so a member reads its contacts and changes none of
/// them. The refusal says so rather than answering as a contact the member does not hold, because the member can see
/// the contact and has to be told why the change did not land: promoting it into their own book is what keeps a copy
/// they may change.
/// </para>
/// <para>
/// An exception rather than an outcome, because the refusal travels through the use case to every surface that reaches
/// it — the client answers it with a status and its code, and the MCP boundary with its code — and none of them acts
/// on it and continues.
/// </para>
/// </remarks>
public sealed class ContactReadOnlyException : MailFathomException
{
    /// <summary>Initializes the failure for a contact the caller's organization keeps.</summary>
    public ContactReadOnlyException()
        : base("This contact is kept by your organization, so it cannot be changed here. Promote it to keep a copy of your own.")
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.OrganizationContactReadOnly;
}
