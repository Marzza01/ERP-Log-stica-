using MarketplaceERP.Catalog.Entities;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.Catalog.Data;

/// <summary>
/// DbContext del módulo Catalog (Equipo 2).
/// Gestiona las entidades de Catálogo: Productos, Stock Físico y Reservas de Stock.
/// Esquema PostgreSQL: catalog
/// </summary>
public class CatalogDbContext : DbContext
{
    public CatalogDbContext(DbContextOptions<CatalogDbContext> options) : base(options) { }

    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<StockFisico> StockFisico => Set<StockFisico>();
    public DbSet<ReservaStock> ReservasStock => Set<ReservaStock>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Todas las entidades de este contexto van al esquema "catalog"
        modelBuilder.HasDefaultSchema("catalog");

        // ── Producto ──────────────────────────────────────────────────────────
        modelBuilder.Entity<Producto>(entity =>
        {
            entity.ToTable("productos");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).ValueGeneratedOnAdd();
            entity.Property(p => p.Nombre).HasMaxLength(300).IsRequired();
            entity.Property(p => p.Descripcion).HasMaxLength(2000);
            entity.Property(p => p.PrecioBase).HasPrecision(18, 2).IsRequired();
            entity.Property(p => p.Moneda).HasMaxLength(3).HasDefaultValue("USD");
            entity.Property(p => p.FechaCreacion).HasDefaultValueSql("NOW()");

            // VendedorId y CategoriaId son referencias cross-schema: NO FK en DB
            entity.Property(p => p.VendedorId).IsRequired();
            entity.Property(p => p.CategoriaId).IsRequired();

            entity.HasIndex(p => p.VendedorId);
            entity.HasIndex(p => p.CategoriaId);
            entity.HasIndex(p => p.EstaPublicado);
        });

        // ── StockFisico ───────────────────────────────────────────────────────
        modelBuilder.Entity<StockFisico>(entity =>
        {
            entity.ToTable("stock_fisico");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Id).ValueGeneratedOnAdd();
            entity.Property(s => s.CantidadTotal).IsRequired();
            entity.Property(s => s.CantidadReservada).HasDefaultValue(0);
            entity.Property(s => s.UltimaActualizacion).HasDefaultValueSql("NOW()");

            // Ignora la propiedad calculada (no se persiste en DB)
            entity.Ignore(s => s.CantidadDisponible);

            // Relación 1:1 con Producto
            entity.HasOne(s => s.Producto)
                  .WithOne(p => p.Stock)
                  .HasForeignKey<StockFisico>(s => s.ProductoId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── ReservaStock ──────────────────────────────────────────────────────
        modelBuilder.Entity<ReservaStock>(entity =>
        {
            entity.ToTable("reservas_stock");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Id).ValueGeneratedOnAdd();
            entity.Property(r => r.Cantidad).IsRequired();
            entity.Property(r => r.Estado)
                  .HasConversion<string>()
                  .HasMaxLength(20);
            entity.Property(r => r.FechaReserva).HasDefaultValueSql("NOW()");

            // OrdenId es referencia cross-schema: NO FK en DB
            entity.Property(r => r.OrdenId).IsRequired();
            entity.HasIndex(r => r.OrdenId);
            entity.HasIndex(r => r.Estado);

            entity.HasOne(r => r.Producto)
                  .WithMany(p => p.Reservas)
                  .HasForeignKey(r => r.ProductoId)
                  .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
