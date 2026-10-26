// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Net;
using System.Text;
using MailFathom.AI.DecisionModels;
using MailFathom.AI.Providers;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>The last hop of a decision request, answering from a script so a free run reaches no provider.</summary>
/// <remarks>
/// A handler is made per request the factory asks for and forwards here, because the factory owns and disposes what it
/// is handed while a test reads the count afterwards. Every layer above this one is the deployment's own.
/// </remarks>
/// <param name="plan">The model a deployment would have declared.</param>
/// <param name="status">The status every request is answered with.</param>
/// <param name="body">The body every request is answered with.</param>
internal sealed class ScriptedDecisionEndpoint(DecisionModelPlan plan, HttpStatusCode status, string body)
{
    private int requests;

    /// <summary>Gets the model a deployment would have declared.</summary>
    internal DecisionModelPlan Plan { get; } = plan;

    /// <summary>Gets how many requests reached the endpoint.</summary>
    internal int Requests => Volatile.Read(ref this.requests);

    /// <summary>Makes a handler answering every request this endpoint's way.</summary>
    /// <returns>A handler the factory owns.</returns>
    internal HttpMessageHandler CreateHandler() => new Relay(this);

    private HttpResponseMessage Answer(HttpRequestMessage request)
    {
        Interlocked.Increment(ref this.requests);

        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
            RequestMessage = request,
        };
    }

    /// <summary>Presents no credential, which is what a scripted endpoint asks for.</summary>
    internal sealed class NoCredential : IProviderEndpointCredentialSource
    {
        /// <inheritdoc />
        public Task<ProviderEndpointCredential> ResolveAsync(string endpointAlias, CancellationToken cancellationToken) =>
            Task.FromResult(ProviderEndpointCredential.Unauthenticated());
    }

    private sealed class Relay(ScriptedDecisionEndpoint endpoint) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(endpoint.Answer(request));
    }
}
