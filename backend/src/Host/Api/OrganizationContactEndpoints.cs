// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Access.Organizations;
using MailFathom.Application.Accounts;
using MailFathom.Application.Contacts;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves one organization's contact book: the people its administrators wrote down for every member.</summary>
/// <remarks>
/// <para>
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0043-how-a-sender-is-verified-and-trusted-and-contacts-as-the-one-trusted-sender-list.md">ADR 0043</see>
/// makes this surface and <c>mfctl contact … --organization</c> the only writers of that book: what every member's
/// mailbox recognizes is an administrative act, so neither the client endpoint nor MCP reaches these routes. Every
/// member reads the book beside their own on every surface that reads one, which is <see cref="ContactEndpoints" />'
/// listing for an administrator and the client's and the agent's for the member.
/// </para>
/// <para>
/// The grants are the user's book's: reading and exporting are <c>mailfathom.admin.audit.read</c>, recording, amending,
/// and promoting are <c>mailfathom.admin.operate</c>, and erasing is <c>mailfathom.admin.erase</c>, each held at the
/// organization's scope or the deployment's. An organization outside the caller's scope is answered as one the book
/// holds nothing for, and a write naming one is refused as an organization the deployment does not hold, so the answer
/// does not say whether it exists.
/// </para>
/// <para>
/// Nothing a contact carries reaches a refusal, for the reason <see cref="ContactEndpoints" /> gives.
/// </para>
/// </remarks>
internal static class OrganizationContactEndpoints
{
    /// <summary>The route an organization's book is listed and written to, relative to the administrative prefix.</summary>
    internal const string OrganizationContactsRoute = $"{OrganizationEndpoints.OrganizationRoute}/contacts";

    /// <summary>The route one contact of an organization's book is read, amended, and erased at.</summary>
    internal const string OrganizationContactRoute = $"{OrganizationContactsRoute}/{{contactId:guid}}";

    /// <summary>The route the contact of an organization's book holding an address is read from.</summary>
    internal const string OrganizationContactByAddressRoute = $"{OrganizationContactsRoute}/by-address";

    /// <summary>The route a mailbox's collected contact is promoted into an organization's book at.</summary>
    internal const string OrganizationContactPromotionRoute = $"{OrganizationContactRoute}/promotion";

    /// <summary>The route everything an organization's book holds about one person is exported from.</summary>
    internal const string OrganizationContactExportRoute = $"{OrganizationContactRoute}/export";

    /// <summary>The origin every write to an organization's book acts under, because it holds people somebody wrote down.</summary>
    private const ContactOrigin AdministrativeWriter = ContactOrigin.Asserted;

    /// <summary>Maps the organization contact routes into the administrative group, so they inherit its authorization.</summary>
    /// <param name="api">The administrative route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapOrganizationContacts(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(OrganizationContactsRoute, ListAsync)
            .RequirePermissionOverTarget(MailFathomPermission.AdminAuditRead);

        api.MapPost(OrganizationContactsRoute, RecordAsync)
            .WithMetadata(new RequestSizeLimitAttribute(ContactEndpoints.MaxRecordRequestBytes))
            .RequirePermissionOverTarget(MailFathomPermission.AdminOperate);

        api.MapGet(OrganizationContactByAddressRoute, FindByAddressAsync)
            .RequirePermissionOverTarget(MailFathomPermission.AdminAuditRead);

        api.MapGet(OrganizationContactRoute, FindAsync)
            .RequirePermissionOverTarget(MailFathomPermission.AdminAuditRead);

        api.MapPut(OrganizationContactRoute, AmendAsync)
            .WithMetadata(new RequestSizeLimitAttribute(ContactEndpoints.MaxRecordRequestBytes))
            .RequirePermissionOverTarget(MailFathomPermission.AdminOperate);

        api.MapDelete(OrganizationContactRoute, EraseAsync)
            .RequirePermissionOverTarget(MailFathomPermission.AdminErase);

        api.MapPost(OrganizationContactPromotionRoute, PromoteAsync)
            .RequirePermissionOverTarget(MailFathomPermission.AdminOperate);

