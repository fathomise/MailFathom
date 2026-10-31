// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>The user a deployment serves: one row per person, whichever mail accounts they own.</summary>
/// <remarks>
/// <para>
/// This is the user axis the mail graph hangs on. A mail account names exactly one of these rows, and everything
/// derived from that account's mail reaches this row through the foreign keys beneath it, which is what makes erasing
/// a user a delete rather than a question asked of every repository.
/// </para>
/// <para>
/// The relational envelope is deliberately the whole of what this type declares. The identifier, the label, the
/// version, the marker, and the two instants are what ownership, lookup, concurrency, and cascade are decided by, so
/// none of them depends on reading a document; the document beside them is the user's configurable record and is
/// opaque here.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class UserAccountEntity
{
    /// <summary>The table these rows live in, named here because a composed statement locks one.</summary>
    /// <remarks>Stated for the reason <see cref="ClientSessionEntity.TableName" /> is: a session's mint and renewal take a share lock on the user row before they write, and a statement naming this table by a literal would be a second description of where these rows live.</remarks>
    internal const string TableName = "settings_accounts";

    /// <summary>The key column, named here for the same reason the table is.</summary>
    internal const string IdColumnName = "Id";

    /// <summary>The MCP endpoint switch's column, named here because a session's statements read the client one beside it.</summary>
    internal const string McpEndpointEnabledColumnName = nameof(McpEndpointEnabled);

    /// <summary>The client endpoint switch's column, named here because a session's statements read and lock on it.</summary>
    internal const string ClientEndpointEnabledColumnName = nameof(ClientEndpointEnabled);

    /// <summary>The organization column, named here because presence reads who to tell about a member's change by it.</summary>
    internal const string OrganizationIdColumnName = nameof(OrganizationId);

    /// <summary>The longest label a user is told apart by, which is what a mail account's identifier is bounded at.</summary>
    /// <remarks>Taken from the application's own statement of the bound, so the column and the rule that judges a declaration carrying a label cannot disagree about it.</remarks>
    public const int MaximumDisplayNameLength = UserRecord.MaximumDisplayNameLength;

    /// <summary>The stable user identity every mail account of theirs points at.</summary>
    /// <remarks>
    /// A version 7 identifier like every other MailFathom mints. A user identifier reaches administrative APIs, audit
    /// records, and logs, so it says there when each user was provisioned and in what order relative to every other —
    /// a fact about people, which ADR 0036 accepts as a residual in exchange for a rule with no exceptions.
    /// </remarks>
    public Guid Id { get; set; }

    /// <summary>The label an operator reads this user by, which another user may carry as well.</summary>
    /// <remarks>
    /// A label rather than an identity: nothing resolves a user by it, and every reference to a user is the
    /// identifier above. So any number of rows may carry one label, as people share a name, and it is exact rather
    /// than case-folded, because a comparison rule is owed by a value something resolves by and nothing resolves by
    /// this.
    /// </remarks>
    public required string DisplayName { get; set; }

    /// <summary>The organization the user belongs to, or <see langword="null" /> for somebody in none.</summary>
    /// <remarks>It groups users and scopes the login their passwords are typed as, and narrows nothing they read.</remarks>
    public Guid? OrganizationId { get; set; }

    /// <summary>Whether the user is a person or a technical account.</summary>
    /// <remarks>A column rather than a property of the document, because the organization's member directory reads it across every member in one statement, and an administrator writes it rather than the person.</remarks>
    public UserType Type { get; set; } = UserType.User;

    /// <summary>The title the user's colleagues read them by, or <see langword="null" /> for none.</summary>
    /// <remarks>A column for the reason <see cref="Type" /> is one.</remarks>
    public string? JobTitle { get; set; }

    /// <summary>The member of the same organization this user reports to, or <see langword="null" /> for nobody.</summary>
    /// <remarks>A column for the reason <see cref="Type" /> is one. Who reports to a user is never stored: it is every row naming them here.</remarks>
    public Guid? ManagerId { get; set; }

    /// <summary>The user's configurable record, as one <c>jsonb</c> document.</summary>
    /// <remarks>
    /// Held as text because nothing here reads into it: what the document contains — the user's mail-account
    /// declarations and their user-level settings — is written and projected by the configuration layer, and this
    /// table exists to give that layer a row per user rather than to interpret one.
    /// </remarks>
    public required string Document { get; set; }

    /// <summary>Whether this user is served on the MCP endpoint, whichever credential they present there.</summary>
    /// <remarks>On until an administrator turns it off, on the row as in the database default, so a row written by a statement naming no switch reaches both endpoints.</remarks>
    public bool McpEndpointEnabled { get; set; } = true;

    /// <summary>Whether this user is served on the client endpoint, whichever credential or session they present there.</summary>
    public bool ClientEndpointEnabled { get; set; } = true;

    /// <summary>The <see cref="Version" /> of the record the two endpoint switches were derived from.</summary>
    /// <remarks>
    /// <para>
    /// Half of the stamp
    /// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0037-a-settings-policy-at-the-deployment-and-at-each-organization.md">ADR 0037</see>
    /// § <em>How a value resolves</em> decides, and <see cref="EndpointSwitchesOrganizationId" /> is the other. A
    /// write that derives the switches from the effective record sets both to what the row then holds, and a write
    /// that moves the version and leaves the switches as they are moves this with it. A build that does not know
    /// settings policies writes neither, so a record it wrote, or a user it moved, is a row whose stamp no longer
    /// matches it — which is what the reconciliation on every replica's convergence interval looks for and derives
    /// again.
    /// </para>
    /// <para>
    /// No record version is zero, so a row inserted by a statement naming no stamp reads as one whose switches
    /// nothing has derived yet.
    /// </para>
    /// </remarks>
    public long EndpointSwitchesVersion { get; set; }

    /// <summary>The organization whose policy the two endpoint switches were derived under, or <see langword="null" /> for none.</summary>
    /// <remarks>
    /// A stamp rather than a reference, so it carries no foreign key: it is compared with
    /// <see cref="OrganizationId" /> and never followed, and a key that cleared it when an organization went would
    /// turn a row derived under that organization into one that reads as derived under none.
    /// </remarks>
    public Guid? EndpointSwitchesOrganizationId { get; set; }

    /// <summary>The version a write is accepted against, so two writers cannot both commit over one document.</summary>
    public long Version { get; set; }

    /// <summary>When the user record was provisioned.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the document last changed, which is the provisioning instant until one does.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
