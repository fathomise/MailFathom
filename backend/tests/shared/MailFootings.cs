// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using MailFathom.Application.Contacts.Members;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Footers;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.TestSupport;

/// <summary>Builds the footer decision the outbox is exercised against.</summary>
/// <remarks>
/// Most tests of a send are about something other than its footer, so the default shape is a deployment where nobody
/// wrote one: the message is stored exactly as it was composed.
/// </remarks>
internal static class MailFootings
{
    /// <summary>Builds a decision under which no message carries a footer.</summary>
    /// <returns>The decision.</returns>
    internal static MailFootersInForce SendingNone() => Over(new InMemoryMailFooterStore());

    /// <summary>Builds a decision over the footers a store holds, appending them with the composer given.</summary>
    /// <param name="store">The footers.</param>
    /// <param name="composer">What puts a footer into a message, or <see langword="null" /> for one that appends its text under the message.</param>
    /// <param name="members">The member directory a template is filled from, or <see langword="null" /> for one holding nobody.</param>
    /// <returns>The decision.</returns>
    internal static MailFootersInForce Over(
        IMailFooterStore store,
        IMailFooterComposer? composer = null,
        IMemberDirectory? members = null) =>
        new(
            store,
            members ?? new EmptyMemberDirectory(),
            new VerbatimMailFooterMarkup(),
            composer ?? new TextAppendingMailFooterComposer(),
            new OutgoingEmailBounds
            {
                MaxRecipientCount = 100,
                MaxBodyCharacters = 1_000_000,
                MaxAttachmentCount = 20,
                MaxAttachmentBytes = 10_000_000,
                MaxMessageBytes = 25_000_000,
            });

    /// <summary>Markup that is already safe, so sanitizing it changes nothing and its text is its markup.</summary>
    internal sealed class VerbatimMailFooterMarkup : IMailFooterMarkup
    {
        /// <inheritdoc />
        public MailFooterMarkupReading Sanitize(string html, IReadOnlySet<string> imageNames) => new(html, null);

        /// <inheritdoc />
        public string ReadPlainText(string html) => html;
    }

    /// <summary>A composer that writes the footer's text under the message's bytes, so a test can see it arrived once.</summary>
    internal sealed class TextAppendingMailFooterComposer : IMailFooterComposer
    {
        /// <inheritdoc />
        public ReadOnlyMemory<byte> Append(ReadOnlyMemory<byte> rawMime, MailFooter footer) =>
            (byte[])[.. rawMime.ToArray(), .. Encoding.UTF8.GetBytes($"\r\n[footer:{footer.PlainText}]")];

        /// <inheritdoc />
        public ReadOnlyMemory<byte> ComposeAlone(MailFooter footer) =>
            Encoding.UTF8.GetBytes($"[footer:{footer.PlainText}]");
    }

    private sealed class EmptyMemberDirectory : IMemberDirectory
    {
        public Task<IReadOnlyList<MemberContact>> ReadAsync(Guid organization, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MemberContact>>([]);

        public Task<MemberContact?> FindAsync(Guid organization, UserId member, CancellationToken cancellationToken) =>
            Task.FromResult<MemberContact?>(null);
    }
}

/// <summary>Holds footers and templates in memory, beside the assignments and organizations they hang off.</summary>
internal sealed class InMemoryMailFooterStore : IMailFooterStore
{
    private readonly Dictionary<(UserId User, MailAccountId Account), MailFooterContent> own = [];
    private readonly Dictionary<Guid, MailFooterContent> templates = [];
    private readonly Dictionary<MailAccountId, Guid> holders = [];
    private readonly HashSet<(UserId User, MailAccountId Account)> assignments = [];
    private readonly HashSet<Guid> organizations = [];

    /// <summary>Assigns a user an account, which is what a footer of theirs for it hangs off.</summary>
    /// <param name="user">The user.</param>
    /// <param name="account">The account.</param>
    /// <returns>This store.</returns>
    internal InMemoryMailFooterStore Assigning(UserId user, MailAccountId account)
    {
        this.assignments.Add((user, account));

        return this;
    }

    /// <summary>Records an organization holding an account.</summary>
    /// <param name="organization">The organization.</param>
    /// <param name="account">The account it holds.</param>
    /// <returns>This store.</returns>
    internal InMemoryMailFooterStore Holding(Guid organization, MailAccountId account)
    {
        this.organizations.Add(organization);
        this.holders[account] = organization;

        return this;
    }

    /// <inheritdoc />
    public Task<MailFooterContent?> ReadOwnAsync(UserId user, MailAccountId account, CancellationToken cancellationToken) =>
        Task.FromResult(this.own.GetValueOrDefault((user, account)));

    /// <inheritdoc />
    public Task<bool> SaveOwnAsync(UserId user, MailAccountId account, MailFooterContent footer, CancellationToken cancellationToken)
    {
        if (!this.assignments.Contains((user, account)))
        {
            return Task.FromResult(false);
        }

        this.own[(user, account)] = footer;

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task RemoveOwnAsync(UserId user, MailAccountId account, CancellationToken cancellationToken)
    {
        this.own.Remove((user, account));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<MailFooterContent?> ReadTemplateAsync(Guid organization, CancellationToken cancellationToken) =>
        Task.FromResult(this.templates.GetValueOrDefault(organization));

    /// <inheritdoc />
    public Task<bool> SaveTemplateAsync(Guid organization, MailFooterContent footerTemplate, CancellationToken cancellationToken)
    {
        if (!this.organizations.Contains(organization))
        {
            return Task.FromResult(false);
        }

        this.templates[organization] = footerTemplate;

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task RemoveTemplateAsync(Guid organization, CancellationToken cancellationToken)
    {
        this.templates.Remove(organization);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<MailFooterSources> ReadSourcesAsync(UserId user, MailAccountId account, CancellationToken cancellationToken)
    {
        Guid? organization = this.holders.TryGetValue(account, out var holder) ? holder : null;

        return Task.FromResult(new MailFooterSources(
            organization,
            organization is { } held ? this.templates.GetValueOrDefault(held) : null,
            this.own.GetValueOrDefault((user, account))));
    }
}
