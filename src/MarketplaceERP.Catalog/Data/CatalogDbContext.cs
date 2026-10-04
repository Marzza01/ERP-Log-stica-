/**
 * ERP desarrollado por Marcelo Zepeda
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Catalog.Entities;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.Catalog.Data;

/// <summary>
/// DbContext del módulo Catalog (Equipo 2 / Célula A).
/// Gestiona el Catálogo de 500 SKUs, Stock Físico, WMS dinámico, RMA y MWIP.
/// Esquema PostgreSQL: catalog
/// </summary>
public class CatalogDbContext : DbContext
{
    public CatalogDbContext(DbContextOptions<CatalogDbContext> options) : base(options) { }

    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<StockFisico> StockFisico => Set<StockFisico>();
    public DbSet<ReservaStock> ReservasStock => Set<ReservaStock>();
    public DbSet<UbicacionAlmacen> UbicacionesAlmacen => Set<UbicacionAlmacen>();
    public DbSet<DevolucionRMA> DevolucionesRMA => Set<DevolucionRMA>();
    public DbSet<MovimientoMWIP> MovimientosMWIP => Set<MovimientoMWIP>();
    public DbSet<PickList> PickLists => Set<PickList>();
    public DbSet<ItemPickList> ItemsPickList => Set<ItemPickList>();
    public DbSet<PaqueteDespacho> PaquetesDespacho => Set<PaqueteDespacho>();

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
            
            entity.Property(p => p.Sku).HasMaxLength(50).IsRequired();
            entity.Property(p => p.Nombre).HasMaxLength(300).IsRequired();
            entity.Property(p => p.CategoriaNombre).HasMaxLength(100).IsRequired();
            entity.Property(p => p.Subcategoria).HasMaxLength(100);
            entity.Property(p => p.PrecioB2C).HasPrecision(18, 2).IsRequired();
            entity.Property(p => p.PrecioB2B).HasPrecision(18, 2).IsRequired();
            entity.Property(p => p.Moneda).HasMaxLength(3).HasDefaultValue("USD");
            entity.Property(p => p.StockInicial).HasDefaultValue(0);
            entity.Property(p => p.StockMinimo).HasDefaultValue(10);
            entity.Property(p => p.ProveedorNombre).HasMaxLength(200);
            entity.Property(p => p.PaisOrigen).HasMaxLength(100).HasDefaultValue("El Salvador");
            entity.Property(p => p.PesoKg).HasPrecision(18, 2).HasDefaultValue(1.0m);
            entity.Property(p => p.DimensionesCm).HasMaxLength(50);
            entity.Property(p => p.UnidadMedida).HasMaxLength(50).HasDefaultValue("Unidad");
            entity.Property(p => p.EstadoProducto).HasMaxLength(50).HasDefaultValue("Activo");
            entity.Property(p => p.CodigoEAN).HasMaxLength(50);
            entity.Property(p => p.Descripcion).HasMaxLength(2000);
            entity.Property(p => p.FechaCreacion).HasDefaultValueSql("NOW()");

            // Ignorar propiedad de compatibilidad
            entity.Ignore(p => p.PrecioBase);

            // VendedorId y CategoriaId son referencias cross-schema: NO FK en DB
            entity.Property(p => p.VendedorId).IsRequired();
            entity.Property(p => p.CategoriaId).IsRequired();

            entity.HasIndex(p => p.Sku).IsUnique();
            entity.HasIndex(p => p.CodigoEAN);
            entity.HasIndex(p => p.CategoriaNombre);
            entity.HasIndex(p => p.VendedorId);
            entity.HasIndex(p => p.CategoriaId);
            entity.HasIndex(p => p.EstaPublicado);
        });

        // ── UbicacionAlmacen (WMS) ─────────────────────────────────────────────
        modelBuilder.Entity<UbicacionAlmacen>(entity =>
        {
            entity.ToTable("ubicaciones_almacen");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Id).ValueGeneratedOnAdd();
            entity.Property(u => u.Codigo).HasMaxLength(100).IsRequired();
            entity.Property(u => u.Pasillo).HasMaxLength(50).IsRequired();
            entity.Property(u => u.Rack).HasMaxLength(50).IsRequired();
            entity.Property(u => u.Posicion).HasMaxLength(50).IsRequired();
            entity.Property(u => u.Zona).HasMaxLength(50).HasDefaultValue("AlmacenamientoGeneral");
            entity.Property(u => u.FechaCreacion).HasDefaultValueSql("NOW()");

            entity.HasIndex(u => u.Codigo).IsUnique();
            entity.HasIndex(u => new { u.Pasillo, u.Rack, u.Nivel, u.Posicion });
        });

        // ── StockFisico ───────────────────────────────────────────────────────
        modelBuilder.Entity<StockFisico>(entity =>
        {
            entity.ToTable("stock_fisico");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Id).ValueGeneratedOnAdd();
            entity.Property(s => s.CantidadTotal).IsRequired();
            entity.Property(s => s.CantidadReservada).HasDefaultValue(0);
            entity.Property(s => s.PuntoReorden).HasDefaultValue(15);
            entity.Property(s => s.UbicacionCodigo).HasMaxLength(100).HasDefaultValue("PAS-A-RCK-01-NIV-01-POS-01");
            entity.Property(s => s.UltimaActualizacion).HasDefaultValueSql("NOW()");

            // Ignora las propiedades calculadas
            entity.Ignore(s => s.CantidadDisponible);
            entity.Ignore(s => s.EsBajoMinimo);

            // Relación 1:1 con Producto
            entity.HasOne(s => s.Producto)
                  .WithOne(p => p.Stock)
                  .HasForeignKey<StockFisico>(s => s.ProductoId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Relación opcional con UbicacionAlmacen
            entity.HasOne(s => s.UbicacionAlmacen)
                  .WithMany(u => u.StocksAlmacenados)
                  .HasForeignKey(s => s.UbicacionAlmacenId)
                  .OnDelete(DeleteBehavior.SetNull);
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

        // ── DevolucionRMA (Logística Inversa) ──────────────────────────────────
        modelBuilder.Entity<DevolucionRMA>(entity =>
        {
            entity.ToTable("devoluciones_rma");
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Id).ValueGeneratedOnAdd();
            entity.Property(d => d.NumeroRMA).HasMaxLength(50).IsRequired();
            entity.Property(d => d.MotivoCliente).HasMaxLength(500).IsRequired();
            entity.Property(d => d.DictamenTecnico).HasMaxLength(1000);
            entity.Property(d => d.InspectorTecnico).HasMaxLength(150);
            entity.Property(d => d.PorcentajeRemateDescuento).HasPrecision(5, 2).HasDefaultValue(0m);
            entity.Property(d => d.Estado)
                  .HasConversion<string>()
                  .HasMaxLength(30);
            entity.Property(d => d.Clasificacion)
                  .HasConversion<string>()
                  .HasMaxLength(30);
            entity.Property(d => d.FechaSolicitud).HasDefaultValueSql("NOW()");

            entity.HasIndex(d => d.NumeroRMA).IsUnique();
            entity.HasIndex(d => d.OrdenId);

            entity.HasOne(d => d.Producto)
                  .WithMany(p => p.Devoluciones)
                  .HasForeignKey(d => d.ProductoId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // ── MovimientoMWIP (Material & Work in Process) ─────────────────────────
        modelBuilder.Entity<MovimientoMWIP>(entity =>
        {
            entity.ToTable("movimientos_mwip");
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Id).ValueGeneratedOnAdd();
            entity.Property(m => m.TipoEtapa).HasMaxLength(50).IsRequired();
            entity.Property(m => m.NumeroLote).HasMaxLength(50);
            entity.Property(m => m.UbicacionOrigen).HasMaxLength(100);
            entity.Property(m => m.UbicacionDestino).HasMaxLength(100);
            entity.Property(m => m.Estado).HasMaxLength(30).HasDefaultValue("EnProgreso");
            entity.Property(m => m.Observaciones).HasMaxLength(500);
            entity.Property(m => m.FechaInicio).HasDefaultValueSql("NOW()");

            entity.HasOne(m => m.Producto)
                  .WithMany(p => p.MovimientosMWIP)
                  .HasForeignKey(m => m.ProductoId)
                  .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
