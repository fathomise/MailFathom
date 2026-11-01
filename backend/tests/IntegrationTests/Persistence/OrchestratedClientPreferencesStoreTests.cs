// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Preferences;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves a person's preferences survive the upsert and the read against a real PostgreSQL database.</summary>
/// <remarks>
/// The document's own rendering and parsing are the unit suite's; what only this suite settles is that the upsert
/// statement composed from the model writes the column the read selects, and that a second write replaces the first
/// rather than adding a row beside it. The list mode is the preference stated, because it is the one whose two values
/// differ from each other and from nothing else.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedClientPreferencesStoreTests(MailFathomOrchestrationFixture orchestration)
{
    [Fact]
    public async Task SaveAsync_TwoWritesForOnePerson_ReadsBackTheSecond()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        // Act
        var readingByConversation = await SaveAndReadAsync(
            services,
            ClientPreferences.Unset with { ListMode = ClientMailListMode.Threads, NotificationSeconds = 12 },
            cancellationToken);
        var readingByMessage = await SaveAndReadAsync(services, ClientPreferences.Unset, cancellationToken);

        // Assert
        Assert.Equal((ClientMailListMode.Threads, 12), (readingByConversation.ListMode, readingByConversation.NotificationSeconds));
        Assert.Equal((ClientMailListMode.Messages, 5), (readingByMessage.ListMode, readingByMessage.NotificationSeconds));
    }

    private static Task<ClientPreferences> SaveAndReadAsync(
        OrchestratedMailFathomServices services,
        ClientPreferences preferences,
        CancellationToken cancellationToken) => services.InScopeAsync(
        async (scope, token) =>
        {
            var store = scope.GetRequiredService<IClientPreferencesStore>();

            Assert.True(await store.SaveAsync(services.ServedUser, preferences, token));

            return Assert.IsType<ClientPreferences>(await store.ReadAsync(services.ServedUser, token));
        },
        cancellationToken);
}
