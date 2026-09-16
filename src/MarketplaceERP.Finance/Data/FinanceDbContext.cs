using MarketplaceERP.Finance.Entities;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.Finance.Data;

/// <summary>
/// DbContext del módulo Finance (Equipo 3).
/// Gestiona las entidades Transaccionales: Órdenes, Items de Orden, Splits de Pago y Liquidaciones.
/// Esquema PostgreSQL: finance
/// </summary>
public class FinanceDbContext : DbContext
{
    public FinanceDbContext(DbContextOptions<FinanceDbContext> options) : base(options) { }

    public DbSet<Orden> Ordenes => Set<Orden>();
    public DbSet<ItemOrden> ItemsOrden => Set<ItemOrden>();
    public DbSet<SplitPago> SplitsPago => Set<SplitPago>();
    public DbSet<Liquidacion> Liquidaciones => Set<Liquidacion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Todas las entidades de este contexto van al esquema "finance"
        modelBuilder.HasDefaultSchema("finance");

        // ── Orden ─────────────────────────────────────────────────────────────
        modelBuilder.Entity<Orden>(entity =>
        {
            entity.ToTable("ordenes");
            entity.HasKey(o => o.Id);
            entity.Property(o => o.Id).ValueGeneratedOnAdd();
            entity.Property(o => o.MontoTotal).HasPrecision(18, 2).IsRequired();
            entity.Property(o => o.Moneda).HasMaxLength(3).HasDefaultValue("USD");
            entity.Property(o => o.Estado)
                  .HasConversion<string>()
                  .HasMaxLength(20);
            entity.Property(o => o.FechaCreacion).HasDefaultValueSql("NOW()");

            // CompradorId y VendedorId son referencias cross-schema: NO FK en DB
            entity.Property(o => o.CompradorId).IsRequired();
            entity.Property(o => o.VendedorId).IsRequired();
            entity.HasIndex(o => o.CompradorId);
            entity.HasIndex(o => o.VendedorId);
            entity.HasIndex(o => o.Estado);
        });

        // ── ItemOrden ─────────────────────────────────────────────────────────
        modelBuilder.Entity<ItemOrden>(entity =>
        {
            entity.ToTable("items_orden");
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Id).ValueGeneratedOnAdd();
            entity.Property(i => i.PrecioUnitario).HasPrecision(18, 2).IsRequired();
            entity.Property(i => i.Cantidad).IsRequired();

            // Ignora la propiedad calculada (no se persiste en DB)
            entity.Ignore(i => i.Subtotal);

            // ProductoId es referencia cross-schema: NO FK en DB
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

            // Relación 1:1 con Orden
            entity.HasOne(s => s.Orden)
                  .WithOne(o => o.SplitPago)
                  .HasForeignKey<SplitPago>(s => s.OrdenId)
                  .OnDelete(DeleteBehavior.Restrict);
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

            // VendedorId es referencia cross-schema: NO FK en DB
            entity.Property(l => l.VendedorId).IsRequired();
            entity.HasIndex(l => l.VendedorId);
            entity.HasIndex(l => l.Estado);

            entity.HasOne(l => l.SplitPago)
                  .WithOne(s => s.Liquidacion)
                  .HasForeignKey<Liquidacion>(l => l.SplitPagoId)
                  .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
