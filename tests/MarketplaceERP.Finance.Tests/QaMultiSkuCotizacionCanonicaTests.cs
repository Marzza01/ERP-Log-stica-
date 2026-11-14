using System.Security.Cryptography;
using System.Text;
using MarketplaceERP.API.Services;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// Contrato de checkout V2 SOLO QA. Nunca llama endpoints, bases ni pasarelas.
/// </summary>
public sealed class QaMultiSkuCotizacionCanonicaTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-000000000020");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-000000000030");
    private static readonly Guid Seller = Guid.Parse("00000000-0000-0000-0000-000000000040");

    private static readonly QaCheckoutPoliticaServidor SinImpuestos = new(0, 0);
    private static readonly QaCheckoutPoliticaServidor ComoOrdenGeneral = new(.13m, 5m);

    private static List<QaCheckoutItemSolicitud> DosItems(int cantidadA = 2, int cantidadB = 3) =>
        new() { new(A, cantidadA), new(B, cantidadB) };

    private static List<QaCheckoutProductoServidor> Catalogo(
        decimal precioA = 12.50m, decimal precioB = 2m) =>
        new()
        {
            new(A, Seller, precioA, "USD", true, "Activo"),
            new(B, Seller, precioB, "USD", true, "Activo"),
        };

    [Fact]
    public void NormalizacionAgrupaSkuDuplicadosAntesDeCotizar()
    {
        var crudo = new[] {
            new QaCheckoutItemSolicitud(B, 1),
            new QaCheckoutItemSolicitud(A, 1),
            new QaCheckoutItemSolicitud(B, 2),
            new QaCheckoutItemSolicitud(A, 1)
        };
        var normalizado = QaMultiSkuCotizacionCanonica.NormalizarLineasCliente(crudo);
        Assert.Equal(new[] { A, B }, normalizado.Select(x => x.ProductoId));
        Assert.Equal(new[] { 2, 3 }, normalizado.Select(x => x.Cantidad));
        var cotizacion = QaMultiSkuCotizacionCanonica.Cotizar(
            normalizado, Catalogo(), SinImpuestos);
        Assert.Equal(31m, cotizacion.MontoTotal);
        Assert.Equal(QaMultiSkuCotizacionCanonica.Cotizar(
            DosItems(), Catalogo(), SinImpuestos).PayloadHash, cotizacion.PayloadHash);
    }

    [Fact]
    public void NormalizacionRechazaOverflowAcumuladoDeSKU()
    {
        Assert.Throws<InvalidOperationException>(() =>
            QaMultiSkuCotizacionCanonica.NormalizarLineasCliente(new[] {
                new QaCheckoutItemSolicitud(A, 9000),
                new QaCheckoutItemSolicitud(A, 1000),
                new QaCheckoutItemSolicitud(B, 1)
            }));
    }

    [Fact]
    public void NormalizacionNoAdmiteSoloUnSkuAunqueEsteDuplicado()
    {
        Assert.Throws<InvalidOperationException>(() =>
            QaMultiSkuCotizacionCanonica.NormalizarLineasCliente(new[] {
                new QaCheckoutItemSolicitud(A, 1),
                new QaCheckoutItemSolicitud(A, 1)
            }));
    }

    [Fact]
    public void NormalizacionNoAdmiteMasDeVeinteSkuUnicos()
    {
        var crudo = Enumerable.Range(1, 21)
            .Select(i => new QaCheckoutItemSolicitud(
                new Guid(i, 0, 0, new byte[8]), 1)).ToArray();
        Assert.Throws<InvalidOperationException>(() =>
            QaMultiSkuCotizacionCanonica.NormalizarLineasCliente(crudo));
    }

    [Fact]
    public void NormalizacionNoAdmiteMasDeDoscientasLineas()
    {
        var crudo = Enumerable.Repeat(new QaCheckoutItemSolicitud(A, 1), 201)
            .Concat(new[] { new QaCheckoutItemSolicitud(B, 1) }).ToArray();
        Assert.Throws<InvalidOperationException>(() =>
            QaMultiSkuCotizacionCanonica.NormalizarLineasCliente(crudo));
    }

    [Fact]
    public void NormalizacionNoAdmiteCantidadesInvalidas()
    {
        Assert.Throws<InvalidOperationException>(() =>
            QaMultiSkuCotizacionCanonica.NormalizarLineasCliente(new[] {
                new QaCheckoutItemSolicitud(A, -1),
                new QaCheckoutItemSolicitud(B, 1)
            }));
    }

    [Fact]
    public void NormalizacionNoModificaLosObjetosOriginales()
    {
        var original = DosItems();
        var before = original.ToArray();
        var canon = QaMultiSkuCotizacionCanonica.NormalizarLineasCliente(original);
        Assert.Equal(before, original);
        Assert.Equal(original, canon);
    }

    [Fact]
    public void Idempotencia_ReordenarCarritoNoCambiaHuellaNiImportes()
    {
        var original = QaMultiSkuCotizacionCanonica.Cotizar(
            DosItems(), Catalogo(), SinImpuestos);
        var invertido = QaMultiSkuCotizacionCanonica.Cotizar(
            DosItems().AsEnumerable().Reverse().ToArray(),
            Catalogo().AsEnumerable().Reverse().ToArray(), SinImpuestos);
        Assert.Equal(original.PayloadHash, invertido.PayloadHash);
        Assert.Equal(original.MontoTotal, invertido.MontoTotal);
        Assert.Equal(original.Lineas.Select(x => x.ProductoId),
            invertido.Lineas.Select(x => x.ProductoId));
        Assert.Equal(31m, original.MontoTotal);
        Assert.Equal(64, original.PayloadHash.Length);
    }

    [Fact]
    public void CambiarCantidadCambiaHuella()
    {
        var a = QaMultiSkuCotizacionCanonica.Cotizar(DosItems(), Catalogo(), SinImpuestos);
        var b = QaMultiSkuCotizacionCanonica.Cotizar(DosItems(3, 3), Catalogo(), SinImpuestos);
        Assert.NotEqual(a.PayloadHash, b.PayloadHash);
    }

    [Fact]
    public void PrecioNuevoNoCambiaHuellaDeSolicitud()
    {
        var a = QaMultiSkuCotizacionCanonica.Cotizar(DosItems(), Catalogo(), SinImpuestos);
        var b = QaMultiSkuCotizacionCanonica.Cotizar(DosItems(), Catalogo(13, 2), SinImpuestos);
        Assert.Equal(a.PayloadHash, b.PayloadHash);
        Assert.Equal(31m, a.MontoTotal);
        Assert.Equal(32m, b.MontoTotal);
    }

    [Fact]
    public void HashLegacySigueSiendoIdenticoYSeparadoDeV2()
    {
        var raw = $"{A:N}|2";
        var legacy = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        Assert.Equal(legacy, QaMultiSkuCotizacionCanonica.HashCheckoutLegacy(A, 2));
        Assert.NotEqual(legacy,
            QaMultiSkuCotizacionCanonica.Cotizar(DosItems(), Catalogo(), SinImpuestos).PayloadHash);
    }

    [Fact]
    public void PoliticaActualB2CYOrdenGeneralProducenCotizacionesDistintas()
    {
        var v1 = QaMultiSkuCotizacionCanonica.Cotizar(DosItems(), Catalogo(), SinImpuestos);
        var general = QaMultiSkuCotizacionCanonica.Cotizar(
            DosItems(), Catalogo(), ComoOrdenGeneral);
        Assert.Equal(31m, v1.SubTotal);
        Assert.Equal(0m, v1.ImpuestoIva);
        Assert.Equal(31m, v1.MontoTotal);
        Assert.Equal(31m, general.SubTotal);
        Assert.Equal(4.03m, general.ImpuestoIva);
        Assert.Equal(5m, general.CostoEnvio);
        Assert.Equal(40.03m, general.MontoTotal);
        Assert.Equal(v1.PayloadHash, general.PayloadHash);
    }

    [Fact]
    public void RedondeoCoincideConControladorGeneralMathRound()
    {
        var catalogo = Catalogo(.50m, 1m);
        var quote = QaMultiSkuCotizacionCanonica.Cotizar(
            DosItems(1, 1), catalogo, new(.13m, 0));
        Assert.Equal(decimal.Round(1.5m * .13m, 2), quote.ImpuestoIva);
        Assert.Equal(1.70m, quote.MontoTotal);
    }

    [Fact]
    public void MultiSkuNuncaAceptaListaDeUnProducto()
    {
        Assert.Throws<InvalidOperationException>(() =>
            QaMultiSkuCotizacionCanonica.Cotizar(
                new[] { new QaCheckoutItemSolicitud(A, 2) },
                Catalogo(), SinImpuestos));
    }

    [Fact]
    public void MultiSkuRechazaMasDeVeinteProductos()
    {
        var items = Enumerable.Range(1, 21)
            .Select(x => new QaCheckoutItemSolicitud(Guid.NewGuid(), 1)).ToArray();
        Assert.Throws<InvalidOperationException>(() =>
            QaMultiSkuCotizacionCanonica.Cotizar(items, Catalogo(), SinImpuestos));
    }

    [Fact]
    public void MultiSkuRechazaDuplicarProducto()
    {
        Assert.Throws<InvalidOperationException>(() =>
            QaMultiSkuCotizacionCanonica.Cotizar(
                new[] { new QaCheckoutItemSolicitud(A, 1), new QaCheckoutItemSolicitud(A, 2) },
                Catalogo(), SinImpuestos));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10000)]
    public void MultiSkuRechazaCantidadesFueraDeLimites(int cantidad)
    {
        Assert.Throws<InvalidOperationException>(() =>
            QaMultiSkuCotizacionCanonica.Cotizar(
                DosItems(cantidad, 1), Catalogo(), SinImpuestos));
    }

    [Fact]
    public void MultiSkuRechazaProductoNoPresente()
    {
        var catalogo = Catalogo();
        catalogo.RemoveAt(1);
        Assert.Throws<InvalidOperationException>(() =>
            QaMultiSkuCotizacionCanonica.Cotizar(DosItems(), catalogo, SinImpuestos));
    }

    [Fact]
    public void MultiSkuRechazaDuplicadoEnCatalogoServidor()
    {
        var catalogo = Catalogo();
        catalogo.Add(catalogo[0]);
        Assert.Throws<InvalidOperationException>(() =>
            QaMultiSkuCotizacionCanonica.Cotizar(DosItems(), catalogo, SinImpuestos));
    }

    [Fact]
    public void MultiSkuRechazaVendedoresDiferentes()
    {
        var catalogo = Catalogo();
        catalogo[1] = catalogo[1] with { VendedorId = Guid.NewGuid() };
        var ex = Assert.Throws<InvalidOperationException>(() =>
            QaMultiSkuCotizacionCanonica.Cotizar(DosItems(), catalogo, SinImpuestos));
        Assert.Contains("VENDEDORES", ex.Message);
    }

    [Theory]
    [InlineData("EUR")]
    [InlineData("")]
    public void MultiSkuRechazaMonedaDistintaUSD(string moneda)
    {
        var catalogo = Catalogo();
        catalogo[1] = catalogo[1] with { Moneda = moneda };
        Assert.Throws<InvalidOperationException>(() =>
            QaMultiSkuCotizacionCanonica.Cotizar(DosItems(), catalogo, SinImpuestos));
    }

    [Theory]
    [InlineData(false, "Activo")]
    [InlineData(true, "Inactivo")]
    public void MultiSkuRechazaInactivosONoPublicados(bool publicado, string estado)
    {
        var catalogo = Catalogo();
        catalogo[1] = catalogo[1] with { EstaPublicado = publicado, EstadoProducto = estado };
        Assert.Throws<InvalidOperationException>(() =>
            QaMultiSkuCotizacionCanonica.Cotizar(DosItems(), catalogo, SinImpuestos));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.001")]
    [InlineData("10000000000000000.00")]
    public void MultiSkuRechazaPreciosInvalidos(string precio)
    {
        var catalogo = Catalogo();
        catalogo[1] = catalogo[1] with { PrecioB2C = decimal.Parse(
            precio, System.Globalization.CultureInfo.InvariantCulture) };
        Assert.Throws<InvalidOperationException>(() =>
            QaMultiSkuCotizacionCanonica.Cotizar(DosItems(), catalogo, SinImpuestos));
    }

    [Fact]
    public void MultiSkuRechazaImporteAcumuladoFueraDePrecision()
    {
        var catalogo = Catalogo(9999999999999999.99m, 2m);
        Assert.Throws<InvalidOperationException>(() =>
            QaMultiSkuCotizacionCanonica.Cotizar(DosItems(2, 1), catalogo, SinImpuestos));
    }

    [Theory]
    [InlineData("-0.01", "0")]
    [InlineData("0.1301", "-1")]
    [InlineData("1.01", "0")]
    [InlineData("0.13", "5.001")]
    public void MultiSkuRechazaPoliticasMonetariasInvalidas(string tasa, string envio)
    {
        var p = new QaCheckoutPoliticaServidor(
            decimal.Parse(tasa, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(envio, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Throws<InvalidOperationException>(() =>
            QaMultiSkuCotizacionCanonica.Cotizar(DosItems(), Catalogo(), p));
    }

    [Fact]
    public void SolicitudNoPermiteImportesVendedorNiMonedaDelComprador()
    {
        var names = typeof(QaCheckoutItemSolicitud).GetProperties()
            .Select(x => x.Name).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "Cantidad", "ProductoId" }, names);
    }

    [Fact]
    public void TresSkuSeOrdenanYHashNoDependeDelCatalogoExtra()
    {
        var pedido = new[] {
            new QaCheckoutItemSolicitud(C, 1),
            new QaCheckoutItemSolicitud(B, 3),
            new QaCheckoutItemSolicitud(A, 2)
        };
        var catalogo = Catalogo();
        catalogo.Add(new(C, Seller, 7m, "USD", true, "Activo"));
        var total = QaMultiSkuCotizacionCanonica.Cotizar(pedido, catalogo, SinImpuestos);
        Assert.Equal(new[] { A, B, C }, total.Lineas.Select(x => x.ProductoId));
        Assert.Equal(38m, total.MontoTotal);
        catalogo.Add(new(Guid.NewGuid(), Guid.NewGuid(), 100m, "USD", true, "Activo"));
        var segundo = QaMultiSkuCotizacionCanonica.Cotizar(pedido, catalogo, SinImpuestos);
        Assert.Equal(total.PayloadHash, segundo.PayloadHash);
    }
}
