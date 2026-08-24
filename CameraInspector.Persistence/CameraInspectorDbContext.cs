using CameraInspector.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CameraInspector.Persistence;

/// <summary>
/// Contexto de persistencia local de Camera Inspector.
/// SQLite se utiliza únicamente como inventario, historial y referencias de configuración.
/// </summary>
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
        // El índice IP acelera búsquedas de la cámara que actualmente ocupa una dirección concreta.
        modelBuilder.Entity<CameraEntity>()
            .HasIndex(c => c.Ip);

        // La MAC es el identificador físico más estable de una cámara dentro de una red local.
        // El índice permite encontrar la misma cámara después de que cambie su IP.
        modelBuilder.Entity<CameraEntity>()
            .HasIndex(c => c.Mac);

        // Las relaciones siguientes mantienen agrupados interfaces, pruebas y eventos por cámara.
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

        // CredentialRef nunca almacena la contraseña: solamente relaciona SQLite con Windows Credential Manager.
        modelBuilder.Entity<CameraCredentialEntity>()
            .HasIndex(c => c.CredentialRef)
            .IsUnique();
    }

    /// <summary>
    /// Devuelve la ruta de la base local.
    /// LocalApplicationData evita mezclar datos privados de la app con documentos sincronizados por Windows.
    /// </summary>
    public static string GetDefaultDbPath()
    {
        // folder representa la carpeta privada de datos locales de Camera Inspector para el usuario actual.
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CameraInspector");

        // CreateDirectory es idempotente: crea la carpeta solo cuando todavía no existe.
        Directory.CreateDirectory(folder);

        // La aplicación trabaja con un único archivo SQLite local.
        return Path.Combine(folder, "camerainspector.db");
    }
}
