using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MarketplaceERP.API.Services;

/// <summary>
/// SOLO QA: contrato de solicitud para el eventual checkout de varios SKU.
/// Precio, moneda, vendedor, IVA y envío jamás proceden del cliente.
/// No hay endpoint ni registro en DI. No ejecuta cobros ni consulta bases de datos.
/// </summary>
public sealed record QaCheckoutItemSolicitud(Guid ProductoId, int Cantidad);

/// <summary>Ficha confiable del catálogo, construida por el servidor.</summary>
public sealed record QaCheckoutProductoServidor(
    Guid ProductoId, Guid VendedorId, decimal PrecioB2C,
    string Moneda, bool EstaPublicado, string EstadoProducto);

/// <summary>
/// Política decidida explícitamente por servidor. El 0%+0 reproduce el
/// checkout B2C actual; 13%+$5 reproduce el cálculo de OrdenesController.
/// Ninguna es política fiscal aprobada para un nuevo checkout.
/// </summary>
public sealed record QaCheckoutPoliticaServidor(decimal PorcentajeIva, decimal CostoEnvio);

public sealed record QaCheckoutLineaCotizada(Guid ProductoId, int Cantidad,
    decimal PrecioUnitario, decimal TotalLinea);

public sealed record QaCheckoutCotizacion(
    Guid VendedorId, string Moneda, string PayloadHash,
    IReadOnlyList<QaCheckoutLineaCotizada> Lineas,
    decimal SubTotal, decimal ImpuestoIva, decimal CostoEnvio, decimal MontoTotal);

public static class QaMultiSkuCotizacionCanonica
{
    private const decimal MaxMoneda18_2 = 9999999999999999.99m;
    private const int CantidadMaximaPorSkuQa = 9999;

    /// <summary>
    /// Normalización opcional previa al contrato V2. Agrupa el mismo SKU
    /// llegado desde varias acciones de carrito y rechaza desbordamientos.
    /// Devuelve únicamente ProductoId/Cantidad, nunca datos financieros.
    /// La cotización sigue exigiendo SKU únicos para detectar saltos del
    /// paso de normalización por parte de un cliente malicioso.
    /// </summary>
    public static IReadOnlyList<QaCheckoutItemSolicitud> NormalizarLineasCliente(
        IReadOnlyList<QaCheckoutItemSolicitud> origen)
    {
        if (origen is null || origen.Count is < 2 or > 200 ||
            origen.Any(x => x.ProductoId == Guid.Empty ||
                x.Cantidad is < 1 or > CantidadMaximaPorSkuQa))
            throw new InvalidOperationException("CARRITO_V2_INVALIDO");

        var grupos = origen.GroupBy(x => x.ProductoId)
            .OrderBy(x => x.Key).ToArray();
        if (grupos.Length is < 2 or > 20)
            throw new InvalidOperationException("CARRITO_V2_SKUS_FUERA_LIMITE");

        return grupos.Select(g =>
        {
            var unidades = g.Sum(x => (long)x.Cantidad);
            if (unidades > CantidadMaximaPorSkuQa)
                throw new InvalidOperationException("CARRITO_V2_CANTIDAD_ACUMULADA_INVALIDA");
            return new QaCheckoutItemSolicitud(g.Key, (int)unidades);
        }).ToArray();
    }

