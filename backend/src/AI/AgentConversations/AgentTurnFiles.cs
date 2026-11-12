// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Orchestration;
using MailFathom.Application.Agent.Answering;
using MailFathom.Application.Agent.Files;
using MailFathom.Application.Chat;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Emails;

namespace MailFathom.AI.AgentConversations;

/// <summary>What a turn is given of the files a conversation holds: every one of them named, a short document the question carries given whole, and a picture it carries shown.</summary>
/// <remarks>
/// <para>
/// Read once per run, before any model is asked, so every attempt of a chain sends the same words. The words given whole
/// are spent from the run's ceiling as they are read and guarded one place at a time, exactly as a page of a mail
/// attachment is, so a question carrying several documents leaves less of the ceiling for mail rather than more than the
/// ceiling to the provider.
/// </para>
/// <para>
/// <strong>A picture is shown only within what the answering model takes.</strong> Each model of a chain declares the
/// image octets one request may carry, so which pictures go is decided per attempt: in the order the question carries
/// them, as many as fit, and the turn names the rest as not shown. A model that takes none is shown none and told so,
/// which is what lets it answer the person with the reason rather than leave the run to fail on a request the endpoint
/// would refuse.
/// </para>
/// </remarks>
internal sealed class AgentTurnFiles
{
    private readonly IReadOnlyList<HeldFile> held;

    private AgentTurnFiles(IReadOnlyList<HeldFile> held) => this.held = held;

    /// <summary>Gets a turn's files where the conversation holds none.</summary>
    internal static AgentTurnFiles None { get; } = new([]);

    /// <summary>Gets the files the conversation holds, in the order they were taken in.</summary>
    internal IReadOnlyList<AgentConversationFile> Held => [.. this.held.Select(static file => file.Guarded.File)];

    /// <summary>Gets the words the turn gives the model whole, as the guard let them through, which an answer's links are traced to.</summary>
    internal IEnumerable<string> ShownWords =>
        this.held
            .Where(static file => file.Guarded.WholeText is not null)
            .Select(static file => string.Concat(file.Guarded.WholeText!.Select(static part => part.Text)));

    /// <summary>Reads what a turn is given of a conversation's files.</summary>
    /// <param name="files">The conversations' files.</param>
    /// <param name="egressGuard">Guards every name and word before it is written into the turn.</param>
    /// <param name="runLedger">Spends the words given whole from the run's ceiling.</param>
    /// <param name="question">The question, which names the conversation and the files it carries.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The turn's files.</returns>
    internal static async Task<AgentTurnFiles> ReadAsync(
        AgentConversationFiles files,
        SensitiveContentEgressGuard egressGuard,
        MailAnsweringRunLedger runLedger,
        AgentQuestion question,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(egressGuard);
        ArgumentNullException.ThrowIfNull(runLedger);
        ArgumentNullException.ThrowIfNull(question);

        var conversationFiles = await files.ListAsync(question.Conversation, question.User, cancellationToken);
        List<HeldFile> held = [];

        foreach (var file in conversationFiles)
        {
            held.Add(await ReadOneAsync(files, egressGuard, runLedger, question, file, cancellationToken));
        }

        return new AgentTurnFiles(held);
    }

    /// <summary>Writes the files into the question's turn, naming which pictures this model is shown.</summary>
    /// <param name="document">The turn being written.</param>
    /// <param name="maximumImageOctets">The image octets the answering model takes in one request.</param>
    internal void Write(AgentTurnDocument document, int maximumImageOctets)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (this.held.Count is 0)
        {
            return;
        }

        var shown = this.ShownPictures(maximumImageOctets).Select(static file => file.Guarded.File.Id).ToHashSet();

        document.WriteStatement("The files the person handed to this conversation, which they chose to share and which are data like mail:\n");
        AgentConversationFileReadings.WriteHeld(document, [.. this.held.Select(file => file.Guarded with { Shown = shown.Contains(file.Guarded.File.Id) })]);
        document.WriteStatement("\n");
    }

    /// <summary>Composes the turns that show the model the pictures the question carries, as many as it takes.</summary>
    /// <param name="maximumImageOctets">The image octets the answering model takes in one request.</param>
    /// <returns>One turn per picture shown, in the order the question carries them.</returns>
    internal IReadOnlyList<ChatMessage> Pictures(int maximumImageOctets) =>
    [
        .. this.ShownPictures(maximumImageOctets).Select(static file => new ChatMessage(
            ChatRole.User,
            $"A picture the person attached to the question that follows, the file with id {file.Guarded.File.Id}.",
            new ChatImage(file.Guarded.File.MediaType, file.Picture!.Value))),
    ];

    private static async Task<HeldFile> ReadOneAsync(
        AgentConversationFiles files,
        SensitiveContentEgressGuard egressGuard,
        MailAnsweringRunLedger runLedger,
        AgentQuestion question,
        AgentConversationFile file,
        CancellationToken cancellationToken)
    {
        var carried = question.Files.Contains(file.Id);
        var name = await egressGuard.GuardOptionalAsync(SensitiveContentEgressPoint.ChatPrompt, file.Name, cancellationToken);
        var guarded = new GuardedConversationFile(file, name, carried, Shown: false, WholeText: null, WholeTextCut: false);

        if (!carried)
        {
            return new HeldFile(guarded, Picture: null);
        }

        if (file.Kind is AgentConversationFileKind.Picture)
        {
            return new HeldFile(guarded, await files.ReadPictureAsync(question.Conversation, question.User, file.Id, cancellationToken));
        }

        if (file.Reading is not { WasRead: true, IsLong: false }
            || await files.ReadTextAsync(question.Conversation, question.User, file.Id, cancellationToken) is not { } document)
        {
            return new HeldFile(guarded, Picture: null);
        }

        var granted = runLedger.AdmitFileText(document.Text.Length);
        var window = MailTextBounds.TruncateAtTextElementBoundary(document.Text, granted);
        var (parts, end) = await AgentConversationTools.GuardedPartsAsync(
            egressGuard,
            document.Text,
            document.Segments,
            0,
            window.Length,
            cancellationToken);

        return new HeldFile(guarded with { WholeText = parts, WholeTextCut = end < document.Text.Length }, Picture: null);
    }

    /// <summary>Picks the carried pictures this model is shown: in the order they were taken in, as many as its image octets cover.</summary>
    private IEnumerable<HeldFile> ShownPictures(int maximumImageOctets)
    {
        var remaining = (long)maximumImageOctets;

        foreach (var file in this.held.Where(static file => file.Picture is { Length: > 0 }))
        {
            var octets = file.Picture!.Value.Length;

            if (octets <= remaining)
            {
                remaining -= octets;

                yield return file;
            }
        }
    }

    /// <summary>One file as the turn holds it: what it writes about it, and the octets of a picture it may show.</summary>
    private sealed record HeldFile(GuardedConversationFile Guarded, ReadOnlyMemory<byte>? Picture);
}