        api.MapGet(OrganizationContactExportRoute, ExportAsync)
            .RequirePermissionOverTarget(MailFathomPermission.AdminAuditRead);
    }

    /// <summary>Serves one bounded page of an organization's book, or reports what was wrong with the request.</summary>
    /// <param name="organizationId">The organization whose book is read.</param>
    /// <param name="pageSize">How many contacts the page may hold, or <see langword="null" /> for the default.</param>
    /// <param name="cursor">The cursor the previous page returned, or <see langword="null" /> for the first page.</param>
    /// <param name="book">Reads the page.</param>
    /// <param name="authorization">Answers whether the caller's scope covers the organization, which is otherwise answered as one whose book holds nothing.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the page, or <c>400</c> naming what was wrong with the request.</returns>
    internal static async Task<Results<Ok<ContactPageResponse>, ProblemHttpResult>> ListAsync(
        Guid organizationId,
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] ContactBook book,
        [FromServices] AccessAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(authorization);

        if (organizationId == Guid.Empty)
        {
            return EmptyOrganization();
        }

        ContactCursor? decodedCursor = null;

        if (cursor is not null && !ContactCursor.TryDecode(cursor, out decodedCursor))
        {
            return ContactEndpoints.Refused("The continuation cursor is not one this deployment issued.");
        }

        ContactQuery query;

        try
        {
            query = ContactQuery.Create(origin: null, search: null, pageSize, decodedCursor);
        }
        catch (ArgumentOutOfRangeException)
        {
            return ContactEndpoints.Refused($"A contact page holds between 1 and {ContactQuery.MaximumPageSize} contacts.");
        }

        if (!await Covers(authorization, MailFathomPermission.AdminAuditRead, organizationId, cancellationToken))
        {
            return TypedResults.Ok(new ContactPageResponse([], NextCursor: null));
        }

        var page = await book.ReadPageAsync(ScopeOf(organizationId), query, cancellationToken);

        return TypedResults.Ok(new ContactPageResponse(
            [.. page.Contacts.Select(ContactResponse.For)],
            page.NextCursor?.Encode()));
    }

    /// <summary>Records a person an organization's book does not yet hold.</summary>
    /// <param name="context">The request, whose administrator every address records as its writer.</param>
    /// <param name="organizationId">The organization whose book the person is written into.</param>
    /// <param name="request">The record to write.</param>
    /// <param name="book">Performs the write.</param>
    /// <param name="organizations">Answers whether this deployment holds the named organization.</param>
    /// <param name="authorization">Answers whether the caller's scope covers the organization, which is otherwise refused as one this deployment does not hold.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the outcome, or <c>400</c> naming which rule the record broke.</returns>
    internal static async Task<Results<Ok<ContactWriteResponse>, ProblemHttpResult>> RecordAsync(
        HttpContext context,
        Guid organizationId,
        [FromBody] ContactRecordRequest? request,
        [FromServices] ContactBook book,
        [FromServices] IOrganizationStore organizations,
        [FromServices] AccessAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);

        if (await RefuseWriterAsync(
                organizationId,
                MailFathomPermission.AdminOperate,
                organizations,
                authorization,
                cancellationToken) is { } refusal)
        {
            return refusal;
        }

        var read = ContactEndpoints.ReadRecord(request);

        if (read.Record is not { } record)
        {
            return ContactEndpoints.Refused(read.Refusal!);
        }

        var written = await book.RecordInOrganizationAsync(
            organizationId,
            new NewContact
            {
                DisplayName = record.DisplayName,
                Addresses = record.Addresses,
                PreferredAddress = record.PreferredAddress,
                Note = record.Note,
                Origin = AdministrativeWriter,
            },
            ContactEndpoints.AdministratorWriting(context),
            cancellationToken);

        return TypedResults.Ok(ContactWriteResponse.For(written));
    }

    /// <summary>Reads one contact of an organization's book by the identity the book gave it.</summary>
    /// <param name="organizationId">The organization whose book is read.</param>
    /// <param name="contactId">The contact to read.</param>
    /// <param name="book">Answers what the book holds.</param>
    /// <param name="authorization">Answers whether the caller's scope covers the organization, which is otherwise answered as one whose book holds nothing.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the contact, or <c>200</c> with none where the book holds no such person.</returns>
    internal static async Task<Results<Ok<ContactLookupResponse>, ProblemHttpResult>> FindAsync(
        Guid organizationId,
        Guid contactId,
        [FromServices] ContactBook book,
        [FromServices] AccessAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(authorization);

        if (organizationId == Guid.Empty)
        {
            return EmptyOrganization();
        }

        if (!ContactEndpoints.TryReadContactId(contactId, out var identity))
        {
            return ContactEndpoints.EmptyIdentity();
        }

        if (!await Covers(authorization, MailFathomPermission.AdminAuditRead, organizationId, cancellationToken))
        {
            return TypedResults.Ok(new ContactLookupResponse(Contact: null));
        }

        var held = await book.FindAsync(ScopeOf(organizationId), identity, cancellationToken);

        return TypedResults.Ok(new ContactLookupResponse(held is null ? null : ContactResponse.For(held)));
    }

    /// <summary>Reads the contact of an organization's book holding one address.</summary>
    /// <param name="organizationId">The organization whose book is read.</param>
    /// <param name="address">The address to resolve, compared without regard to case.</param>
    /// <param name="book">Answers what the book holds.</param>
    /// <param name="authorization">Answers whether the caller's scope covers the organization, which is otherwise answered as one whose book holds nothing.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the contact, <c>200</c> with none where nobody holds it, or <c>400</c> where the address is not one.</returns>
    internal static async Task<Results<Ok<ContactLookupResponse>, ProblemHttpResult>> FindByAddressAsync(
        Guid organizationId,
        [FromQuery] string? address,
        [FromServices] ContactBook book,
        [FromServices] AccessAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(authorization);

        if (organizationId == Guid.Empty)
        {
            return EmptyOrganization();
        }

        if (!ContactEndpoints.TryReadAddress(address, out var resolved))
        {
            return ContactEndpoints.Refused("The lookup names no usable address.");
        }

        if (!await Covers(authorization, MailFathomPermission.AdminAuditRead, organizationId, cancellationToken))
        {
            return TypedResults.Ok(new ContactLookupResponse(Contact: null));
        }

        var held = await book.FindByAddressAsync(ScopeOf(organizationId), resolved, cancellationToken);

        return TypedResults.Ok(new ContactLookupResponse(held is null ? null : ContactResponse.For(held)));
    }

    /// <summary>Amends one contact of an organization's book to the record the caller states.</summary>
    /// <param name="context">The request, whose administrator an address the amendment adds records as its writer.</param>
    /// <param name="organizationId">The organization whose book holds the contact.</param>
    /// <param name="contactId">The contact to amend.</param>
    /// <param name="request">The record the contact is to have afterwards.</param>
    /// <param name="book">Performs the write.</param>
    /// <param name="organizations">Answers whether this deployment holds the named organization.</param>
    /// <param name="authorization">Answers whether the caller's scope covers the organization, which is otherwise refused as one this deployment does not hold.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the outcome, or <c>400</c> naming which rule the record broke.</returns>
    internal static async Task<Results<Ok<ContactWriteResponse>, ProblemHttpResult>> AmendAsync(
        HttpContext context,
        Guid organizationId,
        Guid contactId,
        [FromBody] ContactRecordRequest? request,
        [FromServices] ContactBook book,
        [FromServices] IOrganizationStore organizations,
        [FromServices] AccessAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);

        if (await RefuseWriterAsync(
                organizationId,
                MailFathomPermission.AdminOperate,
                organizations,
                authorization,
                cancellationToken) is { } refusal)
        {
            return refusal;
        }

        if (!ContactEndpoints.TryReadContactId(contactId, out var identity))
        {
            return ContactEndpoints.EmptyIdentity();
        }

        var read = ContactEndpoints.ReadRecord(request);

        if (read.Record is not { } record)
        {
            return ContactEndpoints.Refused(read.Refusal!);
        }

        var amended = await book.AmendAsync(
            ScopeOf(organizationId),
            new ContactAmendment
            {
                ContactId = identity,
                Writer = AdministrativeWriter,
                DisplayName = record.DisplayName,
                Addresses = record.Addresses,
                PreferredAddress = record.PreferredAddress,
                Note = record.Note,
            },
            ContactEndpoints.AdministratorWriting(context),
            cancellationToken);

        return TypedResults.Ok(ContactWriteResponse.For(amended));
    }

    /// <summary>Promotes a contact one mail account collected into an organization's book.</summary>
    /// <param name="context">The request, whose administrator every address of the copy records as its writer.</param>
    /// <param name="organizationId">The organization whose book the copy is written into.</param>
    /// <param name="contactId">The collected contact to promote.</param>
    /// <param name="account">The mail account whose collected book holds the contact.</param>
    /// <param name="book">Performs the write.</param>
    /// <param name="organizations">Answers whether this deployment holds the named organization.</param>
    /// <param name="accounts">Reports the accounts this deployment serves, which the named one is resolved against.</param>
    /// <param name="placements">Answers which organization the named account belongs to.</param>
    /// <param name="authorization">Answers whether the caller's scope covers the organization and the account.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the outcome and no record, including for a contact that was already asserted.</returns>
    /// <remarks>
    /// The route names the account the record is copied out of, because an organization reads no collected book of its
    /// own: a collected record is a mailbox's, and the administrator promoting it has to be able to reach that mailbox.
    /// The mailbox has to be one of the organization's own as well, and one of another organization's is refused exactly as
    /// one the deployment does not serve: otherwise an administrator covering two organizations could hand every member of
    /// one a correspondent learned from the other's mail. The copy is a new record in the organization's book, and the
    /// mailbox keeps its own.
    /// </remarks>
    internal static async Task<Results<Ok<ContactWriteResponse>, ProblemHttpResult>> PromoteAsync(
        HttpContext context,
        Guid organizationId,
        Guid contactId,
        [FromQuery] string? account,
        [FromServices] ContactBook book,
        [FromServices] IOrganizationStore organizations,
        [FromServices] IDeploymentMailAccountCatalog accounts,
        [FromServices] IAdministrativeTargets placements,
        [FromServices] AccessAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(placements);

        if (await RefuseWriterAsync(
                organizationId,
                MailFathomPermission.AdminOperate,
                organizations,
                authorization,
                cancellationToken) is { } refusal)
        {
            return refusal;
        }

        if (!ContactEndpoints.TryReadContactId(contactId, out var identity))
        {
            return ContactEndpoints.EmptyIdentity();
        }

        if (await AdminAccountRequest.ResolveCoveredAsync(
                account,
                MailFathomPermission.AdminOperate,
                accounts,
                authorization,
                cancellationToken) is not { } servedAccount
            || !await BelongsToAsync(servedAccount, organizationId, placements, cancellationToken))
        {
            return AdminAccountRequest.Refuse(account);
        }

        var promoted = await book.PromoteAsync(
            ContactBookScope.OfOrganization(organizationId, [servedAccount]),
            identity,
            AdministrativeWriter,
            ContactEndpoints.AdministratorWriting(context),
            cancellationToken);

        return TypedResults.Ok(ContactWriteResponse.OutcomeOf(promoted));
    }

    /// <summary>Erases one person from an organization's book.</summary>
    /// <param name="organizationId">The organization whose book the erasure reaches.</param>
    /// <param name="contactId">The contact to erase.</param>
    /// <param name="book">Performs the erasure.</param>
    /// <param name="organizations">Answers whether this deployment holds the named organization.</param>
    /// <param name="authorization">Answers whether the caller's scope covers the organization, which is otherwise refused as one this deployment does not hold.</param>
    /// <param name="cancellationToken">Cancels the erasure when the client disconnects.</param>
    /// <returns><c>200</c> with what was removed, including a book that held no such contact.</returns>
    internal static async Task<Results<Ok<ContactErasureResponse>, ProblemHttpResult>> EraseAsync(
        Guid organizationId,
        Guid contactId,
        [FromServices] ContactBook book,
        [FromServices] IOrganizationStore organizations,
        [FromServices] AccessAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);

        if (await RefuseWriterAsync(
                organizationId,
                MailFathomPermission.AdminErase,
                organizations,
                authorization,
                cancellationToken) is { } refusal)
        {
            return refusal;
        }

        if (!ContactEndpoints.TryReadContactId(contactId, out var identity))
        {
            return ContactEndpoints.EmptyIdentity();
        }

        var erasure = await book.EraseAsync(ScopeOf(organizationId), identity, cancellationToken);

        return TypedResults.Ok(new ContactErasureResponse(
            erasure.ContactId.Value,
            erasure.WasHeld,
            erasure.AddressesErased));
    }

    /// <summary>Produces everything an organization's book holds about one person.</summary>
    /// <param name="organizationId">The organization whose book is read.</param>
    /// <param name="contactId">The contact to export.</param>
    /// <param name="book">Produces the export.</param>
    /// <param name="authorization">Answers whether the caller's scope covers the organization, which is otherwise answered as one whose book holds nothing.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the export, or <c>200</c> with none where the book holds no such person.</returns>
    internal static async Task<Results<Ok<ContactExportResponse>, ProblemHttpResult>> ExportAsync(
        Guid organizationId,
        Guid contactId,
        [FromServices] ContactBook book,
        [FromServices] AccessAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(authorization);

        if (organizationId == Guid.Empty)
        {
            return EmptyOrganization();
        }

        if (!ContactEndpoints.TryReadContactId(contactId, out var identity))
        {
            return ContactEndpoints.EmptyIdentity();
        }

        if (!await Covers(authorization, MailFathomPermission.AdminAuditRead, organizationId, cancellationToken))
        {
            return TypedResults.Ok(ContactExportResponse.None);
        }

        var export = await book.ExportAsync(ScopeOf(organizationId), identity, cancellationToken);

        return TypedResults.Ok(ContactExportResponse.For(export));
    }

    /// <summary>Refuses a write naming an organization the caller's scope does not cover or the deployment does not hold, answering both alike.</summary>
    /// <remarks>
    /// The two are asked before the write rather than left to the foreign key, which would make a write under an
    /// organization nobody holds a fault where a refusal is owed. Answering an uncovered organization the same way keeps
    /// the refusal from telling an organization's administrator that another organization exists.
    /// </remarks>
    private static async Task<ProblemHttpResult?> RefuseWriterAsync(
        Guid organizationId,
        MailFathomPermission permission,
        IOrganizationStore organizations,
        AccessAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(organizations);
        ArgumentNullException.ThrowIfNull(authorization);

        if (organizationId == Guid.Empty)
        {
            return EmptyOrganization();
        }

        if (!await Covers(authorization, permission, organizationId, cancellationToken)
            || !await organizations.ExistsAsync(organizationId, cancellationToken))
        {
            return ContactEndpoints.Refused($"This deployment holds no organization '{organizationId:D}'.");
        }

        return null;
    }

    private static async Task<bool> BelongsToAsync(
        MailAccountId account,
        Guid organizationId,
        IAdministrativeTargets placements,
        CancellationToken cancellationToken)
    {
        var placed = await placements.PlaceMailAccountsAsync([account], cancellationToken);

        return placed.TryGetValue(account, out var target) && target.Organization == organizationId;
    }

    private static Task<bool> Covers(
        AccessAuthorization authorization,
        MailFathomPermission permission,
        Guid organizationId,
        CancellationToken cancellationToken) =>
        authorization.PermitsOverScopeAsync(permission, AssignmentScope.Organization(organizationId), cancellationToken);

    private static ContactBookScope ScopeOf(Guid organizationId) => ContactBookScope.OfOrganization(organizationId, []);

    private static ProblemHttpResult EmptyOrganization() =>
        ContactEndpoints.Refused("The request names no organization, and this contact book belongs to one.");
}
