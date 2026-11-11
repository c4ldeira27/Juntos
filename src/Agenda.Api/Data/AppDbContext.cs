using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Agenda.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<GoogleConnection> GoogleConnections => Set<GoogleConnection>();

    /// <summary>
    /// Chaves do ASP.NET Data Protection. Ficam no banco porque o container é descartável:
    /// se as chaves morressem junto com ele, os refresh tokens criptografados virariam lixo a cada deploy.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.GoogleSubject).IsUnique();
            e.Property(x => x.GoogleSubject).HasMaxLength(64);
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.AvatarUrl).HasMaxLength(2048);
            e.HasOne(x => x.GoogleConnection)
                .WithOne()
                .HasForeignKey<GoogleConnection>(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GoogleConnection>(e =>
        {
            e.HasKey(x => x.UserId);
            e.Property(x => x.GrantedScopes).HasMaxLength(2000);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        });
    }
}
