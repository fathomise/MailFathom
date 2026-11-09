// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Evaluations.UnitTests.Providers;

/// <summary>The response a provider's client library hands a <see cref="ClientResultException" />, carrying a status and the headers it answered with.</summary>
/// <param name="status">The HTTP status the provider answered with.</param>
/// <param name="headers">The headers the provider answered with.</param>
internal sealed class FakeProviderResponse(int status, params (string Name, string Value)[] headers) : PipelineResponse
{
    public override int Status => status;

    public override string ReasonPhrase => string.Empty;

    public override Stream? ContentStream { get; set; }

    public override BinaryData Content => BinaryData.Empty;

    protected override PipelineResponseHeaders HeadersCore { get; } = new FakeHeaders(headers);

    /// <summary>Raises the failure a client library raises over this response.</summary>
    /// <param name="status">The HTTP status the provider answered with.</param>
    /// <param name="headers">The headers the provider answered with.</param>
    /// <returns>The failure.</returns>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The failure keeps the response as its raw response for as long as it lives, as a client library's does, and this response holds nothing to release.")]
    public static ClientResultException Refusal(int status, params (string Name, string Value)[] headers) =>
        new(new FakeProviderResponse(status, headers));

    public override BinaryData BufferContent(CancellationToken cancellationToken = default) => BinaryData.Empty;

    public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(BinaryData.Empty);

    public override void Dispose()
    {
    }

    private sealed class FakeHeaders((string Name, string Value)[] headers) : PipelineResponseHeaders
    {
        public override bool TryGetValue(string name, out string? value)
        {
            value = headers.FirstOrDefault(header => string.Equals(header.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

            return value is not null;
        }

        public override bool TryGetValues(string name, out IEnumerable<string>? values)
        {
            values = this.TryGetValue(name, out var value) ? [value!] : null;

            return values is not null;
        }

        public override IEnumerator<KeyValuePair<string, string>> GetEnumerator() =>
            headers.Select(static header => KeyValuePair.Create(header.Name, header.Value)).GetEnumerator();
    }
}
