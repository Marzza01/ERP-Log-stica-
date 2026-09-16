using MarketplaceERP.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.Core.Data;

/// <summary>
/// DbContext del módulo Core (Equipo 1).
/// Gestiona las entidades de Gobernanza: Vendedores, Compradores, Categorías y Reglas de Comisión.
/// Esquema PostgreSQL: core
/// </summary>
public class CoreDbContext : DbContext
{
    public CoreDbContext(DbContextOptions<CoreDbContext> options) : base(options) { }

    public DbSet<Vendedor> Vendedores => Set<Vendedor>();
    public DbSet<Comprador> Compradores => Set<Comprador>();
    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<ReglaComision> ReglasComision => Set<ReglaComision>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Todas las entidades de este contexto van al esquema "core"
        modelBuilder.HasDefaultSchema("core");

        // ── Vendedor ──────────────────────────────────────────────────────────
        modelBuilder.Entity<Vendedor>(entity =>
        {
            entity.ToTable("vendedores");
            entity.HasKey(v => v.Id);
            entity.Property(v => v.Id).ValueGeneratedOnAdd();
            entity.Property(v => v.NombreEmpresa).HasMaxLength(200).IsRequired();
            entity.Property(v => v.Email).HasMaxLength(254).IsRequired();
            entity.HasIndex(v => v.Email).IsUnique();
            entity.Property(v => v.Telefono).HasMaxLength(20);
            entity.Property(v => v.FechaRegistro).HasDefaultValueSql("NOW()");
        });

        // ── Comprador ─────────────────────────────────────────────────────────
        modelBuilder.Entity<Comprador>(entity =>
        {
            entity.ToTable("compradores");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Id).ValueGeneratedOnAdd();
            entity.Property(c => c.NombreCompleto).HasMaxLength(300).IsRequired();
            entity.Property(c => c.Email).HasMaxLength(254).IsRequired();
            entity.HasIndex(c => c.Email).IsUnique();
            entity.Property(c => c.DireccionEnvio).HasMaxLength(500);
            entity.Property(c => c.FechaRegistro).HasDefaultValueSql("NOW()");
        });

        // ── Categoria ─────────────────────────────────────────────────────────
        modelBuilder.Entity<Categoria>(entity =>
        {
            entity.ToTable("categorias");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Id).ValueGeneratedOnAdd();
            entity.Property(c => c.Nombre).HasMaxLength(150).IsRequired();
            entity.Property(c => c.Descripcion).HasMaxLength(500);

            // Auto-referencia para jerarquía de categorías
            entity.HasOne(c => c.CategoriaPadre)
                  .WithMany(c => c.SubCategorias)
                  .HasForeignKey(c => c.CategoriaPadreId)
                  .OnDelete(DeleteBehavior.Restrict)
                  .IsRequired(false);
        });

        // ── ReglaComision ─────────────────────────────────────────────────────
        modelBuilder.Entity<ReglaComision>(entity =>
        {
            entity.ToTable("reglas_comision");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Id).ValueGeneratedOnAdd();
            entity.Property(r => r.PorcentajeComision)
                  .HasPrecision(5, 2)
                  .IsRequired();
            entity.Property(r => r.VigenteDesde).HasDefaultValueSql("NOW()");

            entity.HasOne(r => r.Vendedor)
                  .WithMany(v => v.ReglasComision)
                  .HasForeignKey(r => r.VendedorId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
