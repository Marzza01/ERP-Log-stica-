using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MarketplaceERP.API.Services;

/// <summary>Órdenes sintéticas con reservas reales exclusivamente en STAGING aislado.
/// La orden y TODOS los cambios de inventario comparten conexión y transacción.
/// El rollback es la compensación de cualquier fallo anterior al commit; un timeout
/// de commit se resuelve repitiendo la misma clave, nunca creando otra operación.
/// No depende de una pasarela ni confirma cobros.</summary>
public sealed class OrdenEvaluacionService(
    FinanceDbContext finance, CatalogDbContext catalog, CoreDbContext core,
    IStockService stock, IConfiguration configuration, IWebHostEnvironment environment)
{
    private const string IsolatedHost = "ep-sparkling-smoke-bbcm2qma.c-15.us-east-1.aws.neon.tech";

    public void ValidateTarget()
    {
        if (!configuration.GetValue<bool>("Orders:StagingEvaluation:Enabled") ||
            !string.Equals(configuration["Payments:Provider"], "Disabled", StringComparison.OrdinalIgnoreCase) ||
            !DateTimeOffset.TryParse(configuration["Orders:StagingEvaluation:ExpiresAtUtc"],
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expiry) ||
            expiry <= DateTimeOffset.UtcNow ||
            expiry > DateTimeOffset.UtcNow.AddHours(24))
            throw new EvaluacionOrdenException(503, "Creación de órdenes de evaluación no autorizada.");

        var descriptor = new Npgsql.NpgsqlConnectionStringBuilder(finance.Database.GetDbConnection().ConnectionString);
        var localRehearsal = environment.IsDevelopment() &&
            descriptor.Host is "localhost" or "127.0.0.1" or "::1" &&
            descriptor.Database?.StartsWith("ci_oms_evaluation_", StringComparison.Ordinal) == true &&
            configuration["RENDER_SERVICE_NAME"] is null && configuration["Deployment:Target"] is null;
        if (!localRehearsal)
        {
            if (configuration["RENDER_SERVICE_NAME"] != StagingDatabaseTargetGuard.ServiceName ||
                configuration["Deployment:Target"] != StagingDatabaseTargetGuard.TargetMarker ||
                configuration[StagingDatabaseTargetGuard.ApprovedHostConfiguration] != IsolatedHost)
                throw new EvaluacionOrdenException(503, "Destino de evaluación no autorizado.");
            StagingDatabaseTargetGuard.Validate(configuration,
                configuration["RENDER_SERVICE_NAME"], environment.EnvironmentName,
                finance.Database.GetDbConnection().ConnectionString);
        }
        // No permitir StockServiceMock u otra implementación que no escriba en Catalog.
        if (stock.GetType().FullName != "MarketplaceERP.Catalog.Services.StockService")
            throw new EvaluacionOrdenException(503, "Reservas persistentes no disponibles.");
    }

    public async Task<Orden> CreateAsync(CrearOrdenRequest request, Guid actor, string key,
        CancellationToken ct)
    {
        ValidateTarget();
        if (actor == Guid.Empty || key.Length is < 16 or > 100 ||
            key.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new EvaluacionOrdenException(400, "Clave de idempotencia inválida.");
        if (request.CompradorId == Guid.Empty || request.VendedorId == Guid.Empty ||
            !Enum.IsDefined(request.TipoCliente) || request.Items is null ||
            request.Items.Count is < 1 or > 20 || request.Items.Any(i => i is null ||
                i.ProductoId == Guid.Empty || i.Cantidad is < 1 or > 10000) ||
            request.Items.Select(i => i.ProductoId).Distinct().Count() != request.Items.Count ||
            string.IsNullOrWhiteSpace(request.DireccionEntrega) || request.DireccionEntrega.Length > 500 ||
            (request.CostoEnvio.HasValue && request.CostoEnvio != 5m))
            throw new EvaluacionOrdenException(400, "Comprador, vendedor, dirección o líneas inválidos.");

        var normalized = new
        {
            request.CompradorId, request.VendedorId, request.TipoCliente,
            DireccionEntrega = request.DireccionEntrega.Trim(),
            Items = request.Items.OrderBy(i => i.ProductoId).Select(i => new { i.ProductoId, i.Cantidad })
        };
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(normalized)));
        var operationKey = "OMS:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actor + ":" + key)));

        var originalConnection = catalog.Database.GetDbConnection();
        catalog.Database.SetDbConnection(finance.Database.GetDbConnection(), contextOwnsConnection: false);
        try
        {
            await using var tx = await finance.Database.BeginTransactionAsync(ct);
            await using var catalogTx = await catalog.Database.UseTransactionAsync(tx.GetDbTransaction(), ct);
            // La misma llave se serializa aunque cambie el comprador enviado.
            var lockKey = BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes(operationKey)), 0);
            await finance.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", ct);
            var previous = await finance.Ordenes.Include(o => o.Items)
                .SingleOrDefaultAsync(o => o.IdempotencyKey == operationKey, ct);
            if (previous is not null)
            {
                if (previous.CheckoutPayloadHash != hash)
                    throw new EvaluacionOrdenException(409, "La clave ya pertenece a otra solicitud.");
                await tx.CommitAsync(ct);
                return previous;
            }

            if (!await core.Compradores.AnyAsync(b => b.Id == request.CompradorId && b.EstaActivo, ct) ||
                !await core.Vendedores.AnyAsync(s => s.Id == request.VendedorId && s.EstaActivo, ct))
                throw new EvaluacionOrdenException(400, "Comprador o vendedor inactivo o inexistente.");

            var order = new Orden
            {
                Id = Guid.NewGuid(), CompradorId = request.CompradorId, VendedorId = request.VendedorId,
                IdempotencyKey = operationKey, CheckoutPayloadHash = hash, TipoCliente = request.TipoCliente,
                DireccionEntrega = request.DireccionEntrega.Trim(), CostoEnvio = 5m,
                TipoComprobante = request.TipoCliente == TipoCliente.B2B ? "CreditoFiscalCCF" : "FacturaConsumidorFinal",
                CondicionPago = "EvaluacionSinPago", Estado = EstadoOrden.Pendiente
            };
            order.NumeroOrden = $"TEST-{DateTime.UtcNow:yyyyMMdd}-{order.Id:N}";
            foreach (var item in request.Items.OrderBy(i => i.ProductoId))
            {
                // Precio/nombre/SKU enviados por el cliente nunca son fuente de verdad.
                var product = (await catalog.Productos.FromSqlInterpolated(
                    $"SELECT * FROM catalog.productos WHERE \"Id\" = {item.ProductoId} FOR SHARE")
                    .AsNoTracking().ToListAsync(ct)).SingleOrDefault();
                if (product is null || !product.EstaPublicado || product.EstadoProducto != "Activo" ||
                    product.VendedorId != request.VendedorId || product.Moneda != "USD")
                    throw new EvaluacionOrdenException(400, "Producto no disponible para el vendedor.");
                var price = request.TipoCliente == TipoCliente.B2B ? product.PrecioB2B : product.PrecioB2C;
                if (price <= 0 || price > 1000000m || decimal.Round(price, 2) != price)
                    throw new EvaluacionOrdenException(400, "Precio de catálogo inválido.");
                order.Items.Add(new ItemOrden { Id = Guid.NewGuid(), OrdenId = order.Id,
                    ProductoId = product.Id, Cantidad = item.Cantidad, PrecioUnitario = price,
                    SkuReferencia = product.Sku, NombreProducto = product.Nombre });
                order.SubTotal += item.Cantidad * price;
            }
            if (order.SubTotal < (request.TipoCliente == TipoCliente.B2B ? 500m : 5m))
                throw new EvaluacionOrdenException(400, "No se alcanza el mínimo B2B/B2C.");
            order.ImpuestoIva = decimal.Round(order.SubTotal * 0.13m, 2);
            order.MontoTotal = order.SubTotal + order.ImpuestoIva + order.CostoEnvio;
            finance.Ordenes.Add(order);
            await finance.SaveChangesAsync(ct);
            foreach (var item in order.Items.OrderBy(i => i.ProductoId))
            {
                ct.ThrowIfCancellationRequested();
                var reserved = await stock.ReservarStockAsync(item.ProductoId, item.Cantidad, order.Id);
                if (!reserved.Exitoso || reserved.ReservaId == Guid.Empty)
                    throw new EvaluacionOrdenException(409, "Stock insuficiente; no se creó ninguna reserva ni orden.");
            }
            await tx.CommitAsync(ct);
            return order;
        }
        finally
        {
            // Dispose de la transacción revierte en conjunto también errores de red/cancelación.
            catalog.ChangeTracker.Clear();
            catalog.Database.SetDbConnection(originalConnection, contextOwnsConnection: true);
        }
    }

    public async Task<Orden> CancelAsync(Guid id, CancellationToken ct)
    {
        ValidateTarget();
        var originalConnection = catalog.Database.GetDbConnection();
        catalog.Database.SetDbConnection(finance.Database.GetDbConnection(), contextOwnsConnection: false);
        try
        {
            await using var tx = await finance.Database.BeginTransactionAsync(ct);
            await using var catalogTx = await catalog.Database.UseTransactionAsync(tx.GetDbTransaction(), ct);
            var order = (await finance.Ordenes.FromSqlInterpolated(
                $"SELECT * FROM finance.ordenes WHERE \"Id\" = {id} FOR UPDATE")
                .ToListAsync(ct)).SingleOrDefault();
            if (order is null || order.IdempotencyKey?.StartsWith("OMS:", StringComparison.Ordinal) != true ||
                order.CondicionPago != "EvaluacionSinPago")
                throw new EvaluacionOrdenException(404, "Orden de evaluación inexistente.");
            if (await finance.IntentosPago.AnyAsync(i => i.OrdenId == id, ct) ||
                await finance.SplitsPago.AnyAsync(i => i.OrdenId == id, ct) ||
                await finance.ReembolsosPago.AnyAsync(i => i.OrdenId == id, ct) ||
                order.Estado is not (EstadoOrden.Pendiente or EstadoOrden.Cancelada))
                throw new EvaluacionOrdenException(409, "La orden requiere conciliación; no liberar inventario.");
            var reserves = await catalog.ReservasStock.Where(r => r.OrdenId == id)
                .OrderBy(r => r.ProductoId).ToListAsync(ct);
            if (reserves.Any(r => r.Estado is not (MarketplaceERP.Catalog.Entities.EstadoReserva.Pendiente or
                MarketplaceERP.Catalog.Entities.EstadoReserva.Liberada)))
                throw new EvaluacionOrdenException(409, "Estado de reserva incompatible con compensación.");
            foreach (var reserve in reserves)
                await stock.LiberarStockAsync(reserve.Id);
            order.Estado = EstadoOrden.Cancelada;
            order.FechaCancelacion ??= DateTime.UtcNow;
            await finance.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return order;
        }
        finally
        {
            catalog.ChangeTracker.Clear();
            catalog.Database.SetDbConnection(originalConnection, contextOwnsConnection: true);
        }
    }
}

public sealed class EvaluacionOrdenException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
