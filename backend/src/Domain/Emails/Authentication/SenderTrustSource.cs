// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>Which source recognized an author, of those this deployment asks in order.</summary>
/// <remarks>
/// A verdict that says only <em>trusted</em> cannot be reviewed. The organization's signature, a contact the
/// organization keeps, the owner's own address or domain, and a contact a person wrote down are different statements,
/// and a contact is the one worth being able to find again, which is why a contact's verdict also names the contact.
/// The numbers <c>2</c> and <c>3</c> named two trusted-sender lists that contacts replaced, and are never reused.
/// </remarks>
public enum SenderTrustSource
{
    /// <summary>Nothing recognized the author, which is what every <see cref="SenderTrustLevel.Unknown" /> verdict carries.</summary>
    None = 0,

    /// <summary>The author's domain is the domain of an account the receiving account's owner holds, and the receiving account opted into those domains.</summary>
    /// <remarks>
    /// Every one of the owner's accounts' domains counts and not only the receiving one, because a person holding a
    /// work mailbox and a personal one holds one person's correspondence. It is off unless an account asks for it,
    /// because on a large shared provider the owner's domain is every stranger's domain too.
    /// </remarks>
    OwnAccountDomain = 1,

    /// <summary>A signature made with a key of the receiving account's organization proved the author a member of it.</summary>
    /// <remarks>
    /// It ranks first and needs no authenticated author domain, because the signature proves the address itself rather
    /// than a domain: a colleague on a server that publishes no DKIM key is still proven, and one on the organization's
    /// own domain is named as a member rather than by that domain.
    /// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0042-signing-an-organizations-mail-and-trusting-a-sender-its-signature-proves.md">ADR 0042</see>
    /// states the three conditions it is granted under.
    /// </remarks>
    OrganizationMember = 4,

    /// <summary>The author wrote as exactly one of the addresses the receiving account's owner holds.</summary>
    /// <remarks>
    /// It is mail from the owner to themselves, so it needs no configuration. It is held to the rule a contact is: the
    /// author was authenticated, or signed the message themselves, and <c>From</c> shows that address.
    /// </remarks>
    OwnAccountAddress = 5,

    /// <summary>A contact the receiving account's organization keeps holds the author's address.</summary>
    /// <remarks>
    /// Asked right after organization membership, so a partner the whole organization deals with is reported as the
    /// organization's rather than as one member's.
    /// </remarks>
    OrganizationContact = 6,

    /// <summary>A contact that a user the receiving account is assigned to wrote down holds the author's address.</summary>
    /// <remarks>Asked last, so a sender any source above also recognizes is reported under that one.</remarks>
    PersonalContact = 7,

    /// <summary>The author's address is the address of a member of the receiving account's organization, as the organization's member directory reads when the message is read.</summary>
    /// <remarks>
    /// Asked right after the organization's signature and before the organization's contacts, because a member's record
    /// is the authoritative description of a colleague. It counts as an address an administrator wrote down counts, so
    /// it never stands in for the signature: a colleague whose mail the signature does not cover is recognized here,
    /// under authentication, and nowhere above.
    /// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0044-an-organizations-member-directory-as-a-projection-of-user-records.md">ADR 0044</see>
    /// states which addresses are a member's.
    /// </remarks>
    OrganizationDirectory = 8,
}
