// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Persistence;
using MailFathom.Infrastructure;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Mail.Signing;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Secrets.References;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Composes the secret validator the two places that judge a user's mail accounts both need.</summary>
/// <remarks>
/// The validator is a concrete type with ten collaborators, and both the startup gate and the record administration
/// take it, so a copy per suite would be the same ten lines written twice and free to drift. Nothing here reaches a
/// file, a variable, or a database: the resolver hands back the reference's own target, and the connection settings are
/// answered by a stub.
/// </remarks>
internal static class SecretValidation
{
    /// <summary>Builds a validator that resolves a reference under any scheme a deployment registers, and no other.</summary>
    /// <returns>The validator.</returns>
    internal static SecretConfigurationValidator OverRegisteredSchemes() => Over(new RegisteredSchemeSecretReferenceResolver());

    /// <summary>Builds a validator over a resolver the test states, which is how material that has to parse is handed in.</summary>
    /// <param name="resolver">What every reference the validator reads resolves to.</param>
    /// <returns>The validator.</returns>
    internal static SecretConfigurationValidator Over(ISecretReferenceResolver resolver)
    {
        return new SecretConfigurationValidator(
            resolver,
            new TrustAnchorLoader(resolver),
            new SmimeSigningCertificateValidator(resolver, new FakeTimeProvider()),
            new OpenPgpSigningKeyValidator(resolver, new FakeTimeProvider()),
            new DatabaseConnectionSettingsMapper(new ConfigurationBuilder().Build()),
            new StubDatabaseConnectionSettingsValidator(),
            PostgresTextSearchConfiguration.Default,
            new DatabaseCommandTimeout(
                TimeSpan.FromSeconds(HostApplicationBuilderExtensions.DefaultDatabaseCommandTimeoutSeconds)),
            new FakeTimeProvider(),
            new RecordingLogger<SecretConfigurationValidator>());
    }
}
