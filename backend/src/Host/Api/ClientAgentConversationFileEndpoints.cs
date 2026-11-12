// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Agent.Files;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace MailFathom.Host.Api;

/// <summary>Takes files into a person's conversation with the Agent, lists the files a conversation holds, and hands one back whole.</summary>
/// <remarks>
/// <para>
/// A file is posted as the request body itself, with the name the person's machine gave it in a
/// <c>Content-Disposition</c> header, exactly as a download hands one back. The declared <c>Content-Type</c> is not read:
/// what a file is, is decided from its octets. The name is shown back and decides nothing.
/// </para>
/// <para>
/// Every route is published under the grant that uses the Agent, and each resolves whose conversation it is from the
/// signed-in account, so a conversation of somebody else answers as one that does not exist. A file is taken into a
/// conversation the client names, which is started there where nothing stands under that identifier yet — so a file
/// attached before the first question is posted is held by the conversation that question then starts.
/// </para>
/// <para>
/// Nothing a file holds, and not its name, reaches a log, a span, or a problem detail.
/// </para>
/// </remarks>
internal static class ClientAgentConversationFileEndpoints
{
    /// <summary>The route a conversation's files are taken in and listed at, relative to the client prefix.</summary>
    internal const string FilesRoute = "/agent/conversations/{conversationId:guid}/files";

    /// <summary>The route one of a conversation's files is downloaded from, relative to the client prefix.</summary>
    internal const string FileRoute = FilesRoute + "/{fileId:guid}";

    /// <summary>Maps the routes into the client group, so they inherit the group's requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientAgentConversationFiles(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(FilesRoute, ListAsync)
            .RequirePermission(MailFathomPermission.AssistantUse);

        // The attribute is reached for its metadata, as every other write on this surface reaches it, so a body over the
        // bound is refused by the pipeline before it is buffered.
        api.MapPost(FilesRoute, TakeAsync)
            .WithMetadata(new RequestSizeLimitAttribute(AgentConversationFileBounds.MaximumFileOctets))
            .Accepts<Stream>(ClientDraftEndpoints.AnyUploadedMediaType)
            .RequirePermission(MailFathomPermission.AssistantUse);

