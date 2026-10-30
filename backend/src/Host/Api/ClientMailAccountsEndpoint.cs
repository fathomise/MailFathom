// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Spam.Actions;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the signed-in user's mail accounts and how current the local copy of each one is.</summary>
/// <remarks>
/// <para>
/// It is the first route on this surface that answers with something about mail at all, and it is the one a client reads
/// before it can draw anything: a copy of a mailbox is only worth showing beside a statement of when it was last
/// reconciled and whether the deployment can still reach it, and those are separable facts a screen must not blur.
/// </para>
/// <para>
/// What it returns is the accounts the request's acting user is assigned. It composes no deployment-wide catalog: an account
/// another user holds is absent exactly as an account this deployment does not serve is absent, with nothing in the
/// response, its timing, or its failure modes separating the two. A user who owns none is answered with an empty
/// collection, which is a state a client renders rather than an error, and is a different answer from the refusal a
/// credential without the grant receives.
/// </para>
/// <para>
/// Nothing of the mailbox reaches it beyond an account's own identity: no message, no subject, no correspondent, no
/// folder listing, and no mail server, port, user name, or credential. The answer is bounded by how many accounts the
/// user has and by nothing else — not by their folders, their messages, or how many times synchronization has run.
/// </para>
/// </remarks>
internal static class ClientMailAccountsEndpoint
{
    /// <summary>The route reporting the user's accounts, relative to the client prefix.</summary>
    internal const string MailAccountsRoute = "/accounts";

    /// <summary>Maps the route into the client group, so it inherits the group's requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientMailAccounts(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(MailAccountsRoute, ReadAccountsAsync)
            .RequirePermission(MailFathomPermission.MailRead);
    }

    /// <summary>Reports the acting user's accounts and their synchronization freshness.</summary>
    /// <param name="reader">Reads the user's accounts and reduces each one's folders to a single reading.</param>
    /// <param name="spamActions">Reads whether each account's spam verdicts move its mail.</param>
    /// <param name="assignments">Reads which of the accounts more than one user is assigned.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the user's accounts, empty where they own none, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.read</c>.</returns>
    /// <remarks>It speaks to no mail server, so a client request cannot wait on IMAP and cannot set the remote <c>\Seen</c> flag.</remarks>
    internal static async Task<Ok<ClientMailAccountsResponse>> ReadAccountsAsync(
        [FromServices] MailAccountFreshnessReader reader,
        [FromServices] ISpamActionSettingsReader spamActions,
        [FromServices] IMailAccountAssignments assignments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(assignments);

        var accounts = await reader.ReadAsync(MailboxReaderKind.Person, cancellationToken);

        return TypedResults.Ok(await ClientMailAccountsResponse.ForAsync(accounts, spamActions, assignments, cancellationToken));
    }
}

/// <summary>What the client endpoint reports about the user's mail accounts.</summary>
/// <param name="SynchronizationEnabled">Whether this deployment refreshes the local copy of these accounts at all.</param>
/// <param name="Accounts">One entry per account the acting user is assigned, ordered by identifier, empty where they are assigned none.</param>
/// <remarks>
/// The switch is reported beside the accounts because no per-account value carries it: a copy that last moved a week ago
/// means one thing where the deployment is still trying and another where it has stopped, and a client that could not
/// tell the two apart would show every account as failing or none of them.
/// </remarks>
internal sealed record ClientMailAccountsResponse(
    bool SynchronizationEnabled,
    IReadOnlyList<ClientMailAccountResponse> Accounts)
{
    /// <summary>Describes the user's accounts on the wire, reading which of them are shared from the assignments.</summary>
    /// <param name="accounts">What the use case answered.</param>
    /// <param name="spamActions">Reads whether each account's spam verdicts move its mail.</param>
    /// <param name="assignments">Reads which of the accounts more than one user is assigned.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    internal static async Task<ClientMailAccountsResponse> ForAsync(
        MailAccountFreshnessDirectory accounts,
        ISpamActionSettingsReader spamActions,
        IMailAccountAssignments assignments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(assignments);

        var sharedAccounts = await assignments.ReadSharedAmongAsync(
            accounts.Accounts.Select(static account => account.Account.Id),
            cancellationToken);

        return For(accounts, spamActions, sharedAccounts);
    }

    /// <summary>Describes the user's accounts on the wire.</summary>
    /// <param name="accounts">What the use case answered.</param>
    /// <param name="spamActions">Reads whether each account's spam verdicts move its mail.</param>
    /// <param name="sharedAccounts">The accounts among these that more than one user is assigned.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="accounts" />, <paramref name="spamActions" />, or <paramref name="sharedAccounts" /> is <see langword="null" />.</exception>
    internal static ClientMailAccountsResponse For(
        MailAccountFreshnessDirectory accounts,
        ISpamActionSettingsReader spamActions,
        IReadOnlySet<MailAccountId> sharedAccounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(spamActions);
        ArgumentNullException.ThrowIfNull(sharedAccounts);

        return new ClientMailAccountsResponse(
            accounts.SynchronizationEnabled,
            [.. accounts.Accounts.Select(account => ClientMailAccountResponse.For(account, spamActions, sharedAccounts))]);
    }
}

