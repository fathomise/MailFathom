// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Contacts.Failures;
using MailFathom.Application.Portraits;
using MailFathom.Application.Presence;
using MailFathom.Application.StoredFiles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Presence;

namespace MailFathom.Application.Contacts.Members;

/// <summary>Reads the signed-in user's colleagues — the other people in their organization — and copies one into their own book.</summary>
/// <remarks>
/// <para>
/// The directory itself is <see cref="IMemberDirectory" />'s; what this use case adds is what a reader owes and the
/// directory does not: the grant, the organization the reader belongs to, leaving the reader out of their own
/// directory, and the precedence of the reader's own book. A reader in no organization has no colleagues, which is an
/// empty page rather than a refusal.
/// </para>
/// <para>
/// <b>The reader's own book comes first.</b> A colleague one of whose addresses the reader wrote down is listed under
/// the reader's own record of them and left out here, which is the order
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0044-an-organizations-member-directory-as-a-projection-of-user-records.md">ADR 0044</see>
/// § <em>Where it sits</em> states. The identity reads do not apply it, because a caller naming a member already chose
/// whom to read.
/// </para>
/// <para>
/// Nothing writes a member contact. Promotion writes a copy of one into the reader's own book through
/// <see cref="ContactBookWriter" />, so it is refused for every reason a record a person types is refused — a colleague
/// with no address among them.
/// </para>
/// <para>Nothing here logs: every field of a colleague is personal data about them.</para>
/// </remarks>
public sealed class ColleagueBook
{
    /// <summary>How many colleagues are checked against the reader's own book in one round.</summary>
    /// <remarks>Small enough that their addresses rarely need more than one lookup, and large enough that a page rarely needs more than a handful.</remarks>
    private const int OwnBookCheckSize = 16;

    private readonly IMemberDirectory members;
    private readonly IContactDirectory contacts;
    private readonly IStoredFileStore files;
    private readonly ContactBookWriter writer;
    private readonly ContactBookOwnership ownership;
    private readonly AccessAuthorization authorization;
    private readonly IPresenceStore presence;
    private readonly TimeProvider time;

    /// <summary>Initializes the use case.</summary>
    /// <param name="members">Reads the organization's member directory.</param>
    /// <param name="contacts">Answers which addresses the reader's own book holds.</param>
    /// <param name="files">Holds the portraits the colleagues are drawn by.</param>
    /// <param name="writer">Writes the copy a promotion makes.</param>
    /// <param name="ownership">Answers whose books this caller reads, and which organization they belong to.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <param name="presence">Holds what each colleague's presence is worked out from.</param>
    /// <param name="time">The clock presence is read at.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public ColleagueBook(
        IMemberDirectory members,
        IContactDirectory contacts,
        IStoredFileStore files,
        ContactBookWriter writer,
        ContactBookOwnership ownership,
        AccessAuthorization authorization,
        IPresenceStore presence,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(contacts);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(time);