        api.MapGet(FileRoute, DownloadAsync)
            .RequirePermission(MailFathomPermission.AssistantUse)
            .Produces<Stream>(StatusCodes.Status200OK, AttachmentContentResponse.FallbackMediaType);
    }

    /// <summary>Hands back one file a conversation holds, whole: one the person attached, or a report the Agent wrote.</summary>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="fileId">The file, as the conversation's list or an answer's block named it.</param>
    /// <param name="scopeResolver">Names the acting user, whose conversation it has to be.</param>
    /// <param name="files">The conversations' files.</param>
    /// <param name="context">The exchange, whose response is told the octets are not to be sniffed or cached.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The file's octets; <c>404</c> where this person's conversation holds no such file — the conversation deleted, the file gone, or either somebody else's; or <c>403</c> for a caller whose grant does not carry <c>mailfathom.assistant.use</c>.</returns>
    /// <remarks>
    /// Served as an attachment, under the media type its octets proved and with sniffing refused, for the reason a mail
    /// attachment is: a file a person uploaded is bytes from outside the deployment, and rendered inline from its origin
    /// it could be a page of its own there. Every absence answers alike, so asking says nothing about a conversation the
    /// caller cannot read. <c>no-store</c> keeps the octets out of every cache between the deployment and the reader.
    /// </remarks>
    internal static async Task<Results<FileContentHttpResult, NotFound>> DownloadAsync(
        [FromRoute] Guid conversationId,
        [FromRoute] Guid fileId,
        [FromServices] MailboxScopeResolver scopeResolver,
        [FromServices] AgentConversationFiles files,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(context);

        if (conversationId == Guid.Empty || fileId == Guid.Empty)
        {
            return TypedResults.NotFound();
        }

        var held = await files.ReadContentAsync(
            AgentConversationId.Create(conversationId),
            scopeResolver.User,
            AgentConversationFileId.Create(fileId),
            cancellationToken);

        if (held is null)
        {
            return TypedResults.NotFound();
        }

        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.CacheControl = "no-store";

        return TypedResults.File(
            held.Content.ToArray(),
            held.File.MediaType,
            held.File.Name ?? FallbackNameOf(held.File));
    }

    /// <summary>Names a file the upload gave no usable name, so a download still saves under one.</summary>
    private static string FallbackNameOf(AgentConversationFile file) =>
        file.Id.Value.ToString("N", CultureInfo.InvariantCulture);

    /// <summary>Lists the files one conversation holds, in the order they were taken in.</summary>
    /// <param name="conversationId">The conversation.</param>
    /// <param name="scopeResolver">Names the acting user, whose conversation it has to be.</param>
    /// <param name="files">The conversations' files.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><c>200</c> with the files, none where the conversation holds none or is not this person's, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.assistant.use</c>.</returns>
    internal static async Task<Ok<ClientAgentConversationFileListResponse>> ListAsync(
        [FromRoute] Guid conversationId,
        [FromServices] MailboxScopeResolver scopeResolver,
        [FromServices] AgentConversationFiles files,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(files);

        IReadOnlyList<AgentConversationFile> held = conversationId == Guid.Empty
            ? []
            : await files.ListAsync(AgentConversationId.Create(conversationId), scopeResolver.User, cancellationToken);

        return TypedResults.Ok(new ClientAgentConversationFileListResponse([.. held.Select(ClientAgentConversationFileResponse.Of)]));
    }

    /// <summary>Takes the file the request body carries into a conversation.</summary>
    /// <param name="conversationId">The conversation, which the client named and which is started where it is new.</param>
    /// <param name="scopeResolver">Names the acting user, whose conversation it is or becomes.</param>
    /// <param name="files">Takes the file in.</param>
    /// <param name="context">The request, whose body is the file and whose <c>Content-Disposition</c> names it.</param>
    /// <param name="cancellationToken">Cancels the intake.</param>
    /// <returns>
    /// <c>201</c> with the file and what reading it came to; <c>400</c> for an empty body; <c>404</c> where the
    /// conversation is somebody else's or none could be started; <c>409</c> where it already holds as many files as one
    /// may; <c>413</c> for a file over the bound; <c>415</c> for octets that are neither a document the Agent reads nor a
    /// picture it can be shown; or <c>403</c> for a caller whose grant does not carry <c>mailfathom.assistant.use</c>.
    /// </returns>
    /// <remarks>
    /// A document is read before this answers, so a document nothing could read is reported as unread here, with the
    /// reason, rather than discovered when a question reaches it.
    /// </remarks>
    internal static async Task<Results<Created<ClientAgentConversationFileResponse>, ProblemHttpResult>> TakeAsync(
        [FromRoute] Guid conversationId,
        [FromServices] MailboxScopeResolver scopeResolver,
        [FromServices] AgentConversationFiles files,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(context);

        if (conversationId == Guid.Empty)
        {
            return Refused(AgentConversationFileRefusal.NoSuchConversation);
        }

        using var buffer = new MemoryStream();

        try
        {
            await context.Request.Body.CopyToAsync(buffer, cancellationToken);
        }
        catch (BadHttpRequestException refusal) when (refusal.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return Refused(AgentConversationFileRefusal.TooLarge);
        }

        var conversation = AgentConversationId.Create(conversationId);
        var intake = await files.TakeAsync(
            conversation,
            scopeResolver.User,
            NameOf(context.Request),
            buffer.GetBuffer().AsMemory(0, (int)buffer.Length),
            cancellationToken);

        return intake is { File: { } taken }
            ? TypedResults.Created(ClientAgentConversationEndpoints.ReadAddressOf(conversation) + "/files", ClientAgentConversationFileResponse.Of(taken))
            : Refused(intake.Refusal ?? AgentConversationFileRefusal.KindNotTaken);
    }

    /// <summary>Reads the name a request's <c>Content-Disposition</c> gives the file, preferring the encoded form a non-ASCII name travels in.</summary>
    internal static string? NameOf(HttpRequest request) =>
        ContentDispositionHeaderValue.TryParse(request.Headers.ContentDisposition.ToString(), out var disposition)
            ? HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value
            : null;

    /// <summary>States why a file was not taken, without echoing anything it held.</summary>
    internal static ProblemHttpResult Refused(AgentConversationFileRefusal refusal) => refusal switch
    {
        AgentConversationFileRefusal.Empty => TypedResults.Problem(
            "An upload carries the file. A request with no body takes nothing in.",
            statusCode: StatusCodes.Status400BadRequest),
        AgentConversationFileRefusal.TooLarge => TypedResults.Problem(
            string.Create(CultureInfo.InvariantCulture, $"A file is at most {AgentConversationFileBounds.MaximumFileOctets / 1024 / 1024} MB."),
            statusCode: StatusCodes.Status413PayloadTooLarge),
        AgentConversationFileRefusal.KindNotTaken => TypedResults.Problem(
            "A file is a document the Agent reads — PDF, Word, Excel, PowerPoint, OpenDocument, or text — or a PNG, JPEG, GIF, or WebP picture, judged by what the file is rather than by its name.",
            statusCode: StatusCodes.Status415UnsupportedMediaType),
        AgentConversationFileRefusal.ConversationFull => TypedResults.Problem(
            string.Create(CultureInfo.InvariantCulture, $"A conversation holds at most {AgentConversationFileBounds.MaximumFilesPerConversation} files. Start another one."),
            statusCode: StatusCodes.Status409Conflict),
        _ => TypedResults.Problem(
            "This person holds no such conversation, and none can be started under that identifier.",
            statusCode: StatusCodes.Status404NotFound),
    };
}

