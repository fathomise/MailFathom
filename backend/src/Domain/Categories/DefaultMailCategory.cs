// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.Domain.Categories;

/// <summary>One of the seven answers to what sort of mail a message is.</summary>
/// <remarks>
/// <para>
/// A closed enumeration rather than an enum because each member publishes an identity: it is written into a stored
/// record, named in an account's list of switched-off defaults, and read by whatever asks for a category, so it has to
/// survive a rename of the member. An identity is allocated once and never reused.
/// </para>
/// <para>
/// None of them names health, religion, politics, or anything else that would infer special-category data from
/// somebody's mail.
/// </para>
/// <para>
/// <c>default</c> is reachable and reports itself through <see cref="IsSpecified" />. <see cref="TryParse" /> never
/// yields a specified value from an undeclared identity, and <see cref="DefaultMailCategoryJsonConverter" /> refuses to
/// write the default.
/// </para>
/// </remarks>
[JsonConverter(typeof(DefaultMailCategoryJsonConverter))]
public readonly record struct DefaultMailCategory
{
    private readonly string? identity;

    private DefaultMailCategory(string identity) => this.identity = identity;

    /// <summary>Gets the category of a person writing to the reader.</summary>
    public static DefaultMailCategory Correspondence { get; } = new("correspondence");

    /// <summary>Gets the category of newsletters and offers from senders the reader signed up with or buys from.</summary>
    public static DefaultMailCategory Subscriptions { get; } = new("subscriptions");

    /// <summary>Gets the category of invoices, bills, statements, and payment confirmations.</summary>
    public static DefaultMailCategory Finance { get; } = new("finance");

    /// <summary>Gets the category of orders and deliveries.</summary>
    public static DefaultMailCategory Shopping { get; } = new("shopping");

    /// <summary>Gets the category of bookings and tickets.</summary>
    public static DefaultMailCategory Travel { get; } = new("travel");

    /// <summary>Gets the category of automated notices from services and accounts, security notices included.</summary>
    public static DefaultMailCategory Notifications { get; } = new("notifications");

    /// <summary>Gets the category of mail nothing above fits.</summary>
    public static DefaultMailCategory Other { get; } = new("other");

    /// <summary>Gets every default category.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<DefaultMailCategory> All { get; } =
        [Correspondence, Subscriptions, Finance, Shopping, Travel, Notifications, Other];

    /// <summary>Gets whether this value names a declared category rather than the unusable struct default.</summary>
    public bool IsSpecified => this.identity is not null;

    /// <summary>Reads a published identity.</summary>
    /// <param name="identity">The identity, compared exactly.</param>
    /// <param name="category">The category, or the unspecified default where nothing declares that identity.</param>
    /// <returns><see langword="true" /> when <paramref name="identity" /> names a declared category.</returns>
    public static bool TryParse(string? identity, out DefaultMailCategory category)
    {
        category = All.FirstOrDefault(candidate => string.Equals(candidate.identity, identity, StringComparison.Ordinal));

        return category.IsSpecified;
    }

    /// <inheritdoc />
    public override string ToString() => this.identity ?? "(unspecified)";
}

/// <summary>Serializes <see cref="DefaultMailCategory" /> as its published identity.</summary>
public sealed class DefaultMailCategoryJsonConverter : JsonConverter<DefaultMailCategory>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not a string or names no declared category.</exception>
    public override DefaultMailCategory Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? ParseOrThrow(reader.GetString())
            : throw new JsonException($"A default mail category must be a JSON string, but the token was {reader.TokenType}.");

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void Write(
        Utf8JsonWriter writer,
        DefaultMailCategory value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(IdentityOrThrow(value));
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the property name names no declared category.</exception>
    public override DefaultMailCategory ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) => ParseOrThrow(reader.GetString());

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        DefaultMailCategory value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WritePropertyName(IdentityOrThrow(value));
    }

    private static DefaultMailCategory ParseOrThrow(string? identity) =>
        DefaultMailCategory.TryParse(identity, out var category)
            ? category
            : throw new JsonException($"'{identity}' is not a default mail category.");

    private static string IdentityOrThrow(DefaultMailCategory category) => category.IsSpecified
        ? category.ToString()
        : throw new JsonException("An unspecified default mail category cannot be serialized.");
}
