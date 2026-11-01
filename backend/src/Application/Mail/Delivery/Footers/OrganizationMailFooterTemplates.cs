// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Mail.Delivery.Footers;

/// <summary>Sets, reads, and clears the footer template an organization's members send with from its accounts.</summary>
/// <remarks>
/// <para>
/// Reading is <see cref="MailFathomPermission.AdminRead" /> and writing is
/// <see cref="MailFathomPermission.AdminConfigurationWrite" />, each at a scope covering the organization, which the
/// transport resolves from the route; this asks the same grants at any scope, so a caller reaching it another way is
/// still asked.
/// </para>
/// <para>
/// A template is not a governed property. A settings policy governs a property of the user record or of the mail
/// account record, and a footer belongs to neither alone: it is what one person sends from one account.
/// </para>
/// </remarks>
/// <param name="authorization">Reports the grant the caller holds.</param>
/// <param name="store">Holds the templates.</param>
/// <param name="markup">Sanitizes what the administrator writes.</param>
public sealed class OrganizationMailFooterTemplates(
    AccessAuthorization authorization,
    IMailFooterStore store,
    IMailFooterMarkup markup)
{
    /// <summary>Reads one organization's template.</summary>
    /// <param name="organization">The organization.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The template, or <see langword="null" /> where none is set.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller holds <see cref="MailFathomPermission.AdminRead" /> at no scope.</exception>
    public Task<MailFooterContent?> ReadAsync(Guid organization, CancellationToken cancellationToken)
    {
        authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminRead);

        return store.ReadTemplateAsync(organization, cancellationToken);
    }

    /// <summary>Replaces one organization's template, or says why it may not be saved.</summary>
    /// <param name="organization">The organization.</param>
    /// <param name="html">The template's markup, naming its variables as <c>{{name}}</c>.</param>
    /// <param name="images">The images uploaded with it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What became of the write, naming the variable a refusal is about.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="html" /> or <paramref name="images" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller holds <see cref="MailFathomPermission.AdminConfigurationWrite" /> at no scope.</exception>
    public async Task<MailFooterWrite> SaveAsync(
        Guid organization,
        string html,
        IReadOnlyList<MailFooterImage> images,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(images);

        authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminConfigurationWrite);

        if (MailFooterContent.Read(html, images, markup, out var refusal) is not { } template)
        {
            return new MailFooterWrite(MailFooterWriteOutcome.Refused, Refusal: refusal);
        }

        // Read off the sanitized markup rather than what was written, because that is what is filled at send time: a
        // variable written inside an element the sanitizer removes is not one any member's message would carry.
        if (MailFooterTemplate.FindUnknownVariable(template.Html) is { } unknown)
        {
            return new MailFooterWrite(
                MailFooterWriteOutcome.Refused,
                Refusal: new MailFooterRefusal(MailFooterRefusalReason.VariableUnknown, unknown));
        }

        return await store.SaveTemplateAsync(organization, template, cancellationToken)
            ? new MailFooterWrite(MailFooterWriteOutcome.Saved, template)
            : new MailFooterWrite(MailFooterWriteOutcome.OrganizationUnknown);
    }

    /// <summary>Clears one organization's template, so each member's own footer is what they send with again.</summary>
    /// <param name="organization">The organization.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the template is gone.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller holds <see cref="MailFathomPermission.AdminConfigurationWrite" /> at no scope.</exception>
    public Task ClearAsync(Guid organization, CancellationToken cancellationToken)
    {
        authorization.RequirePermissionAtAnyScope(MailFathomPermission.AdminConfigurationWrite);

        return store.RemoveTemplateAsync(organization, cancellationToken);
    }
}
