using CameraInspector.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CameraInspector.Persistence;

public sealed class CameraInspectorDbContext : DbContext
{
    public DbSet<CameraEntity> Cameras => Set<CameraEntity>();
    public DbSet<CameraInterfaceEntity> CameraInterfaces => Set<CameraInterfaceEntity>();
    public DbSet<CameraTestEntity> CameraTests => Set<CameraTestEntity>();
    public DbSet<CameraEventEntity> CameraEvents => Set<CameraEventEntity>();
    public DbSet<CameraCredentialEntity> CameraCredentials => Set<CameraCredentialEntity>();

    public CameraInspectorDbContext(DbContextOptions<CameraInspectorDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CameraEntity>()
            .HasIndex(c => c.Ip);

        modelBuilder.Entity<CameraEntity>()
            .HasMany(c => c.Interfaces)
            .WithOne()
            .HasForeignKey(i => i.CameraId);

        modelBuilder.Entity<CameraEntity>()
            .HasMany(c => c.Tests)
            .WithOne()
            .HasForeignKey(t => t.CameraId);

        modelBuilder.Entity<CameraEntity>()
            .HasMany(c => c.Events)
            .WithOne()
            .HasForeignKey(e => e.CameraId);

        modelBuilder.Entity<CameraEntity>()
            .HasOne(c => c.Credential)
            .WithOne()
            .HasForeignKey<CameraCredentialEntity>(cr => cr.CameraId);
    }

    /// <summary>
    /// Ruta del archivo SQLite: junto al ejecutable en desarrollo, en %AppData% en producción.
    /// El técnico nunca tiene que crear ni configurar esta base manualmente.
    /// </summary>
    public static string GetDefaultDbPath()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CameraInspector");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "camerainspector.db");
    }
}
