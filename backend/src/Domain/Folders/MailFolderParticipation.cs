// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Folders;

/// <summary>States how far into MailFathom one folder is admitted, starting with whether MailFathom has it at all.</summary>
/// <remarks>
/// <para>
/// Mapping a folder and mirroring it are two decisions rather than one. A mapping makes the folder known: it is
/// nameable by its alias, resolvable by the role its server advertises, and reachable as the destination of a change
/// MailFathom is asked to make. What the other three answers add is what happens to the mail inside it, which costs
/// storage, a provider's tokens, and — for some folders — the discretion of never having an agent or a model read them.
/// </para>
/// <para>
/// A folder no mapping names is not one of those decisions taken quietly: it is <see cref="Unmapped" />, which is a
/// folder MailFathom does not have. That is why <see cref="IsMapped" /> is a state of its own rather than something a
/// reader infers from the other three being off — <see cref="MappedOnly" /> answers those three identically and means
/// something else entirely, and two values that compare equal could not tell an operator's decision to stop mirroring a
/// folder apart from their decision to stop having it.
/// </para>
/// <para>
/// The answers are not independent, and the dependencies run one way. A folder nothing mirrors has no local content, so
/// there is nothing to embed and nothing AI could read whatever the other two say. A folder withheld from AI is one no
/// model may reach, and an embedding is a model's reading of its content, so it is embedded by nobody. This type derives
/// both rather than trusting a caller with them, which is why every mapped value is built through <see cref="Create" />.
/// Configuration refuses each contradiction separately, so an operator who asked for embeddings on an unmirrored or a
/// withheld folder is told rather than quietly given the normalized answer.
/// </para>
/// </remarks>
public sealed record MailFolderParticipation
{
    private MailFolderParticipation(
        bool isMapped,
        bool isSynchronized,
        bool generatesEmbeddings,
        bool isVisibleToAI)
    {
        this.IsMapped = isMapped;
        this.IsSynchronized = isSynchronized;
        this.GeneratesEmbeddings = generatesEmbeddings;
        this.IsVisibleToAI = isVisibleToAI;
    }

    /// <summary>Gets the participation of a folder that takes part in everything, which is what a mapping means by default.</summary>
    public static MailFolderParticipation Full { get; } = new(
        isMapped: true,
        isSynchronized: true,
        generatesEmbeddings: true,
        isVisibleToAI: true);

    /// <summary>Gets the participation of a folder MailFathom knows by name and mirrors nothing of.</summary>
    public static MailFolderParticipation MappedOnly { get; } = new(
        isMapped: true,
        isSynchronized: false,
        generatesEmbeddings: false,
        isVisibleToAI: false);

    /// <summary>Gets the participation of a folder no mapping names, which takes part in nothing because it does not exist here.</summary>
    /// <remarks>
    /// Mail stored under such an alias is inert rather than readable, and it is kept rather than erased for the reason
    /// <see cref="MappedOnly" />'s is: nothing here takes local mail away because a configuration value changed. What
    /// makes the mail readable again is mapping the folder again, which is the same resumption switching mirroring back
    /// on is.
    /// </remarks>
    public static MailFolderParticipation Unmapped { get; } = new(
        isMapped: false,
        isSynchronized: false,
        generatesEmbeddings: false,
        isVisibleToAI: false);

    /// <summary>Gets whether a mapping names the folder at all.</summary>
    /// <remarks>
    /// Nothing discovers folders into mappings, so this is an operator's statement rather than a reading of what the
    /// server publishes. Off, every other answer here is off with it and no configuration can raise one of them.
    /// </remarks>
    public bool IsMapped { get; }

    /// <summary>Gets whether the folder's mail is mirrored locally.</summary>
    /// <remarks>
    /// Off, no synchronization connection is opened for the folder and nothing further of it is stored. What a previous
    /// configuration stored is kept, inert and read by nothing, so switching the folder back on resumes from the
    /// checkpoint it left rather than mirroring the folder again; erasing it is a command an operator runs. The alias
    /// goes on naming the folder, which is what a mutation writes into.
    /// </remarks>
    public bool IsSynchronized { get; }

    /// <summary>Gets whether the folder's content is cut into passages and embedded.</summary>
    /// <remarks>Off, the folder is still mirrored and still searchable by everything an embedding is not needed for.</remarks>
    public bool GeneratesEmbeddings { get; }

    /// <summary>Gets whether any AI function may reach the folder: an agent, an MCP tool, or a model or embedding provider.</summary>
    /// <remarks>
    /// Off, the folder is withheld from AI and from nothing else. It is still mirrored and the person still reads it in
    /// the client like any other, and everything that asks no model — synchronization, rules, retention — still reaches
    /// it. What no longer reaches it is every read whose answer goes to an agent or a model, every act an agent asks for,
    /// and every derivation a model computes, which is why <see cref="GeneratesEmbeddings" /> is off with it.
    /// </remarks>
    public bool IsVisibleToAI { get; }

    /// <summary>Builds the participation an operator's three answers describe, for a folder their mapping names.</summary>
    /// <param name="isSynchronized">Whether the folder's mail is mirrored locally.</param>
    /// <param name="generatesEmbeddings">Whether the mirrored content is embedded.</param>
    /// <param name="isVisibleToAI">Whether any AI function may reach the folder.</param>
    /// <returns>The participation, with everything an unmirrored or a withheld folder cannot take part in already withdrawn.</returns>
    /// <remarks>
    /// Every value this builds is mapped, because the three answers are what a mapping says and there is nowhere else
    /// to write them. <see cref="Unmapped" /> is therefore reached by a folder having no mapping rather than by an
    /// argument here.
    /// </remarks>
    public static MailFolderParticipation Create(
        bool isSynchronized,
        bool generatesEmbeddings,
        bool isVisibleToAI) => (isSynchronized, generatesEmbeddings, isVisibleToAI) switch
        {
            (false, _, _) => MappedOnly,
            (true, true, true) => Full,
            _ => new MailFolderParticipation(
                isMapped: true,
                isSynchronized: true,
                generatesEmbeddings && isVisibleToAI,
                isVisibleToAI),
        };

    /// <summary>Reports whether a read answering for one kind of reader may reach the folder.</summary>
    /// <param name="reader">Who the read is answering for.</param>
    /// <returns><see langword="true" /> when the folder is visible to AI for an agent, or mirrored for the person.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="reader" /> is not a defined member.</exception>
    /// <remarks>
    /// The person is answered by mirroring rather than by mapping: a folder nothing mirrors keeps whatever an earlier
    /// configuration stored, inert, and reading it would present a copy nothing refreshes as the person's mailbox.
    /// </remarks>
    public bool Admits(MailboxReaderKind reader) => reader switch
    {
        MailboxReaderKind.Agent => this.IsVisibleToAI,
        MailboxReaderKind.Person => this.IsSynchronized,
        _ => throw new ArgumentOutOfRangeException(nameof(reader), reader, "A read answers either for an agent or for the person."),
    };
}
