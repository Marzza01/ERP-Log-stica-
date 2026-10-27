/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
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
    public DbSet<ReservaStockDetalle> ReservasStockDetalle => Set<ReservaStockDetalle>();
    public DbSet<MovimientoInventario> MovimientosInventario => Set<MovimientoInventario>();
    public DbSet<AjusteInventario> AjustesInventario => Set<AjusteInventario>();
    public DbSet<ConteoCiclico> ConteosCiclicos => Set<ConteoCiclico>();
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

            // Ignorar propiedades de compatibilidad que no forman parte del modelo relacional
            entity.Ignore(p => p.PrecioBase);
            entity.Ignore(p => p.Stock);

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
            entity.Property(s => s.CantidadBloqueada).HasDefaultValue(0);
            entity.Property(s => s.PuntoReorden).HasDefaultValue(15);
            entity.Property(s => s.UbicacionCodigo).HasMaxLength(100).HasDefaultValue("PAS-A-RCK-01-NIV-01-POS-01");
            entity.Property(s => s.UltimaActualizacion).HasDefaultValueSql("NOW()");

            // Propiedades calculadas
            entity.Ignore(s => s.CantidadDisponible);
            entity.Ignore(s => s.EsBajoMinimo);

            // Integridad: ninguna cantidad puede ser negativa y reservado+bloqueado no supera el físico.
            entity.HasCheckConstraint(
                "CK_stock_fisico_cantidades_no_negativas",
                "\"CantidadTotal\" >= 0 AND \"CantidadReservada\" >= 0 AND \"CantidadBloqueada\" >= 0");
            entity.HasCheckConstraint(
                "CK_stock_fisico_reservado_bloqueado",
                "\"CantidadReservada\" + \"CantidadBloqueada\" <= \"CantidadTotal\"");

            // Relación N:1 con Producto: un SKU puede existir en varias ubicaciones.
            entity.HasOne(s => s.Producto)
                  .WithMany(p => p.Stocks)
                  .HasForeignKey(s => s.ProductoId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Relación opcional con UbicacionAlmacen
            entity.HasOne(s => s.UbicacionAlmacen)
                  .WithMany(u => u.StocksAlmacenados)
                  .HasForeignKey(s => s.UbicacionAlmacenId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(s => s.ProductoId);
            entity.HasIndex(s => s.UbicacionAlmacenId);
            entity.HasIndex(s => new { s.ProductoId, s.UbicacionAlmacenId }).IsUnique();
        });

        // ── ReservaStock ──────────────────────────────────────────────────────
        modelBuilder.Entity<ReservaStock>(entity =>
        {
            entity.ToTable("reservas_stock");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Id).ValueGeneratedOnAdd();
            entity.Property(r => r.CantidadOriginal).IsRequired();
            entity.Property(r => r.CantidadLiberada).HasDefaultValue(0);
            entity.Property(r => r.CantidadConsumida).HasDefaultValue(0);
            entity.Ignore(r => r.CantidadPendiente);
            entity.Property(r => r.Estado)
                  .HasConversion<string>()
                  .HasMaxLength(30);
            entity.Property(r => r.FechaReserva).HasDefaultValueSql("NOW()");

            entity.HasCheckConstraint(
                "CK_reservas_stock_cantidades_no_negativas",
                "\"CantidadOriginal\" >= 0 AND \"CantidadLiberada\" >= 0 AND \"CantidadConsumida\" >= 0");
            entity.HasCheckConstraint(
                "CK_reservas_stock_cantidad_resuelta",
                "\"CantidadLiberada\" + \"CantidadConsumida\" <= \"CantidadOriginal\"");

            // OrdenId es referencia cross-schema: NO FK en DB
            entity.Property(r => r.OrdenId).IsRequired();
            entity.HasIndex(r => r.OrdenId);
            entity.HasIndex(r => r.Estado);

            entity.HasOne(r => r.Producto)
                  .WithMany(p => p.Reservas)
                  .HasForeignKey(r => r.ProductoId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // ── ReservaStockDetalle ───────────────────────────────────────────────
        modelBuilder.Entity<ReservaStockDetalle>(entity =>
        {
            entity.ToTable("reservas_stock_detalle");
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Id).ValueGeneratedOnAdd();
            entity.Property(d => d.CantidadAsignada).IsRequired();
            entity.Property(d => d.CantidadLiberada).HasDefaultValue(0);
            entity.Property(d => d.CantidadConsumida).HasDefaultValue(0);
            entity.Ignore(d => d.CantidadPendiente);

            entity.HasCheckConstraint(
                "CK_reservas_stock_detalle_cantidades_no_negativas",
                "\"CantidadAsignada\" >= 0 AND \"CantidadLiberada\" >= 0 AND \"CantidadConsumida\" >= 0");
            entity.HasCheckConstraint(
                "CK_reservas_stock_detalle_cantidad_resuelta",
                "\"CantidadLiberada\" + \"CantidadConsumida\" <= \"CantidadAsignada\"");

            entity.HasOne(d => d.ReservaStock)
                  .WithMany(r => r.Detalles)
                  .HasForeignKey(d => d.ReservaStockId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(d => d.StockFisico)
                  .WithMany(s => s.DetallesReserva)
                  .HasForeignKey(d => d.StockFisicoId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(d => d.ReservaStockId);
            entity.HasIndex(d => d.StockFisicoId);
            entity.HasIndex(d => new { d.ReservaStockId, d.StockFisicoId }).IsUnique();
        });

        // ── MovimientoInventario (ledger operativo) ───────────────────────────
        modelBuilder.Entity<MovimientoInventario>(entity =>
        {
            entity.ToTable("movimientos_inventario");
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Id).ValueGeneratedOnAdd();
            entity.Property(m => m.TipoMovimiento)
                  .HasConversion<string>()
                  .HasMaxLength(40);
            entity.Property(m => m.Cantidad).IsRequired();
            entity.Property(m => m.Referencia).HasMaxLength(100);
            entity.Property(m => m.Observaciones).HasMaxLength(500);
            entity.Property(m => m.FechaMovimiento).HasDefaultValueSql("NOW()");

            entity.HasCheckConstraint(
                "CK_movimientos_inventario_cantidad_positiva",
                "\"Cantidad\" > 0");

            entity.HasOne(m => m.Producto)
                  .WithMany(p => p.MovimientosInventario)
                  .HasForeignKey(m => m.ProductoId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(m => m.UbicacionOrigen)
                  .WithMany()
                  .HasForeignKey(m => m.UbicacionOrigenId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(m => m.UbicacionDestino)
                  .WithMany()
                  .HasForeignKey(m => m.UbicacionDestinoId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(m => m.ReservaStock)
                  .WithMany(r => r.Movimientos)
                  .HasForeignKey(m => m.ReservaStockId)
                  .OnDelete(DeleteBehavior.SetNull);

            // OrdenId y RmaId son referencias de trazabilidad; no crean dependencia cross-module.
            entity.HasIndex(m => m.ProductoId);
            entity.HasIndex(m => m.OrdenId);
            entity.HasIndex(m => m.RmaId);
            entity.HasIndex(m => m.ReservaStockId);
            entity.HasIndex(m => m.FechaMovimiento);
        });

        // ── AjusteInventario ───────────────────────────────────────────────────
        modelBuilder.Entity<AjusteInventario>(entity =>
        {
            entity.ToTable("ajustes_inventario");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Id).ValueGeneratedOnAdd();
            entity.Property(a => a.Tipo).HasConversion<string>().HasMaxLength(20);
            entity.Property(a => a.Estado).HasConversion<string>().HasMaxLength(30);
            entity.Property(a => a.Cantidad).IsRequired();
            entity.Property(a => a.Motivo).HasMaxLength(100).IsRequired();
            entity.Property(a => a.Detalle).HasMaxLength(500);
            entity.Property(a => a.Referencia).HasMaxLength(100);
            entity.Property(a => a.CreadoPor).HasMaxLength(150);
            entity.Property(a => a.AprobadoPor).HasMaxLength(150);
            entity.Property(a => a.AplicadoPor).HasMaxLength(150);
            entity.Property(a => a.FechaCreacion).HasDefaultValueSql("NOW()");

            entity.HasCheckConstraint(
                "CK_ajustes_inventario_cantidad_positiva",
                "\"Cantidad\" > 0");

            entity.HasOne(a => a.StockFisico)
                  .WithMany()
                  .HasForeignKey(a => a.StockFisicoId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(a => a.Producto)
                  .WithMany()
                  .HasForeignKey(a => a.ProductoId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(a => a.StockFisicoId);
            entity.HasIndex(a => a.ProductoId);
            entity.HasIndex(a => a.Estado);
            entity.HasIndex(a => a.FechaCreacion);
        });

        // ── ConteoCiclico ─────────────────────────────────────────────────────
        modelBuilder.Entity<ConteoCiclico>(entity =>
        {
            entity.ToTable("conteos_ciclicos");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Id).ValueGeneratedOnAdd();
            entity.Property(c => c.Estado).HasConversion<string>().HasMaxLength(30);
            entity.Property(c => c.Observaciones).HasMaxLength(500);
            entity.Property(c => c.CreadoPor).HasMaxLength(150);
            entity.Property(c => c.IniciadoPor).HasMaxLength(150);
            entity.Property(c => c.ContadoPor).HasMaxLength(150);
            entity.Property(c => c.RecontadoPor).HasMaxLength(150);
            entity.Property(c => c.FechaCreacion).HasDefaultValueSql("NOW()");

            entity.HasCheckConstraint(
                "CK_conteos_ciclicos_cantidades_no_negativas",
                "(\"CantidadSistema\" IS NULL OR \"CantidadSistema\" >= 0) AND " +
                "(\"PrimerConteo\" IS NULL OR \"PrimerConteo\" >= 0) AND " +
                "(\"SegundoConteo\" IS NULL OR \"SegundoConteo\" >= 0)");

            entity.HasOne(c => c.StockFisico)
                  .WithMany()
                  .HasForeignKey(c => c.StockFisicoId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(c => c.Producto)
                  .WithMany()
                  .HasForeignKey(c => c.ProductoId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(c => c.AjusteInventario)
                  .WithMany()
                  .HasForeignKey(c => c.AjusteInventarioId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(c => c.StockFisicoId);
            entity.HasIndex(c => c.ProductoId);
            entity.HasIndex(c => c.AjusteInventarioId);
            entity.HasIndex(c => c.Estado);
            entity.HasIndex(c => c.FechaCreacion);
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
