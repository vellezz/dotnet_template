using System.Reflection;
using SuperApp.Framework.Infrastructure.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace SuperApp.Framework.Infrastructure.Persistence.Conventions;

/// <summary>
/// EF Core convention that stores every strongly typed ID and single-value object as its primitive value (ADR-0023, ADR-0024).
/// </summary>
/// <remarks>
/// Thanks to this convention, entity configurations map an <c>OrderId</c> or <c>SleepQuality</c> property like any primitive property, without
/// <c>HasConversion</c>. Values are read back with <c>FromTrusted</c>, because they were validated when written.
/// </remarks>
public static class SingleValueObjectConventions
{
    /// <summary>Registers a value converter for every <see cref="SuperApp.Framework.Domain.ValueObjects.ISingleValueObject{TSelf, TValue}"/> implementation found in <paramref name="assemblies"/>.</summary>
    /// <remarks>Called by <see cref="WriteDbContextBase"/> and <see cref="ReadDbContextBase"/>; service code does not need to call it.</remarks>
    /// <param name="builder">The EF Core conventions builder.</param>
    /// <param name="assemblies">Assemblies to scan, normally the service's Domain assembly.</param>
    public static void AddSingleValueObjectConversions(this ModelConfigurationBuilder builder, params Assembly[] assemblies)
    {
        foreach (var (type, valueType) in SingleValueObjectTypes.Find(assemblies))
        {
            var converter = typeof(SingleValueObjectValueConverter<,>).MakeGenericType(type, valueType);
            builder.Properties(type).HaveConversion(converter);
        }
    }
}
