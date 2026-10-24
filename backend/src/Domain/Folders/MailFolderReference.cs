// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Folders;

/// <summary>Names one folder of an account: by its identifier, by the alias configuration gave it, or by the role it plays.</summary>
/// <remarks>
/// <para>
/// Each form belongs to one audience. A surface — a tool argument, a client request — names a folder by its
/// <see cref="MailFolderId" /> or by its role, because that is the one identity every folder of every account carries
/// whatever copy of the mailbox is the truth. Configuration — a rule's destination, a spam destination — names a
/// folder by the alias it was declared under or by its role, because an operator writes names they chose rather than
/// identifiers a deployment generated. Which folder a reference means is an account's question rather than this
/// type's, so nothing here resolves one. See
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0039-one-folder-model-and-one-implementation-per-custody-phase.md">ADR 0039</see>.
/// </para>
/// <para>
/// The written form is the identifier or the alias itself, or the role behind the <c>role:</c> prefix — the same
/// <c>&lt;scheme&gt;:&lt;target&gt;</c> shape a secret reference is written in. The prefix is what keeps a role
/// unambiguous: an alias may legitimately read <c>Junk</c>, and without a marker a deployment that renamed its alias
/// would silently start meaning the role instead.
/// </para>
/// <para>
/// Being a struct, <see langword="default" /> is reachable and names no folder at all. <see cref="IsSpecified" />
/// reports that, and every factory here produces a specified value, so the default can only arrive from code that
/// declared a field and never assigned it.
/// </para>
/// </remarks>
public readonly record struct MailFolderReference
{
    /// <summary>The scheme that marks the text after it as a role rather than as an alias or an identifier.</summary>
    public const string RoleScheme = "role:";

    private MailFolderReference(MailFolderId? folder, MailFolderAlias? alias, MailFolderSpecialUse? role)
    {
        this.Folder = folder;
        this.Alias = alias;
        this.Role = role;
    }

    /// <summary>Gets the folder this reference names by its identifier, and <see langword="null" /> when it names it otherwise.</summary>
    public MailFolderId? Folder { get; }

    /// <summary>Gets the alias this reference names, and <see langword="null" /> when it names the folder otherwise.</summary>
    public MailFolderAlias? Alias { get; }

    /// <summary>Gets the role this reference names, and <see langword="null" /> when it names the folder otherwise.</summary>
    public MailFolderSpecialUse? Role { get; }

    /// <summary>Gets whether this value names a folder rather than being the unusable struct default.</summary>
    public bool IsSpecified => this.Folder is not null || this.Alias is not null || this.Role is not null;

    /// <summary>Names the folder carrying that identifier.</summary>
    /// <param name="folder">The folder's identifier.</param>
    /// <returns>The reference.</returns>
    public static MailFolderReference ToFolder(MailFolderId folder) => new(folder, alias: null, role: null);

    /// <summary>Names the folder configuration gave that alias.</summary>
    /// <param name="alias">The operator-facing folder name.</param>
    /// <returns>The reference.</returns>
    public static MailFolderReference ToAlias(MailFolderAlias alias) => new(folder: null, alias, role: null);

    /// <summary>Names whichever folder of the account plays that role.</summary>
    /// <param name="role">The role the folder plays.</param>
    /// <returns>The reference.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="role" /> is not a defined role.</exception>
    public static MailFolderReference ToRole(MailFolderSpecialUse role) => Enum.IsDefined(role)
        ? new MailFolderReference(folder: null, alias: null, role)
        : throw new ArgumentOutOfRangeException(
            nameof(role),
            role,
            "A folder reference cannot name a special-use role that does not exist.");

    /// <summary>Reads the text configuration named a folder with.</summary>
    /// <param name="value">The alias, or a role written as <c>role:&lt;name&gt;</c>.</param>
    /// <returns>The reference the text names.</returns>
    /// <exception cref="ArgumentException">Thrown when the text is blank, carries a control character, or names a role that does not exist.</exception>
    public static MailFolderReference Create(string value) => TryCreate(value, out var reference)
        ? reference
        : throw new ArgumentException(
            $"'{DescribeRefusedText(value)}' names neither a folder alias nor one of the roles {string.Join(", ", Enum.GetNames<MailFolderSpecialUse>())}.",
            nameof(value));

    /// <summary>Reads the text configuration named a folder with, without raising on text that names none.</summary>
    /// <param name="value">The alias, or a role written as <c>role:&lt;name&gt;</c>.</param>
    /// <param name="reference">The reference when the text names one; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the text names a folder.</returns>
    /// <remarks>
    /// The role name is matched against the declared names rather than parsed, which refuses two spellings that
    /// <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)" /> accepts and neither of which any operator meant.
    /// <c>role:4</c> would bind a typo onto whichever member currently carries that number. <c>role:Archive,Drafts</c>
    /// is worse: a comma-separated list is combined by bitwise OR whether or not the enumeration carries
    /// <see cref="FlagsAttribute" />, and these values are dense, so <c>Archive|Drafts</c> is <c>Sent</c> — a real role
    /// nobody wrote, which <see cref="Enum.IsDefined{TEnum}(TEnum)" /> then confirms.
    /// </remarks>
    public static bool TryCreate(string? value, out MailFolderReference reference)
    {
        reference = default;

        if (!TryTrim(value, out var trimmed))
        {
            return false;
        }

        if (!trimmed.StartsWith(RoleScheme, StringComparison.OrdinalIgnoreCase))
        {
            reference = ToAlias(MailFolderAlias.Create(trimmed));

            return true;
        }

        return TryCreateRole(trimmed, out reference);
    }

    /// <summary>Reads the text a caller of a surface named a folder with.</summary>
    /// <param name="value">The folder's identifier, or a role written as <c>role:&lt;name&gt;</c>.</param>
    /// <returns>The reference the text names.</returns>
    /// <exception cref="ArgumentException">Thrown when the text is neither an identifier nor a role that exists, which an alias is not.</exception>
    public static MailFolderReference CreateNamedByCaller(string value) => TryCreateNamedByCaller(value, out var reference)
        ? reference
        : throw new ArgumentException(
            $"'{DescribeRefusedText(value)}' names neither a folder identifier nor one of the roles {string.Join(", ", Enum.GetNames<MailFolderSpecialUse>())}.",
            nameof(value));

    /// <summary>Reads the text a caller of a surface named a folder with, without raising on text that names none.</summary>
    /// <param name="value">The folder's identifier, or a role written as <c>role:&lt;name&gt;</c>.</param>
    /// <param name="reference">The reference when the text names one; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the text names a folder.</returns>
    /// <remarks>
    /// An alias is refused here rather than resolved: it is configuration's name for a folder, a folder created on a
    /// held account has none, and a surface that accepted one beside the identifier would give every folder two names
    /// a caller has to know apart.
    /// </remarks>
    public static bool TryCreateNamedByCaller(string? value, out MailFolderReference reference)
    {
        reference = default;

        if (!TryTrim(value, out var trimmed))
        {
            return false;
        }

        if (trimmed.StartsWith(RoleScheme, StringComparison.OrdinalIgnoreCase))
        {
            return TryCreateRole(trimmed, out reference);
        }

        if (!MailFolderId.TryParse(trimmed, out var folder))
        {
            return false;
        }

        reference = ToFolder(folder);

        return true;
    }

    /// <summary>Returns the reference in the form it is written in, so a message names it back the way a caller wrote it.</summary>
    /// <returns>The identifier, the alias, the prefixed role, or a marker when the value is the struct default.</returns>
    public override string ToString() => this switch
    {
        { Folder: { } folder } => folder.ToString(),
        { Alias: { } alias } => alias.Value,
        { Role: { } role } => $"{RoleScheme}{role}",
        _ => "(unspecified)",
    };

    private static bool TryTrim(string? value, out string trimmed)
    {
        trimmed = string.Empty;

        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            return false;
        }

        trimmed = value.Trim();

        return true;
    }

    private static bool TryCreateRole(string trimmed, out MailFolderReference reference)
    {
        reference = default;

        var roleName = trimmed[RoleScheme.Length..].Trim();

        if (!Enum.GetNames<MailFolderSpecialUse>()
            .Any(declared => string.Equals(declared, roleName, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        reference = ToRole(Enum.Parse<MailFolderSpecialUse>(roleName, ignoreCase: true));

        return true;
    }

    /// <summary>Describes refused text without carrying a control character into a message that will be logged.</summary>
    private static string DescribeRefusedText(string? value) =>
        value is null || value.Any(char.IsControl) ? string.Empty : value;
}
