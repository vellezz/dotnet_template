using SuperApp.Framework.Domain.ValueObjects;
using SuperApp.Framework.Infrastructure.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace SuperApp.Framework.Infrastructure.Persistence.Conventions;

/// <summary>
/// EF Core value converter between a single-value object and its primitive: writes <c>Value</c> and reads with <c>FromTrusted</c>,
/// because values in the database were validated when they were written.
/// </summary>
internal sealed class SingleValueObjectValueConverter<TSelf, TValue>()
    : ValueConverter<TSelf, TValue>(
        valueObject => valueObject.Value,
        value => SingleValueObjectFactory<TSelf, TValue>.FromTrusted(value))
    where TSelf : struct, ISingleValueObject<TSelf, TValue>
    where TValue : notnull;