        this.members = members;
        this.contacts = contacts;
        this.files = files;
        this.writer = writer;
        this.ownership = ownership;
        this.authorization = authorization;
        this.presence = presence;
        this.time = time;
    }

    /// <summary>Serves one bounded page of the reader's colleagues.</summary>
    /// <param name="request">What the caller asked the page for; its origin is not read, because a colleague has none.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, ordered by name, with the boundary a following one reads beyond.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the reading grant.</exception>
    /// <exception cref="ContactQueryInvalidException">Thrown when the page size or the search text is not one the book serves.</exception>
    /// <exception cref="ContactCursorMalformedException">Thrown when the cursor is not one this system issued.</exception>
    /// <remarks>
    /// The search matches the name or an address, as it does on the contact book, and the walk is ordered by the
    /// same comparison form. The order is taken in process rather than by PostgreSQL, so a cursor from here continues
    /// this walk and no other.
    /// </remarks>
    public async Task<ColleaguePage> ReadPageAsync(ContactPageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        this.authorization.RequirePermission(MailFathomPermission.ContactsRead);

        var query = ContactBookReader.QueryFrom(request with { Origin = null });
        var scope = await this.ownership.ReadScopeAsync(cancellationToken);

        var candidates = (await this.ReadColleaguesAsync(scope, cancellationToken))
            .Where(colleague => query.Search is not { } search || Matches(colleague, search))
            .OrderBy(colleague => colleague.DisplayName.SortKey, StringComparer.Ordinal)
            .ThenBy(colleague => colleague.Member.Value)
            .Where(colleague => query.Cursor is not { } cursor || IsBeyond(colleague, cursor))
            .ToArray();

        var visible = await this.LeaveOutHeldInOwnBookAsync(
            scope.OwnBook,
            candidates,
            query.PageSize + 1,
            cancellationToken);
        MemberContact[] page = [.. visible.Take(query.PageSize)];

        return new ColleaguePage(
            await this.WithPresenceAsync(page, cancellationToken),
            visible.Count > query.PageSize
                ? ContactCursor.After(page[^1].DisplayName, ContactId.Create(page[^1].Member.Value))
                : null);
    }

    /// <summary>Reads one colleague by the member's user identifier.</summary>
    /// <param name="member">The member to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The colleague with their presence, or <see langword="null" /> where the reader's organization holds no such person, or the identifier is the reader's own.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the reading grant.</exception>
    public async Task<Colleague?> FindAsync(UserId member, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.ContactsRead);

        return await this.FindColleagueAsync(member, cancellationToken) is { } colleague
            ? (await this.WithPresenceAsync([colleague], cancellationToken))[0]
            : null;
    }

    /// <summary>Reads where one colleague sits in the reader's organization: the managers above them and their direct reports.</summary>
    /// <param name="member">The member whose structure is read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The structure, or <see langword="null" /> where the reader's organization holds no such person, or the identifier is the reader's own.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the reading grant.</exception>
    /// <remarks>
    /// Composed out of the whole directory rather than one member, because who reports to a member is every member naming
    /// them. The reader is not left out of it: they may be the colleague's manager or report to them, and leaving them out
    /// would cut the line. Nothing about the structure reaches MCP, which reads the directory as a contact book alone.
    /// </remarks>
    public async Task<ColleagueStructure?> ReadStructureAsync(UserId member, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.ContactsRead);

        var scope = await this.ownership.ReadScopeAsync(cancellationToken);

        if (scope.OrganizationBook?.Organization is not { } organization || member == scope.User)
        {
            return null;
        }

        var directory = (await this.members.ReadAsync(organization, cancellationToken))
            .ToDictionary(colleague => colleague.Member);

        if (!directory.ContainsKey(member))
        {
            return null;
        }

        var managers = directory.Values
            .Where(colleague => colleague.Manager is not null)
            .ToDictionary(colleague => colleague.Member, colleague => colleague.Manager!.Value);

        MemberContact[] above =
        [
            .. ReportingLine.ManagersAbove(member, managers)
                .TakeWhile(directory.ContainsKey)
                .Select(manager => directory[manager])
                .Reverse(),
        ];
        MemberContact[] reports =
        [
            .. directory.Values
                .Where(colleague => colleague.Manager == member)
                .OrderBy(colleague => colleague.DisplayName.SortKey, StringComparer.Ordinal)
                .ThenBy(colleague => colleague.Member.Value),
        ];

        return new ColleagueStructure(above, reports, scope.User);
    }

    /// <summary>Reads the portrait one colleague is drawn by.</summary>
    /// <param name="member">The member whose portrait is read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The portrait, or <see langword="null" /> where there is no such colleague, they have no portrait, or its octets are no image kind this build stores.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the reading grant.</exception>
    /// <remarks>The file is the member's own, which is why only a member of the reader's organization is ever looked up: an identifier naming anybody else answers as no colleague before any file is read.</remarks>
    public async Task<Portrait?> ReadPortraitAsync(UserId member, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.ContactsRead);

        if (await this.FindColleagueAsync(member, cancellationToken) is not { Portrait: { } portrait } colleague)
        {
            return null;
        }

        return await this.files.ReadAsync(colleague.Member, portrait, cancellationToken) is { } content
            ? Portrait.Of(content)
            : null;
    }

    /// <summary>Writes a copy of one colleague into the reader's own book.</summary>
    /// <param name="member">The member to copy.</param>
    /// <param name="writtenThrough">The surface the caller wrote through, which every address of the copy records.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The copy as written, not found where there is no such colleague, or the contact of the reader's own book already holding one of the colleague's addresses.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the writing grant.</exception>
    /// <exception cref="ContactRecordInvalidException">Thrown when the colleague does not form a contact the book admits, which is a colleague with no address.</exception>
    /// <remarks>
    /// The copy carries the name, the addresses, the preferred address, and the phone numbers. It is the reader's from
    /// then on and keeps what it held when it was made, and it hides the colleague from the reader's directory by the
    /// precedence of the own book.
    /// </remarks>
    public async Task<ContactWriteResult> PromoteAsync(
        UserId member,
        ContactAddressWriterKind writtenThrough,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.ContactsWrite);

        if (await this.FindColleagueAsync(member, cancellationToken) is not { } colleague)
        {
            return ContactWriteResult.NotFound();
        }

        return await this.writer.RecordAsync(
            new ContactRecordDraft
            {
                DisplayName = colleague.DisplayName.Value,
                Addresses = [.. colleague.Addresses.Select(address => address.Address)],
                PreferredAddress = colleague.PreferredAddress?.Address,
                PhoneNumbers =
                [
                    .. colleague.PhoneNumbers.Select(phoneNumber =>
                        new ContactPhoneNumberDraft(phoneNumber.Number, phoneNumber.Label.ToString())),
                ],
            },
            writtenThrough,
            cancellationToken);
    }

    /// <summary>Says whether a colleague carries the searched text in their name or in one of their addresses.</summary>
    private static bool Matches(MemberContact colleague, ContactSearch search) =>
        colleague.DisplayName.SortKey.Contains(search.ComparisonForm, StringComparison.Ordinal)
        || colleague.Addresses.Any(address =>
            address.NormalizedAddress.Contains(search.ComparisonForm, StringComparison.Ordinal));

    /// <summary>Says whether a colleague comes after the boundary a cursor names, in the order the page is walked.</summary>
    private static bool IsBeyond(MemberContact colleague, ContactCursor cursor)
    {
        var byName = string.CompareOrdinal(colleague.DisplayName.SortKey, cursor.DisplayNameSortKey);

        return byName > 0 || (byName == 0 && colleague.Member.Value.CompareTo(cursor.ContactId.Value) > 0);
    }

    /// <summary>Reads how each colleague reads to the other members, which is the only presence a reader is ever handed.</summary>
    private async Task<IReadOnlyList<Colleague>> WithPresenceAsync(
        IReadOnlyList<MemberContact> colleagues,
        CancellationToken cancellationToken)
    {
        var held = await this.presence.ReadAsync([.. colleagues.Select(colleague => colleague.Member)], cancellationToken);
        var now = this.time.GetUtcNow();

        return
        [
            .. colleagues.Select(colleague => new Colleague(
                colleague,
                held.GetValueOrDefault(colleague.Member, MemberPresence.Unknown).SeenByOthersAt(now))),
        ];
    }

    /// <summary>Reads one colleague of the caller's, without asking for a grant, and without composing anybody else.</summary>
    private async Task<MemberContact?> FindColleagueAsync(UserId member, CancellationToken cancellationToken)
    {
        var scope = await this.ownership.ReadScopeAsync(cancellationToken);

        if (scope.OrganizationBook?.Organization is not { } organization || member == scope.User)
        {
            return null;
        }

        return await this.members.FindAsync(organization, member, cancellationToken);
    }

    /// <summary>Reads every member of the reader's organization but the reader.</summary>
    private async Task<IReadOnlyList<MemberContact>> ReadColleaguesAsync(
        ContactBookScope scope,
        CancellationToken cancellationToken)
    {
        if (scope.OrganizationBook?.Organization is not { } organization)
        {
            return [];
        }

        var reader = scope.User;

        return
        [
            .. (await this.members.ReadAsync(organization, cancellationToken))
                .Where(colleague => colleague.Member != reader),
        ];
    }

    /// <summary>Keeps the colleagues none of whose addresses the reader's own book holds, until enough are kept.</summary>
    /// <remarks>
    /// Taken in rounds over the ordered candidates rather than over the whole directory, so a page costs the lookups
    /// its own candidates need instead of one per batch of everybody's addresses.
    /// </remarks>
    private async Task<IReadOnlyList<MemberContact>> LeaveOutHeldInOwnBookAsync(
        ContactBookHolder ownBook,
        IReadOnlyList<MemberContact> candidates,
        int wanted,
        CancellationToken cancellationToken)
    {
        var kept = new List<MemberContact>();

        foreach (var round in candidates.Chunk(OwnBookCheckSize))
        {
            var held = await this.HeldInAsync(ownBook, round, cancellationToken);

            kept.AddRange(round.Where(colleague => !colleague.Addresses.Any(held.Contains)));

            if (kept.Count >= wanted)
            {
                break;
            }
        }

        return kept;
    }

    /// <summary>Reads which addresses of some colleagues the reader's own book holds, in lookups no larger than one contact's addresses.</summary>
    private async Task<IReadOnlySet<EmailAddress>> HeldInAsync(
        ContactBookHolder ownBook,
        IEnumerable<MemberContact> colleagues,
        CancellationToken cancellationToken)
    {
        var held = new HashSet<EmailAddress>();

        foreach (var lookup in colleagues.SelectMany(colleague => colleague.Addresses).Distinct().Chunk(Contact.MaximumAddressCount))
        {
            held.UnionWith((await this.contacts.FindHoldersOfAsync(ownBook, lookup, cancellationToken)).Keys);
        }

        return held;
    }
}
