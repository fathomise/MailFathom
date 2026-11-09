// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MailFathom.AppHost;

internal sealed class DevelopmentCredentialProvisioner(HttpClient client, TimeProvider timeProvider)
{
    /// <summary>The label the local launch records its user under where the database holds nobody.</summary>
    private const string RecordedUserDisplayName = "user";

    /// <summary>The seeded role whose permissions the local user is given, found by name because its identifier is minted per database.</summary>
    private const string UserRoleName = "User";

    /// <summary>The language the local mailbox is declared in, which every account states.</summary>
    private const string MailAccountLanguage = "English";

    private static readonly TimeSpan ReadinessRetryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Waits for the local host to start, then names the one user it holds, recording them first where it holds nobody.</summary>
    /// <returns>That user.</returns>
    /// <remarks>
    /// The local launch is a quick start, so it records the user a fresh database lacks rather than leaving the developer a
    /// step before anything is provisioned — through the administrative API, as <c>mfctl user add</c> would. Nothing a
    /// deployment runs does this: a deployment's first user is the one its administrator records.
    /// </remarks>
    internal async Task<Guid> WaitForSoleServedUserAsync(
        Uri startedEndpoint,
        Uri adminEndpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startedEndpoint);
        ArgumentNullException.ThrowIfNull(adminEndpoint);

        await this.WaitForStartedAsync(startedEndpoint, cancellationToken);

