using SuperApp.Framework.Domain.Results;
using SuperApp.Framework.Domain.ValueObjects;

namespace SuperApp.Framework.Infrastructure.ValueObjects;

/// <summary>
/// Calls the static abstract factories of a single-value object through ordinary static methods, which expression trees
/// (used by EF Core value converters) can reference.
/// </summary>
internal static class SingleValueObjectFactory<TSelf, TValue>
    where TSelf : struct, ISingleValueObject<TSelf, TValue>
    where TValue : notnull
{
    public static TSelf FromTrusted(TValue value) => TSelf.FromTrusted(value);

    public static Result<TSelf> Create(TValue value) => TSelf.Create(value);
}