/// <summary>One of the user's accounts, and how current its local copy is.</summary>
/// <param name="Id">The identifier the deployment generated for the account, which a client may hold and name it by; it is unique across the deployment.</param>
/// <param name="DisplayName">The name the account is published under, which is what a person recognizes; it is unique only within the user.</param>
/// <param name="SynchronizationState">Whether the deployment's last attempt at the account succeeded, failed, found no mail server, or has never happened, as the state's own name.</param>
/// <param name="LastSynchronizedAt">When the account last durably took anything in, or <see langword="null" /> where it never has.</param>
/// <param name="Behind">Whether any of the account's folders ended its last attempt with mail it had not yet taken in.</param>
/// <param name="FilesJunk">Whether a spam verdict on the account's mail moves it into the junk folder, and the person's not-spam verdict moves it out to the inbox.</param>
/// <param name="Shared">Whether the account is assigned to more than one user, so others read what arrives there.</param>
/// <remarks>
/// <para>
/// The three answer different parts of one question and are published apart for that reason. The timestamp says how old
/// what is being read is, the state says whether it is still being refreshed, and being behind says whether there is
/// known to be more to come — an account that has been failing since yesterday, one nobody has written to since
/// yesterday, and one still catching up carry the same instant and are three different situations. A mailbox this
/// deployment cannot currently reach is a state of its own for the same reason: it is waited out rather than repaired.
/// </para>
/// <para>
/// The junk switch is published because a person's own spam and not-spam marks follow it, and a client has to say before
/// the press whether a mark will move the message — a not-spam mark that moves mail out of the junk folder is confirmed,
/// and one that moves nothing is not. It is read from the same reader the mark route decides its moves from.
/// </para>
/// <para>
/// Being shared is published so a client can mark the mailbox wherever it is drawn or chosen. It is one fact about the
/// assignment and names none of the other users.
/// </para>
/// </remarks>
internal sealed record ClientMailAccountResponse(
    string Id,
    string DisplayName,
    string SynchronizationState,
    DateTimeOffset? LastSynchronizedAt,
    bool Behind,
    bool FilesJunk,
    bool Shared)
{
    /// <summary>Describes one account on the wire.</summary>
    /// <param name="account">The account's freshness.</param>
    /// <param name="spamActions">Reads whether the account's spam verdicts move its mail.</param>
    /// <param name="sharedAccounts">The accounts that more than one user is assigned.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="account" />, <paramref name="spamActions" />, or <paramref name="sharedAccounts" /> is <see langword="null" />.</exception>
    internal static ClientMailAccountResponse For(
        MailAccountFreshness account,
        ISpamActionSettingsReader spamActions,
        IReadOnlySet<MailAccountId> sharedAccounts)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(spamActions);
        ArgumentNullException.ThrowIfNull(sharedAccounts);

        return new(
            account.Account.Id.Value,
            account.Account.DisplayName.Value,
            account.State.ToString(),
            account.LastSynchronizedAt,
            account.IsBehind,
            spamActions.ActionsFor(account.Account.Id).FilesJunk,
            sharedAccounts.Contains(account.Account.Id));
    }
}
