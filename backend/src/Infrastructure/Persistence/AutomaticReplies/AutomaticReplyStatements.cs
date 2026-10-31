// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.AutomaticReplies;

namespace MailFathom.Infrastructure.Persistence.AutomaticReplies;

/// <summary>Composes the two statements whose outcome depends on what another replica may be writing at the same moment.</summary>
/// <remarks>
/// SQL rather than the change tracker because each is one statement whose answer is the rows it changed: an upsert that
/// saves for a user this deployment holds and nobody else, and an insert a competing claim turns into a no-op. Every
/// value is a parameter; the identifiers are quoted because EF Core names the columns after the properties.
/// </remarks>
internal static class AutomaticReplyStatements
{
    /// <summary>Composes the save: the reply under a new revision, and the forgetting of every sender an older one answered.</summary>
    /// <param name="user">The user the reply is saved for.</param>
    /// <param name="revision">The revision the save mints.</param>
    /// <param name="reply">The reply.</param>
    /// <param name="savedAt">When it was saved.</param>
    /// <returns>The statement, whose one row is the revision saved, and which returns none for a user this deployment does not hold.</returns>
    internal static FormattableString ComposeSave(Guid user, Guid revision, AutomaticReply reply, DateTimeOffset savedAt)
    {
        ArgumentNullException.ThrowIfNull(reply);

        var insideText = reply.InsideText;
        var outsideText = reply.OutsideText;
        string[] accountIds = [.. reply.Accounts.Select(static account => account.Value)];
        var startsAt = reply.StartsAt;
        var endsAt = reply.EndsAt;
        var isOn = reply.IsOn;

        return $"""
                WITH saved AS (
                    INSERT INTO automatic_replies
                        ("UserId", "Revision", "InsideText", "OutsideText", "AccountIds", "StartsAt", "EndsAt", "IsOn", "UpdatedAt")
                    SELECT {user}, {revision}, {insideText}::text, {outsideText}::text, {accountIds}, {startsAt}, {endsAt}, {isOn}, {savedAt}
                    FROM settings_accounts
                    WHERE "Id" = {user}
                    ON CONFLICT ("UserId") DO UPDATE SET
                        "Revision" = EXCLUDED."Revision",
                        "InsideText" = EXCLUDED."InsideText",
                        "OutsideText" = EXCLUDED."OutsideText",
                        "AccountIds" = EXCLUDED."AccountIds",
                        "StartsAt" = EXCLUDED."StartsAt",
                        "EndsAt" = EXCLUDED."EndsAt",
                        "IsOn" = EXCLUDED."IsOn",
                        "UpdatedAt" = EXCLUDED."UpdatedAt"
                    RETURNING "UserId", "Revision"
                ),
                forgotten AS (
                    DELETE FROM automatic_reply_answers
                    WHERE "UserId" IN (SELECT "UserId" FROM saved)
                      AND "Revision" <> {revision}
                )
                SELECT "Revision" AS "Value" FROM saved
                """;
    }

    /// <summary>Composes the claim of the one reply a sender is owed under a revision.</summary>
    /// <param name="user">The user whose reply answers.</param>
    /// <param name="revision">The revision the sender is answered under.</param>
    /// <param name="senderAddress">The sender, in its comparison form.</param>
    /// <param name="answeredAt">When the sender was answered.</param>
    /// <returns>The statement, which inserts one row when the claim is this caller's and none when it is not.</returns>
    /// <remarks>
    /// The insert reads the revision from the reply's own row, so a claim made under a revision a save has just replaced
    /// inserts nothing rather than recording a sender against a reply that no longer exists.
    /// </remarks>
    internal static FormattableString ComposeClaim(
        Guid user,
        Guid revision,
        string senderAddress,
        DateTimeOffset answeredAt) =>
        $"""
         INSERT INTO automatic_reply_answers ("UserId", "Revision", "SenderAddress", "AnsweredAt")
         SELECT "UserId", "Revision", {senderAddress}, {answeredAt}
         FROM automatic_replies
         WHERE "UserId" = {user} AND "Revision" = {revision}
         ON CONFLICT DO NOTHING
         """;
}
