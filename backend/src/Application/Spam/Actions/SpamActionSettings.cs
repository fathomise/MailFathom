// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Folders;

namespace MailFathom.Application.Spam.Actions;

/// <summary>What an operator asked to happen to mail a classification calls junk.</summary>
/// <remarks>
/// <para>
/// Every switch is off in <see cref="None" />, which is what a deployment that configured nothing runs with: a verdict
/// is recorded and no mailbox is written to. They are independent of each other, so an operator can file junk without
/// marking it read, mark it read without moving it, and give it the junk keywords every other mail client reads without
/// doing either.
/// </para>
/// <para>
/// No switch is a decision about classification itself. Turning them all off leaves classification recording exactly
/// what it recorded before, which is the property that lets an operator watch what classification concludes for a while
/// before letting it touch anything.
/// </para>
/// </remarks>
public sealed record SpamActionSettings
{
    private SpamActionSettings(
        bool filesJunk,
        bool marksJunkRead,
        bool marksJunkWithKeywords,
        MailFolderReference junkFolder)
    {
        this.FilesJunk = filesJunk;
        this.MarksJunkRead = marksJunkRead;
        this.MarksJunkWithKeywords = marksJunkWithKeywords;
        this.JunkFolder = junkFolder;
    }

    /// <summary>Gets the settings a deployment that asked for no action runs with.</summary>
    public static SpamActionSettings None { get; } = new(
        filesJunk: false,
        marksJunkRead: false,
        marksJunkWithKeywords: false,
        DefaultJunkFolder);

    /// <summary>Gets the folder a filing goes to when the operator named none: whichever folder the account maps to the junk role.</summary>
    /// <remarks>
    /// A role rather than an alias, because the folder junk belongs in is a different name on every server and an account
    /// already states which of its folders plays that part. An operator whose junk folder is not the one the account
    /// labelled names it explicitly instead.
    /// </remarks>
    public static MailFolderReference DefaultJunkFolder { get; } = MailFolderReference.ToRole(MailFolderSpecialUse.Junk);

    /// <summary>Gets whether junk is moved into the junk folder on the mail server.</summary>
    public bool FilesJunk { get; }

    /// <summary>Gets whether junk has its remote <c>\Seen</c> flag set.</summary>
    public bool MarksJunkRead { get; }

    /// <summary>Gets whether junk is given the <c>$Junk</c> keyword, and phishing <c>$Phishing</c> beside it.</summary>
    public bool MarksJunkWithKeywords { get; }

    /// <summary>Gets the folder junk is filed into, named by alias or by the role a folder plays.</summary>
    public MailFolderReference JunkFolder { get; }

    /// <summary>Gets whether anything at all is acted on.</summary>
    public bool IsAnyActionEnabled => this.FilesJunk || this.MarksJunkRead || this.MarksJunkWithKeywords;

    /// <summary>Builds the settings an operator's answers describe.</summary>
    /// <param name="filesJunk">Whether junk is moved into the junk folder.</param>
    /// <param name="marksJunkRead">Whether junk is marked read.</param>
    /// <param name="junkFolder">The folder junk is filed into, or <see langword="null" /> to take <see cref="DefaultJunkFolder" />.</param>
    /// <param name="marksJunkWithKeywords">Whether junk is given the junk keywords.</param>
    /// <returns>The settings.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="junkFolder" /> is the unspecified struct default.</exception>
    public static SpamActionSettings Create(
        bool filesJunk,
        bool marksJunkRead,
        MailFolderReference? junkFolder = null,
        bool marksJunkWithKeywords = false)
    {
        if (junkFolder is { } named && !named.IsSpecified)
        {
            throw new ArgumentException("The unspecified default of the struct names no junk folder.", nameof(junkFolder));
        }

        return new SpamActionSettings(filesJunk, marksJunkRead, marksJunkWithKeywords, junkFolder ?? DefaultJunkFolder);
    }
}
