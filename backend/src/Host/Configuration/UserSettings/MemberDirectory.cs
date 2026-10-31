// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Contacts.Members;
using MailFathom.Application.StoredFiles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Host.Configuration.Policies;
using MailFathom.Infrastructure.Persistence.Users;

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>Composes an organization's member directory from its people's rows, reading each record through the settings policies governing it.</summary>
/// <remarks>
/// <para>
/// Here rather than in the store because what a member's record states is what the configuration layer resolves: the
/// phone numbers, the preferred address, the portrait, and the zone are read from the record each member is served
/// with, through the binder every other reading of a user's record goes through. Nothing composes a served user for
/// it, which would resolve every member's secrets to show their colleagues a name.
/// </para>
/// <para>
/// A record that does not bind — one past the bound, one somebody wrote by hand — still yields the member, with what
/// their row holds and nothing their record states, so nobody drops out of their colleagues' directory over what their
/// own record says. Under a policy this build cannot read, the record is read as it stands: every property the
/// directory shows but the zone is one no policy defaults or forces. A member whose label carries a
/// character no contact name may is left out, because no contact could carry their name.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this directory.")]
internal sealed class MemberDirectory(IOrganizationMemberReader members, UserAccountDocumentBinder binder) : IMemberDirectory
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<MemberContact>> ReadAsync(Guid organization, CancellationToken cancellationToken)
    {
        if (await members.ReadAsync(organization, IMemberDirectory.MaximumMemberCount, cancellationToken) is not { } read)
        {
            return [];
        }

        var layers = SettingsPolicyLayers.Read(read.Policies, SettingsPolicySection.Users, out _);

        return [.. read.Members.Select(member => this.ContactOf(member, read.OrganizationName, layers)).OfType<MemberContact>()];
    }

    /// <inheritdoc />
    public async Task<MemberContact?> FindAsync(Guid organization, UserId member, CancellationToken cancellationToken)
    {
        if (await members.ReadMemberAsync(organization, member, cancellationToken) is not { Members: [var held] } read)
        {
            return null;
        }

        return this.ContactOf(held, read.OrganizationName, SettingsPolicyLayers.Read(read.Policies, SettingsPolicySection.Users, out _));
    }

    /// <summary>Reads the name a member's label gives their contact, or nothing where no contact could carry it.</summary>
    private static ContactDisplayName? NameOf(string label)
    {
        try
        {
            return ContactDisplayName.Create(label);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Composes one member's contact, or nothing where their label can be no contact's name.</summary>
    private MemberContact? ContactOf(OrganizationMember member, string organizationName, SettingsPolicyLayers? layers)
    {
        if (NameOf(member.DisplayName) is not { } name)
        {
            return null;
        }

        EmailAddress[] addresses =
        [
            .. member.Addresses
                .Select(written => EmailAddress.TryCreate(displayName: null, written, out var address) ? address : (EmailAddress?)null)
                .OfType<EmailAddress>(),
        ];
        var record = this.RecordOf(member.Json, layers);

        return new MemberContact(
            member.User,
            name,
            addresses,
            MemberContact.PreferredAddressAmong(addresses, record?.PreferredAddress),
            record?.ReadingPhoneNumbers ?? [],
            member.JobTitle,
            organizationName,
            record?.Portrait is { } portrait ? StoredFileId.Create(portrait) : null,
            record?.ReadingTimeZone);
    }

    /// <summary>Binds the record a member is served with, or nothing where it does not bind.</summary>
    private UserAccountOptions? RecordOf(string? json, SettingsPolicyLayers? layers)
    {
        if (json is null)
        {
            return null;
        }

        var served = layers is null ? json : GovernedRecordResolution.Resolve(SettingsPolicySection.Users, layers, json).EffectiveJson;

        return binder.Bind(served, UserRecordArrival.AlreadyHeld).User;
    }
}
