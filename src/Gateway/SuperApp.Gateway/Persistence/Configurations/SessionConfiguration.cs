using SuperApp.Gateway.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SuperApp.Gateway.Persistence.Configurations;

/// <summary>
/// EF mapping of <see cref="SuperApp.Gateway.Persistence.Entities.Session"/> to <c>gateway.Sessions</c>, with indexes on <c>Subject</c> and <c>SessionId</c> for back-channel
/// logout and on <c>ExpiresAt</c> for the cleanup job (ADR-0013).
/// </summary>
internal sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    /// <summary>Configures the table, the key, the column lengths and the indexes.</summary>
    /// <param name="builder">Builder of the <see cref="SuperApp.Gateway.Persistence.Entities.Session"/> entity.</param>
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("Sessions");
        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).HasMaxLength(64);
        builder.Property(session => session.Subject).HasMaxLength(200);
        builder.Property(session => session.SessionId).HasMaxLength(200);
        builder.HasIndex(session => session.Subject);
        builder.HasIndex(session => session.SessionId);
        builder.HasIndex(session => session.ExpiresAt);
    }
}
