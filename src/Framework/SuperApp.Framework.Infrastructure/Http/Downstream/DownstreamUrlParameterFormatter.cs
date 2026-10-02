using System.Globalization;
using System.Reflection;
using Refit;

namespace SuperApp.Framework.Infrastructure.Http.Downstream;

/// <summary>
/// Formats dates and times in URL paths and query strings of downstream calls the way the contracts of this system expect them
/// (ISO 8601), independently of the process culture.
/// </summary>
/// <remarks>
/// Refit's default formatter uses the type's default text form, which for <see cref="DateOnly"/> is culture dependent (for example
/// <c>01.10.2026</c>); the APIs expect <c>2026-10-01</c> (<c>format: date</c> in OpenAPI). Other values keep the default formatting.
/// </remarks>
internal sealed class DownstreamUrlParameterFormatter : DefaultUrlParameterFormatter
{
    /// <inheritdoc />
    public override string? Format(object? parameterValue, ICustomAttributeProvider attributeProvider, Type type) => parameterValue switch
    {
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
        DateTime dateTime => dateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
        _ => base.Format(parameterValue, attributeProvider, type),
    };
}
