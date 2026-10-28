/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Finance.Entities;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.Finance.Data;

/// <summary>
/// DbContext del módulo Finance & OMS (Célula B).
/// Gestiona Órdenes, Compras (PO), Facturación, Match Tridireccional y Contabilidad General.
/// Esquema PostgreSQL: finance
/// </summary>
public class FinanceDbContext : DbContext
{
    public FinanceDbContext(DbContextOptions<FinanceDbContext> options) : base(options) { }

    public DbSet<Orden> Ordenes => Set<Orden>();
    public DbSet<ItemOrden> ItemsOrden => Set<ItemOrden>();
    public DbSet<SplitPago> SplitsPago => Set<SplitPago>();
    public DbSet<Liquidacion> Liquidaciones => Set<Liquidacion>();

    // Procurement / Compras
    public DbSet<OrdenCompra> OrdenesCompra => Set<OrdenCompra>();
    public DbSet<ItemOrdenCompra> ItemsOrdenCompra => Set<ItemOrdenCompra>();
    public DbSet<FacturaProveedor> FacturasProveedor => Set<FacturaProveedor>();
    public DbSet<MatchTridireccional> MatchesTridireccionales => Set<MatchTridireccional>();

    // Contabilidad General (Libro Diario y Mayor)
    public DbSet<PartidaContable> PartidasContables => Set<PartidaContable>();
    public DbSet<LineaPartidaContable> LineasPartidaContable => Set<LineaPartidaContable>();

    // Idempotencia transaccional
    public DbSet<IdempotencyKeyRecord> IdempotencyKeys => Set<IdempotencyKeyRecord>();

    // Conciliación de pagos huérfanos tras fallo posterior al cobro
    public DbSet<PagoConciliacionHuerfano> PagosConciliacionHuerfanos => Set<PagoConciliacionHuerfano>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Todas las entidades de este contexto van al esquema "finance"
        modelBuilder.HasDefaultSchema("finance");

        // ── Orden (OMS) ───────────────────────────────────────────────────────
        modelBuilder.Entity<Orden>(entity =>
        {
            entity.ToTable("ordenes");
            entity.HasKey(o => o.Id);
            entity.Property(o => o.Id).ValueGeneratedOnAdd();
            entity.Property(o => o.NumeroOrden).HasMaxLength(50).IsRequired();
            entity.Property(o => o.SubTotal).HasPrecision(18, 2).HasDefaultValue(0m);
            entity.Property(o => o.ImpuestoIva).HasPrecision(18, 2).HasDefaultValue(0m);
            entity.Property(o => o.CostoEnvio).HasPrecision(18, 2).HasDefaultValue(0m);
            entity.Property(o => o.MontoTotal).HasPrecision(18, 2).IsRequired();
            entity.Property(o => o.Moneda).HasMaxLength(3).HasDefaultValue("USD");
            entity.Property(o => o.Estado)
                  .HasConversion<string>()
                  .HasMaxLength(30);
            entity.Property(o => o.TipoCliente)
                  .HasConversion<string>()
                  .HasMaxLength(20);
            entity.Property(o => o.TipoComprobante).HasMaxLength(50).HasDefaultValue("FacturaConsumidorFinal");
            entity.Property(o => o.NumeroComprobanteFiscal).HasMaxLength(50);
            entity.Property(o => o.CondicionPago).HasMaxLength(50).HasDefaultValue("Inmediato");
            entity.Property(o => o.Transportista).HasMaxLength(100).HasDefaultValue("LOGIMKT Express");
            entity.Property(o => o.GuiaEnvio).HasMaxLength(100);
            entity.Property(o => o.ZonaEnvio).HasMaxLength(100).HasDefaultValue("San Salvador y AMSS");
            entity.Property(o => o.DireccionEntrega).HasMaxLength(500);
            entity.Property(o => o.FechaCreacion).HasDefaultValueSql("NOW()");

            // Token de concurrencia optimista
            if (Database.IsNpgsql())
            {
                entity.Property(o => o.Version)
                      .IsRowVersion();
            }
            else
            {
                entity.Property(o => o.Version)
                      .IsConcurrencyToken();
            }

            entity.Ignore(o => o.FechaCompletada);

            // Cross-schema references
            entity.Property(o => o.CompradorId).IsRequired();
            entity.Property(o => o.VendedorId).IsRequired();
            entity.HasIndex(o => o.NumeroOrden).IsUnique();
            entity.HasIndex(o => o.CompradorId);
            entity.HasIndex(o => o.VendedorId);
            entity.HasIndex(o => o.Estado);
            entity.HasIndex(o => o.TipoCliente);
            entity.HasIndex(o => o.GuiaEnvio);
        });

