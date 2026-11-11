using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Agenda.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<GoogleConnection> GoogleConnections => Set<GoogleConnection>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<GroupInvite> GroupInvites => Set<GroupInvite>();
    public DbSet<SharedSource> SharedSources => Set<SharedSource>();

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

        modelBuilder.Entity<Group>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(60);
            e.HasMany(x => x.Members).WithOne(x => x.Group).HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Invites).WithOne(x => x.Group).HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GroupMember>(e =>
        {
            e.HasKey(x => new { x.GroupId, x.UserId });
            // Regra do MVP: cada pessoa está em no máximo 1 grupo. O índice único garante isso
            // até contra duas requisições simultâneas (o if no código sozinho não garantiria).
            e.HasIndex(x => x.UserId).IsUnique();
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Color).HasMaxLength(9);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.SharedSources).WithOne()
                .HasForeignKey(x => new { x.GroupId, x.UserId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GroupInvite>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.TokenHash).HasMaxLength(64);
        });

        modelBuilder.Entity<SharedSource>(e =>
        {
            e.HasKey(x => x.Id);
            // 255 caracteres cobrem os ids de agenda do Google e mantêm o índice abaixo do limite de 1700 bytes do SQL Server.
            e.Property(x => x.GoogleId).HasMaxLength(255);
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Visibility).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(x => new { x.GroupId, x.UserId, x.Kind, x.GoogleId }).IsUnique();
        });
    }
}