    public static string HashCarritoNormalizado(
        IReadOnlyList<QaCheckoutItemSolicitud> solicitud)
    {
        if (solicitud is null || solicitud.Count is < 2 or > 20 ||
            solicitud.Any(x => x.ProductoId == Guid.Empty ||
                x.Cantidad is < 1 or > CantidadMaximaPorSkuQa) ||
            solicitud.Select(x => x.ProductoId).Distinct().Count() != solicitud.Count)
            throw new InvalidOperationException("CARRITO_V2_INVALIDO");

        var ordenado = solicitud.OrderBy(x => x.ProductoId).ToArray();
        var payload = "checkout-v2|" + string.Join("|",
            ordenado.Select(x => x.ProductoId.ToString("N") + ":" +
                x.Cantidad.ToString(CultureInfo.InvariantCulture)));
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    public static string HashCheckoutLegacy(Guid productoId, int cantidad)
    {
        // DEBE ser idéntico al contrato actual: no prefijar ni reordenar.
        var payload = $"{productoId:N}|{cantidad}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    public static QaCheckoutCotizacion Cotizar(
        IReadOnlyList<QaCheckoutItemSolicitud> solicitud,
        IReadOnlyList<QaCheckoutProductoServidor> catalogoServidor,
        QaCheckoutPoliticaServidor politicaServidor)
    {
        if (solicitud is null || solicitud.Count is < 2 or > 20 ||
            solicitud.Any(x => x.ProductoId == Guid.Empty ||
                x.Cantidad is < 1 or > CantidadMaximaPorSkuQa) ||
            solicitud.Select(x => x.ProductoId).Distinct().Count() != solicitud.Count)
            throw new InvalidOperationException("CARRITO_V2_INVALIDO");

        if (catalogoServidor is null || politicaServidor is null ||
            politicaServidor.PorcentajeIva < 0 ||
            politicaServidor.PorcentajeIva > 1 ||
            politicaServidor.CostoEnvio < 0 ||
            !EsMoneda18_2(politicaServidor.CostoEnvio))
            throw new InvalidOperationException("POLITICA_SERVIDOR_INVALIDA");

        // Input no monetario del cliente. La ordenación hace que la misma lista
        // de productos con distinto orden tenga la misma huella de idempotencia.
        var orden = solicitud.OrderBy(x => x.ProductoId).ToArray();
        var hash = HashCarritoNormalizado(orden);

        var preciosPorId = catalogoServidor.GroupBy(x => x.ProductoId)
            .ToDictionary(g => g.Key, g => g.ToArray());
        var lineas = new List<QaCheckoutLineaCotizada>(orden.Length);
        Guid vendedorId = Guid.Empty;
        decimal subtotal = 0m;

        foreach (var item in orden)
        {
            if (!preciosPorId.TryGetValue(item.ProductoId, out var precios) ||
                precios.Length != 1)
                throw new InvalidOperationException("PRODUCTO_NO_EN_CATALOGO_UNICO");

            var p = precios[0];
            if (!p.EstaPublicado || !string.Equals(
                    p.EstadoProducto, "Activo", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(p.Moneda, "USD", StringComparison.OrdinalIgnoreCase) ||
                p.VendedorId == Guid.Empty || !EsMoneda18_2(p.PrecioB2C) ||
                p.PrecioB2C <= 0)
                throw new InvalidOperationException("PRODUCTO_NO_APTO_CHECKOUT_V2");

            if (vendedorId == Guid.Empty)
                vendedorId = p.VendedorId;
            else if (vendedorId != p.VendedorId)
                throw new InvalidOperationException("CHECKOUT_V2_VARIOS_VENDEDORES");

            decimal totalLinea;
            try { totalLinea = checked(p.PrecioB2C * item.Cantidad); }
            catch (OverflowException)
            {
                throw new InvalidOperationException("IMPORTE_EXCEDE_PRECISION_18_2");
            }
            if (!EsMoneda18_2(totalLinea))
                throw new InvalidOperationException("IMPORTE_EXCEDE_PRECISION_18_2");
            try { subtotal = checked(subtotal + totalLinea); }
            catch (OverflowException)
            {
                throw new InvalidOperationException("IMPORTE_EXCEDE_PRECISION_18_2");
            }
            if (!EsMoneda18_2(subtotal))
                throw new InvalidOperationException("IMPORTE_EXCEDE_PRECISION_18_2");

            lineas.Add(new(item.ProductoId, item.Cantidad, p.PrecioB2C, totalLinea));
        }

        // Misma convención .NET Math.Round(...,2) del controlador general,
        // pero la decisión de tasa/envío se inyecta desde servidor.
        decimal iva;
        decimal total;
        try
        {
            iva = decimal.Round(checked(subtotal * politicaServidor.PorcentajeIva), 2);
            total = checked(subtotal + iva + politicaServidor.CostoEnvio);
        }
        catch (OverflowException)
        {
            throw new InvalidOperationException("IMPORTE_EXCEDE_PRECISION_18_2");
        }
        if (!EsMoneda18_2(iva) || !EsMoneda18_2(total))
            throw new InvalidOperationException("IMPORTE_EXCEDE_PRECISION_18_2");

        return new(vendedorId, "USD", hash, lineas, subtotal,
            iva, politicaServidor.CostoEnvio, total);
    }

    private static bool EsMoneda18_2(decimal valor) =>
        valor >= 0 && valor <= MaxMoneda18_2 && decimal.Round(valor, 2) == valor;
}
