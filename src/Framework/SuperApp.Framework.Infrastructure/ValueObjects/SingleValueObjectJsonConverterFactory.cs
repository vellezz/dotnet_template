using SuperApp.Framework.Domain.ValueObjects;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SuperApp.Framework.Infrastructure.ValueObjects;

/// <summary>
/// System.Text.Json converter that writes strongly typed IDs and single-value objects as their primitive value and reads them through
/// <c>Create</c>, so invalid input is rejected (ADR-0023, ADR-0024).
/// </summary>
/// <remarks>
/// Registered by <c>HostingExtensions.ConfigureJson</c>. A value that fails validation raises <see cref="JsonException"/>, which ASP.NET Core
/// turns into HTTP 400 during model binding.
/// </remarks>
public sealed class SingleValueObjectJsonConverterFactory : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) => SingleValueObjectTypes.TryGetValueType(typeToConvert, out _);

    /// <summary>Creates the converter for a type accepted by <see cref="CanConvert"/>.</summary>
    /// <param name="typeToConvert">The ID or value object type.</param>
    /// <param name="options">The serializer options in use.</param>
    /// <returns>A converter that serializes the value object as its primitive value and validates it on read.</returns>
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        SingleValueObjectTypes.TryGetValueType(typeToConvert, out var valueType);
        return (JsonConverter)Activator.CreateInstance(typeof(SingleValueObjectJsonConverter<,>).MakeGenericType(typeToConvert, valueType!))!;
    }

    private sealed class SingleValueObjectJsonConverter<TSelf, TValue> : JsonConverter<TSelf>
        where TSelf : struct, ISingleValueObject<TSelf, TValue>
        where TValue : notnull
    {
        public override TSelf Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = JsonSerializer.Deserialize<TValue>(ref reader, options)
                ?? throw new JsonException($"Brak wartości dla {typeof(TSelf).Name}.");

            return SingleValueObjectFactory<TSelf, TValue>.Create(value).TryGetValue(out var created, out var error)
                ? created
                : throw new JsonException(error.Message);
        }

        public override void Write(Utf8JsonWriter writer, TSelf value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.Value, options);
    }
}