        return await this.ReadSoleServedUserAsync(adminEndpoint, cancellationToken);
    }

    internal async Task<bool> EnsureCredentialAsync(
        Uri adminEndpoint,
        Guid user,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(adminEndpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        if (await this.CredentialExistsAsync(adminEndpoint, user, username, cancellationToken))
        {
            return false;
        }

        var requestBody = new JsonObject
        {
            ["method"] = "password",
            ["username"] = username,
            ["password"] = password,
            ["permissions"] = null,
        };
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(adminEndpoint, $"api/admin/users/{user:D}/credentials"))
        {
            Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        return true;
    }

    /// <summary>Creates the mailbox this run collected for the served user, unless an account for its address is already assigned to them.</summary>
    /// <param name="adminEndpoint">The administrative surface the account is written through.</param>
    /// <param name="user">The user the mailbox is assigned to.</param>
    /// <param name="host">The IMAP server the mailbox is read from.</param>
    /// <param name="userName">The login the mailbox is reached under.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns><see langword="true" /> when this call created the account, and <see langword="false" /> when one was already assigned.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the deployment refused the declaration.</exception>
    /// <remarks>
    /// <para>
    /// The password travels as an <c>env:</c> reference to the variable the app model hands the host, never as the value:
    /// a persisted record refuses <c>plaintext:</c>, and the password then stays out of the database altogether.
    /// </para>
    /// <para>
    /// The address is the login where the login is one, which is what a mail provider ordinarily hands out, and is
    /// composed from the login and the server otherwise, so a local server keyed by bare user names still gets an account
    /// told apart from every other.
    /// </para>
    /// </remarks>
    internal async Task<bool> EnsureMailAccountAsync(
        Uri adminEndpoint,
        Guid user,
        string host,
        string userName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(adminEndpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);

        var emailAddress = userName.Contains('@', StringComparison.Ordinal) ? userName : $"{userName}@{host}";

        if (await this.AssignsAccountForAsync(adminEndpoint, user, emailAddress, cancellationToken))
        {
            return false;
        }

        var account = new JsonObject
        {
            ["EmailAddress"] = emailAddress,
            ["DisplayName"] = OrchestrationContract.DevelopmentMailAccountDisplayName,
            ["Host"] = host,
            ["UserName"] = userName,
            ["Language"] = MailAccountLanguage,
            ["Secrets"] = new JsonObject
            {
                ["Password"] = new JsonObject
                {
                    ["Name"] = OrchestrationContract.DevelopmentMailAccountPasswordName,
                    ["SecretReference"] = $"env:{OrchestrationContract.DevelopmentMailAccountPasswordVariable}",
                },
            },
        };

        var requestBody = new JsonObject
        {
            ["userId"] = user.ToString("D"),
            ["account"] = account.ToJsonString(),
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(adminEndpoint, "api/admin/mail-accounts"))
        {
            Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        using var response = await client.SendAsync(request, cancellationToken);

        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        if (!document.RootElement.GetProperty("committed").GetBoolean())
        {
            throw new InvalidOperationException(
                $"The local MailFathom host refused the mailbox declaration: {RefusalOf(document.RootElement)}");
        }

        return true;
    }

    /// <summary>Gives the served user the seeded <c>User</c> role at their own scope, unless it is already given there.</summary>
    /// <param name="adminEndpoint">The administrative surface the role is read and assigned through.</param>
    /// <param name="user">The user the role is given to.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns><see langword="true" /> when this call assigned the role, and <see langword="false" /> when it was already assigned.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the deployment holds no role named <c>User</c>.</exception>
    /// <remarks>
    /// A user holds only what their role assignments grant, and the local credential names no permissions of its own, so
    /// without this the client signs in and every data route refuses it.
    /// </remarks>
    internal async Task<bool> EnsureUserRoleAsync(Uri adminEndpoint, Guid user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(adminEndpoint);

        var role = await this.ReadRoleNamedAsync(adminEndpoint, UserRoleName, cancellationToken);
        var requestBody = new JsonObject
        {
            ["roleId"] = role.ToString("D"),
            ["principalKind"] = "user",
            ["principalId"] = user.ToString("D"),
            ["scopeKind"] = "user",
            ["scopeId"] = user.ToString("D"),
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(adminEndpoint, "api/admin/role-assignments"))
        {
            Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        using var response = await client.SendAsync(request, cancellationToken);

        // The deployment answers 409 when the same role is already given to the same user at the same scope.
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();

        return true;
    }

    private static string RefusalOf(JsonElement outcome) => string.Join(
        " ",
        outcome.GetProperty("messages").EnumerateArray().Select(static message => message.GetString()));

    private static bool HoldsAddress(JsonElement account, string emailAddress) =>
        account.TryGetProperty("emailAddress", out var held)
        && string.Equals(held.GetString()?.Trim(), emailAddress, StringComparison.OrdinalIgnoreCase);

    private async Task<bool> AssignsAccountForAsync(
        Uri adminEndpoint,
        Guid user,
        string emailAddress,
        CancellationToken cancellationToken)
    {
        // Reads the listing's first page alone, which holds every account of a local development deployment;
        // follow its nextCursor if one is ever seeded with more accounts than a page holds.
        using var response = await client.GetAsync(
            new Uri(adminEndpoint, "api/admin/mail-accounts"),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);

        return document.RootElement
            .GetProperty("accounts")
            .EnumerateArray()
            .Any(account =>
                account.GetProperty("users").EnumerateArray().Any(assigned => assigned.GetGuid() == user)
                && HoldsAddress(account, emailAddress));
    }

    private async Task<Guid> ReadRoleNamedAsync(Uri adminEndpoint, string name, CancellationToken cancellationToken)
    {
        // Reads the listing's first page alone, which holds the seeded roles of a local development deployment;
        // follow its nextCursor if one is ever given more roles than a page holds.
        using var response = await client.GetAsync(
            new Uri(adminEndpoint, "api/admin/roles"),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);

        return document.RootElement
            .GetProperty("roles")
            .EnumerateArray()
            .Where(role => string.Equals(role.GetProperty("name").GetString(), name, StringComparison.Ordinal))
            .Select(static role => (Guid?)role.GetProperty("id").GetGuid())
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"The local MailFathom host holds no role named '{name}'.");
    }

    private async Task WaitForStartedAsync(Uri startedEndpoint, CancellationToken cancellationToken)
    {
        var deadline = timeProvider.GetUtcNow() + ReadinessTimeout;

        while (true)
        {
            try
            {
                using var response = await client.GetAsync(
                    startedEndpoint,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                if (response.StatusCode != HttpStatusCode.ServiceUnavailable)
                {
                    response.EnsureSuccessStatusCode();
                }
            }
            catch (HttpRequestException) when (!cancellationToken.IsCancellationRequested)
            {
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }

            if (timeProvider.GetUtcNow() >= deadline)
            {
                throw new TimeoutException("The local MailFathom host did not report startup readiness within two minutes.");
            }

            await Task.Delay(ReadinessRetryDelay, timeProvider, cancellationToken);
        }
    }

    private async Task<Guid> ReadSoleServedUserAsync(Uri adminEndpoint, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            new Uri(adminEndpoint, "api/admin/users"),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
        // Only a user served on a mail endpoint counts: the default administrator every first start records is on the
        // roster with both switches off, and giving it the local mailbox would serve that mail to nobody.
        var users = document.RootElement
            .GetProperty("users")
            .EnumerateArray()
            .Where(static user => user.GetProperty("mcpEndpoint").GetBoolean() || user.GetProperty("clientEndpoint").GetBoolean())
            .Select(static user => user.GetProperty("id").GetGuid())
            .ToArray();

        return users switch
        {
            [] => await this.RecordUserAsync(adminEndpoint, cancellationToken),
            [var only] => only,
            _ => throw new InvalidOperationException(
                $"The normal Aspire launch expected one user served on a mail endpoint but found {users.Length.ToString(CultureInfo.InvariantCulture)}."),
        };
    }

    private async Task<Guid> RecordUserAsync(Uri adminEndpoint, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(adminEndpoint, "api/admin/users"))
        {
            Content = new StringContent(
                new JsonObject { ["displayName"] = RecordedUserDisplayName }.ToJsonString(),
                Encoding.UTF8,
                "application/json"),
        };
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);

        return document.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<bool> CredentialExistsAsync(
        Uri adminEndpoint,
        Guid user,
        string username,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            new Uri(adminEndpoint, $"api/admin/users/{user:D}/credentials"),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);

        return document.RootElement
            .GetProperty("credentials")
            .EnumerateArray()
            .Any(credential =>
                string.Equals(credential.GetProperty("method").GetString(), "password", StringComparison.Ordinal)
                && string.Equals(credential.GetProperty("lookup").GetString(), username, StringComparison.OrdinalIgnoreCase));
    }
}