        // ── ItemOrden ─────────────────────────────────────────────────────────
        modelBuilder.Entity<ItemOrden>(entity =>
        {
            entity.ToTable("items_orden");
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Id).ValueGeneratedOnAdd();
            entity.Property(i => i.SkuReferencia).HasMaxLength(50);
            entity.Property(i => i.NombreProducto).HasMaxLength(300);
            entity.Property(i => i.PrecioUnitario).HasPrecision(18, 2).IsRequired();
            entity.Property(i => i.Cantidad).IsRequired();

            entity.Ignore(i => i.Subtotal);

            entity.Property(i => i.ProductoId).IsRequired();

            entity.HasOne(i => i.Orden)
                  .WithMany(o => o.Items)
                  .HasForeignKey(i => i.OrdenId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── SplitPago ─────────────────────────────────────────────────────────
        modelBuilder.Entity<SplitPago>(entity =>
        {
            entity.ToTable("splits_pago");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Id).ValueGeneratedOnAdd();
            entity.Property(s => s.MontoTotal).HasPrecision(18, 2).IsRequired();
            entity.Property(s => s.PorcentajeVendedor).HasPrecision(5, 2).HasDefaultValue(90m);
            entity.Property(s => s.MontoVendedor).HasPrecision(18, 2).IsRequired();
            entity.Property(s => s.PorcentajeMarketplace).HasPrecision(5, 2).HasDefaultValue(10m);
            entity.Property(s => s.MontoMarketplace).HasPrecision(18, 2).IsRequired();
            entity.Property(s => s.TransaccionId).HasMaxLength(200).IsRequired();
            entity.Property(s => s.FechaProcesado).HasDefaultValueSql("NOW()");

            entity.HasOne(s => s.Orden)
                  .WithOne(o => o.SplitPago)
                  .HasForeignKey<SplitPago>(s => s.OrdenId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(s => s.Liquidacion)
                  .WithMany(l => l.Splits)
                  .HasForeignKey(s => s.LiquidacionId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(s => s.LiquidacionId);
        });

        // ── Liquidacion ───────────────────────────────────────────────────────
        modelBuilder.Entity<Liquidacion>(entity =>
        {
            entity.ToTable("liquidaciones");
            entity.HasKey(l => l.Id);
            entity.Property(l => l.Id).ValueGeneratedOnAdd();
            entity.Property(l => l.MontoLiquidar).HasPrecision(18, 2).IsRequired();
            entity.Property(l => l.Moneda).HasMaxLength(3).HasDefaultValue("USD");
            entity.Property(l => l.Estado)
                  .HasConversion<string>()
                  .HasMaxLength(20);
            entity.Property(l => l.FechaCreacion).HasDefaultValueSql("NOW()");

            // Token de concurrencia optimista
            if (Database.IsNpgsql())
            {
                entity.Property(l => l.Version)
                      .IsRowVersion();
            }
            else
            {
                entity.Property(l => l.Version)
                      .IsConcurrencyToken();
            }

            entity.Property(l => l.VendedorId).IsRequired();
            entity.HasIndex(l => l.VendedorId);
            entity.HasIndex(l => l.Estado);
        });

        // ── OrdenCompra (PO) ──────────────────────────────────────────────────
        modelBuilder.Entity<OrdenCompra>(entity =>
        {
            entity.ToTable("ordenes_compra");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).ValueGeneratedOnAdd();
            entity.Property(p => p.NumeroPO).HasMaxLength(50).IsRequired();
            entity.Property(p => p.ProveedorNombre).HasMaxLength(200).IsRequired();
            entity.Property(p => p.Estado)
                  .HasConversion<string>()
                  .HasMaxLength(30);
            entity.Property(p => p.SubTotal).HasPrecision(18, 2).IsRequired();
            entity.Property(p => p.ImpuestoIva).HasPrecision(18, 2).IsRequired();
            entity.Property(p => p.MontoTotal).HasPrecision(18, 2).IsRequired();
            entity.Property(p => p.Moneda).HasMaxLength(3).HasDefaultValue("USD");
            entity.Property(p => p.TerminosPago).HasMaxLength(100).HasDefaultValue("Crédito 30 días");
            entity.Property(p => p.Observaciones).HasMaxLength(1000);
            entity.Property(p => p.FechaEmision).HasDefaultValueSql("NOW()");

            // Token de concurrencia optimista
            if (Database.IsNpgsql())
            {
                entity.Property(p => p.Version)
                      .IsRowVersion();
            }
            else
            {
                entity.Property(p => p.Version)
                      .IsConcurrencyToken();
            }

            entity.HasIndex(p => p.NumeroPO).IsUnique();
            entity.HasIndex(p => p.ProveedorId);
            entity.HasIndex(p => p.Estado);
        });

        // ── ItemOrdenCompra ───────────────────────────────────────────────────
        modelBuilder.Entity<ItemOrdenCompra>(entity =>
        {
            entity.ToTable("items_orden_compra");
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Id).ValueGeneratedOnAdd();
            entity.Property(i => i.Sku).HasMaxLength(50).IsRequired();
            entity.Property(i => i.NombreProducto).HasMaxLength(300).IsRequired();
            entity.Property(i => i.CantidadSolicitada).IsRequired();
            entity.Property(i => i.CantidadRecibida).HasDefaultValue(0);
            entity.Property(i => i.PrecioUnitarioAcordado).HasPrecision(18, 2).IsRequired();

            entity.Ignore(i => i.TotalLinea);

            entity.HasOne(i => i.OrdenCompra)
                  .WithMany(p => p.Items)
                  .HasForeignKey(i => i.OrdenCompraId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── FacturaProveedor ──────────────────────────────────────────────────
        modelBuilder.Entity<FacturaProveedor>(entity =>
        {
            entity.ToTable("facturas_proveedor");
            entity.HasKey(f => f.Id);
            entity.Property(f => f.Id).ValueGeneratedOnAdd();
            entity.Property(f => f.NumeroFactura).HasMaxLength(50).IsRequired();
            entity.Property(f => f.MontoSubTotal).HasPrecision(18, 2).IsRequired();
            entity.Property(f => f.MontoIva).HasPrecision(18, 2).IsRequired();
            entity.Property(f => f.MontoTotal).HasPrecision(18, 2).IsRequired();
            entity.Property(f => f.FechaEmision).HasDefaultValueSql("NOW()");

            entity.HasIndex(f => f.NumeroFactura);
            entity.HasIndex(f => f.OrdenCompraId);
            entity.HasIndex(f => f.ProveedorId);

            entity.HasOne(f => f.OrdenCompra)
                  .WithMany(p => p.Facturas)
                  .HasForeignKey(f => f.OrdenCompraId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // ── MatchTridireccional ───────────────────────────────────────────────
        modelBuilder.Entity<MatchTridireccional>(entity =>
        {
            entity.ToTable("matches_tridireccionales");
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Id).ValueGeneratedOnAdd();
            entity.Property(m => m.CodigoAuditoria).HasMaxLength(50).IsRequired();
            entity.Property(m => m.MontoTotalPO).HasPrecision(18, 2).IsRequired();
            entity.Property(m => m.MontoTotalFactura).HasPrecision(18, 2).IsRequired();
            entity.Property(m => m.Resultado)
                  .HasConversion<string>()
                  .HasMaxLength(30);
            entity.Property(m => m.DetalleDiscrepancias).HasMaxLength(1000);
            entity.Property(m => m.Auditor).HasMaxLength(150).HasDefaultValue("Sistema Automatizado 3-Way Match");
            entity.Property(m => m.FechaAuditoria).HasDefaultValueSql("NOW()");

            entity.Ignore(m => m.DiferenciaMonto);

            entity.HasIndex(m => m.CodigoAuditoria).IsUnique();
            entity.HasIndex(m => m.OrdenCompraId);
            entity.HasIndex(m => m.Resultado);

            entity.HasOne(m => m.OrdenCompra)
                  .WithMany(p => p.Matches3Way)
                  .HasForeignKey(m => m.OrdenCompraId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(m => m.FacturaProveedor)
                  .WithMany()
                  .HasForeignKey(m => m.FacturaProveedorId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // Secuencia para número de partida contable (PostgreSQL)
        modelBuilder.HasSequence<long>("PartidaContableNumeroSeq", "finance")
            .StartsAt(1)
            .IncrementsBy(1);

        // Secuencia para número de orden (PostgreSQL)
        modelBuilder.HasSequence<long>("NumeroOrdenSeq", "finance")
            .StartsAt(1)
            .IncrementsBy(1);

        // Secuencia para número de orden de compra (PostgreSQL)
        modelBuilder.HasSequence<long>("NumeroPOSeq", "finance")
            .StartsAt(1)
            .IncrementsBy(1);

        // ── PartidaContable (Libro Diario) ────────────────────────────────────
        modelBuilder.Entity<PartidaContable>(entity =>
        {
            entity.ToTable("partidas_contables");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).ValueGeneratedOnAdd();
            entity.Property(p => p.NumeroPartida).HasMaxLength(50).IsRequired();
            entity.Property(p => p.Concepto).HasMaxLength(500).IsRequired();
            entity.Property(p => p.TipoOperacion).HasMaxLength(50).HasDefaultValue("Venta");
            entity.Property(p => p.DocumentoReferencia).HasMaxLength(100);
            entity.Property(p => p.TotalDebe).HasPrecision(18, 2).IsRequired();
            entity.Property(p => p.TotalHaber).HasPrecision(18, 2).IsRequired();
            entity.Property(p => p.Fecha).HasDefaultValueSql("NOW()");
            entity.Property(p => p.FechaRegistro).HasDefaultValueSql("NOW()");

            entity.Ignore(p => p.EstaCuadrada);

            entity.HasIndex(p => p.NumeroPartida).IsUnique();
            entity.HasIndex(p => p.Fecha);
            entity.HasIndex(p => p.TipoOperacion);

            // Índice único condicional para idempotencia: una sola partida por (TipoOperacion, ReferenciaOrigenId)
            // cuando ReferenciaOrigenId no es nulo
            entity.HasIndex(p => new { p.TipoOperacion, p.ReferenciaOrigenId })
                  .IsUnique()
                  .HasFilter("\"ReferenciaOrigenId\" IS NOT NULL");
        });

        // ── LineaPartidaContable ──────────────────────────────────────────────
        modelBuilder.Entity<LineaPartidaContable>(entity =>
        {
            entity.ToTable("lineas_partida_contable");
            entity.HasKey(l => l.Id);
            entity.Property(l => l.Id).ValueGeneratedOnAdd();
            entity.Property(l => l.CodigoCuenta).HasMaxLength(30).IsRequired();
            entity.Property(l => l.NombreCuenta).HasMaxLength(200).IsRequired();
            entity.Property(l => l.Debe).HasPrecision(18, 2).HasDefaultValue(0m);
            entity.Property(l => l.Haber).HasPrecision(18, 2).HasDefaultValue(0m);
            entity.Property(l => l.Glosa).HasMaxLength(300);

            entity.HasIndex(l => l.CodigoCuenta);

            entity.HasOne(l => l.PartidaContable)
                  .WithMany(p => p.Lineas)
                  .HasForeignKey(l => l.PartidaContableId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── IdempotencyKeyRecord ──────────────────────────────────────────────
        modelBuilder.Entity<IdempotencyKeyRecord>(entity =>
        {
            entity.ToTable("idempotency_keys");
            entity.HasKey(k => k.Id);
            entity.Property(k => k.Id).ValueGeneratedOnAdd();
            entity.Property(k => k.Key).HasMaxLength(128).IsRequired();
            entity.Property(k => k.UsuarioId).IsRequired();
            entity.Property(k => k.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(k => k.StatusCode).IsRequired();
            entity.Property(k => k.ResponseJson).IsRequired();
            entity.Property(k => k.FechaCreacion).HasDefaultValueSql("NOW()");
            entity.Property(k => k.FechaExpiracion).IsRequired();

            // Índice único (UsuarioId, Key)
            entity.HasIndex(k => new { k.UsuarioId, k.Key }).IsUnique();
        });

        // ── PagoConciliacionHuerfano ──────────────────────────────────────────
        modelBuilder.Entity<PagoConciliacionHuerfano>(entity =>
        {
            entity.ToTable("pagos_conciliacion_huerfanos");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).ValueGeneratedOnAdd();
            entity.Property(p => p.OrdenId).IsRequired();
            entity.Property(p => p.TransaccionId).HasMaxLength(100).IsRequired();
            entity.Property(p => p.Monto).HasPrecision(18, 2).IsRequired();
            entity.Property(p => p.Moneda).HasMaxLength(3).HasDefaultValue("USD");
            entity.Property(p => p.MotivoFallo).HasMaxLength(500).IsRequired();
            entity.Property(p => p.ReembolsoTransaccionId).HasMaxLength(100);
            entity.Property(p => p.FechaRegistro).HasDefaultValueSql("NOW()");

            entity.HasIndex(p => p.OrdenId);
            entity.HasIndex(p => p.TransaccionId);
            entity.HasIndex(p => p.Reembolsado);
        });
    }
}
