// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Delivery.Footers;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Footers;

/// <summary>Holds footers and templates in PostgreSQL, each as one <c>jsonb</c> document on its row.</summary>
/// <remarks>
/// An account identifier this deployment does not generate names no row, so it is read as an account with no footer
/// and written as one the user is not assigned, rather than refused: neither the relation nor the footers hold anything
/// for it.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailFooterStore(MailFathomDbContext context, TimeProvider clock) : IMailFooterStore
{
    /// <inheritdoc />
    public async Task<MailFooterContent?> ReadOwnAsync(
        UserId user,
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(account.Value, out var accountId))
        {
            return null;
        }

        var userId = user.Value;

        var document = await context.MailAccountFooters
            .AsNoTracking()
            .Where(footer => footer.UserId == userId && footer.MailAccountId == accountId)
            .Select(footer => footer.Document)
            .FirstOrDefaultAsync(cancellationToken);

        return document is null ? null : MailFooterDocument.Parse(document);
    }

    /// <inheritdoc />
    public async Task<bool> SaveOwnAsync(
        UserId user,
        MailAccountId account,
        MailFooterContent footer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(footer);

        if (!Guid.TryParse(account.Value, out var accountId))
        {
            return false;
        }

        var rows = await context.Database.ExecuteSqlRawAsync(
            MailFooterStatements.ComposeOwnUpsert(context.Model),
            [user.Value, accountId, MailFooterDocument.Render(footer), clock.GetUtcNow()],
            cancellationToken);

        return rows > 0;
    }

    /// <inheritdoc />
    public async Task RemoveOwnAsync(UserId user, MailAccountId account, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(account.Value, out var accountId))
        {
            return;
        }

        var userId = user.Value;

        await context.MailAccountFooters
            .Where(footer => footer.UserId == userId && footer.MailAccountId == accountId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MailFooterContent?> ReadTemplateAsync(Guid organization, CancellationToken cancellationToken)
    {
        var document = await context.OrganizationFooterTemplates
            .AsNoTracking()
            .Where(template => template.OrganizationId == organization)
            .Select(template => template.Document)
            .FirstOrDefaultAsync(cancellationToken);

        return document is null ? null : MailFooterDocument.Parse(document);
    }

    /// <inheritdoc />
    public async Task<bool> SaveTemplateAsync(
        Guid organization,
        MailFooterContent footerTemplate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(footerTemplate);

        var rows = await context.Database.ExecuteSqlRawAsync(
            MailFooterStatements.ComposeTemplateUpsert(context.Model),
            [organization, MailFooterDocument.Render(footerTemplate), clock.GetUtcNow()],
            cancellationToken);

        return rows > 0;
    }

    /// <inheritdoc />
    public Task RemoveTemplateAsync(Guid organization, CancellationToken cancellationToken) =>
        context.OrganizationFooterTemplates
            .Where(template => template.OrganizationId == organization)
            .ExecuteDeleteAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<MailFooterSources> ReadSourcesAsync(
        UserId user,
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(account.Value, out var accountId))
        {
            return new MailFooterSources(null, null, null);
        }

        var organization = await context.MailAccountRecords
            .AsNoTracking()
            .Where(record => record.Id == accountId)
            .Select(record => record.OrganizationId)
            .FirstOrDefaultAsync(cancellationToken);

        var template = organization is { } holder
            ? await this.ReadTemplateAsync(holder, cancellationToken)
            : null;

        return new MailFooterSources(
            organization,
            template,
            await this.ReadOwnAsync(user, account, cancellationToken));
    }
}