/// <summary>The files a conversation holds.</summary>
/// <param name="Files">The files, in the order they were taken in.</param>
internal sealed record ClientAgentConversationFileListResponse(IReadOnlyList<ClientAgentConversationFileResponse> Files);

/// <summary>One file a conversation holds.</summary>
/// <param name="FileId">The file's identifier, which a question names to carry it.</param>
/// <param name="Name">The name the person's machine gave it, or <see langword="null" /> where it gave none usable.</param>
/// <param name="Kind"><c>document</c> or <c>picture</c>.</param>
/// <param name="MediaType">What its octets proved it to be.</param>
/// <param name="ByteLength">How many octets it holds.</param>
/// <param name="AddedAt">When it was taken in.</param>
/// <param name="Text">What reading a document's words came to, or <see langword="null" /> for a picture.</param>
internal sealed record ClientAgentConversationFileResponse(
    Guid FileId,
    string? Name,
    string Kind,
    string MediaType,
    long ByteLength,
    DateTimeOffset AddedAt,
    ClientAgentConversationFileTextResponse? Text)
{
    /// <summary>Maps a conversation's file onto the wire.</summary>
    /// <param name="file">The file.</param>
    /// <returns>Its wire form.</returns>
    internal static ClientAgentConversationFileResponse Of(AgentConversationFile file) => new(
        file.Id.Value,
        file.Name,
        file.Kind is AgentConversationFileKind.Picture ? "picture" : "document",
        file.MediaType,
        file.ByteLength,
        file.AddedAt,
        file.Reading is { } reading ? ClientAgentConversationFileTextResponse.Of(reading) : null);
}

/// <summary>What reading one document's words came to.</summary>
/// <param name="State"><c>whole</c> where the Agent is given it whole, <c>long</c> where it reads and searches it a page at a time, or <c>unread</c> where nothing could be read out of it.</param>
/// <param name="Reason">Why nothing was read, for an unread document: the attachment text reader's outcome, such as <c>encrypted</c> or <c>malformed</c>; <c>extracted</c> for a document that holds no words, such as a scan.</param>
/// <param name="PageCount">How many pages, slides, or sheets its words were read into.</param>
/// <param name="PageKind"><c>page</c>, <c>slide</c>, or <c>sheet</c>, or <see langword="null" /> where nothing marked any.</param>
/// <param name="Characters">How many characters its words hold.</param>
internal sealed record ClientAgentConversationFileTextResponse(
    string State,
    string? Reason,
    int PageCount,
    string? PageKind,
    int Characters)
{
    /// <summary>Maps a reading onto the wire.</summary>
    /// <param name="reading">The reading.</param>
    /// <returns>Its wire form.</returns>
    internal static ClientAgentConversationFileTextResponse Of(AgentConversationFileReading reading) => new(
        !reading.WasRead ? "unread" : reading.IsLong ? "long" : "whole",
        reading.WasRead ? null : CamelCase(reading.Outcome.ToString()),
        reading.PageCount,
        reading.PageKind is { } kind ? CamelCase(kind.ToString()) : null,
        reading.TextLength);

    private static string CamelCase(string name) =>
        string.Create(CultureInfo.InvariantCulture, $"{char.ToLowerInvariant(name[0])}{name[1..]}");
}
