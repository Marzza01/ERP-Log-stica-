using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.API.Services;
using MarketplaceERP.API.Security;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using MarketplaceERP.Shared.DTOs;
using MarketplaceERP.Shared.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// AUDITORÍA LOCAL — no forma parte del candidato GitHub.
/// Usa exclusivamente una base efímera creada bajo postgres@localhost.
/// Pasarela Moq: jamás realiza pagos externos.
/// </summary>
public sealed class CheckoutHttpPostgresAuditTests : IAsyncLifetime
{
    private static readonly Guid OperadorQaAuditado = Guid.Parse("71d6c463-1125-4bed-8151-c2510df1ed5e");
    private readonly string _database = "ci_checkout_http_" + Guid.NewGuid().ToString("N");
    private string? _rootConnection;
    private string? _testConnection;
    private bool _created;

    public async Task InitializeAsync()
    {
        var raw = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION")
            ?? throw new InvalidOperationException("Falta TEST_PG_CONNECTION");
        var c = new NpgsqlConnectionStringBuilder(raw);
        if (!new[] { "localhost", "127.0.0.1", "::1" }.Contains(c.Host, StringComparer.OrdinalIgnoreCase)
            || c.Port != 5432 || c.Database != "postgres" || c.Username != "postgres"
            || string.IsNullOrEmpty(c.Password))
            throw new InvalidOperationException("Prohibido conectar a PostgreSQL que no sea localhost:5432/postgres.");
        _rootConnection = c.ConnectionString;
        c.Database = _database;
        _testConnection = c.ConnectionString;

        await using (var admin = new NpgsqlConnection(_rootConnection))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand("CREATE DATABASE \"" + _database + "\"", admin);
            await create.ExecuteNonQueryAsync();
            _created = true;
        }
        await using (var catalog = NewCatalog()) await catalog.Database.MigrateAsync();
        await using (var finance = NewFinance()) await finance.Database.MigrateAsync();
    }

    private CatalogDbContext NewCatalog() => new(
        new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(_testConnection!, x => x.MigrationsHistoryTable("__EFMigrationsHistory", "catalog")).Options);

    private FinanceDbContext NewFinance() => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(_testConnection!, x => x.MigrationsHistoryTable("__EFMigrationsHistory", "finance")).Options);

    [Theory]
    [InlineData("approved")]
    [InlineData("rejected")]
    [InlineData("uncertain")]
    [InlineData("noStock")]
    [InlineData("concurrente")]
    [InlineData("differentKeysLastStock")]
    [InlineData("afterFinance")]
    [InlineData("afterReserve")]
    [InlineData("afterMap")]
    public async Task HttpV2QaCompraCompleta_Jwt_MultiSku_PagoUnico_YRecuperacion(
        string escenario)
    {
        if (!_created) throw new InvalidOperationException("No existe BD QA.");
        var seller = Guid.NewGuid();
        var buyer = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var location = Guid.NewGuid();
        await using (var catalog = NewCatalog())
        {
            catalog.UbicacionesAlmacen.Add(new UbicacionAlmacen
            {
                Id = location, Codigo = "QA-V2PAY-P-1-R-1-N-1-P-1",
                Pasillo = "QA", Rack = "1", Nivel = 1, Posicion = "1",
                EstaOcupada = true
            });
            foreach (var id in new[] { a, b })
            {
                catalog.Productos.Add(new Producto
                {
                    Id = id, VendedorId = seller, CategoriaId = Guid.NewGuid(),
                    Sku = "QA-V2-" + id.ToString("N"),
                    Nombre = "Checkout QA simulado", CategoriaNombre = "QA",
                    PrecioB2C = 10m, PrecioB2B = 8m, Moneda = "USD",
                    EstaPublicado = true, EstadoProducto = "Activo",
                    StockInicial = 0, StockMinimo = 1,
                    ProveedorNombre = "QA",
                    PaisOrigen = "El Salvador", PesoKg = 1m,
                    DimensionesCm = "1x1x1", UnidadMedida = "Unidad"
                });
                catalog.StockFisico.Add(new StockFisico
                {
                    Id = Guid.NewGuid(), ProductoId = id,
                    UbicacionAlmacenId = location,
                    UbicacionCodigo = "QA-V2PAY-P-1-R-1-N-1-P-1",
                    CantidadTotal = escenario == "noStock" && id == b ? 0 : 1
                });
            }
            await catalog.SaveChangesAsync();
        }

        const string issuer = "qa-v2-http-payment-only";
        const string secret = "ONLY-QA-HTTP-V2-SIMULATED-PAYMENTS-2026-11-09-KEY";
        const string route = "/api/checkout/v2/qa/comprar";
        const string key = "QA-HTTP-V2-ONE-PAY-20261109";
        var fake = new Mock<IQaPagoSimulador>(MockBehavior.Strict);
        if (escenario == "uncertain")
            fake.Setup(x => x.SimularAsync(It.IsAny<Guid>(), 20m, "USD",
                    It.IsAny<string>()))
                .ThrowsAsync(new IOException("Respuesta QA incierta"));
        else
            fake.Setup(x => x.SimularAsync(It.IsAny<Guid>(), 20m, "USD",
                    It.IsAny<string>()))
                .ReturnsAsync(escenario == "rejected"
                    ? new QaRespuestaPagoSimulado(false, null)
                    : new QaRespuestaPagoSimulado(true, "QA-SIM-HTTP-V2-PAY-ONCE"));

        TestServer Host(bool habilitado, string entorno = "Development")
            => new TestServer(new WebHostBuilder()
                .UseEnvironment(entorno)
                .ConfigureAppConfiguration((_, cfg) =>
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Checkout:EnableMultiSkuQaCheckout"] = habilitado ? "true" : "false",
                        ["Checkout:QaFaultPoint"] = habilitado &&
                            escenario is "afterFinance" or "afterReserve" or "afterMap"
                            ? escenario : null
                    }))
                .ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddDbContext<CatalogDbContext>(o =>
                        o.UseNpgsql(_testConnection,
                            x => x.MigrationsHistoryTable("__EFMigrationsHistory", "catalog")));
                    services.AddDbContext<FinanceDbContext>(o =>
                        o.UseNpgsql(_testConnection,
                            x => x.MigrationsHistoryTable("__EFMigrationsHistory", "finance")));
                    services.AddSingleton(new PaymentOperationsMode(false));
                    services.AddSingleton(fake.Object);
                    services.AddMarketplaceSecurity(new JwtSettings
                    {
                        Key = secret, Issuer = issuer, Audience = issuer,
                        ExpirationMinutes = 5
                    });
                    services.AddControllers()
                        .AddApplicationPart(typeof(CheckoutV2QaSimulatedController).Assembly);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(ep => ep.MapControllers());
                }));

        string Token(string role)
        {
            var jwt = new JwtSecurityToken(
                issuer: issuer, audience: issuer,
                claims: new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, buyer.ToString()),
                    new Claim(ClaimTypes.Role, role)
                },
                notBefore: DateTime.UtcNow.AddMinutes(-1),
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                    SecurityAlgorithms.HmacSha256));
            return new JwtSecurityTokenHandler().WriteToken(jwt);
        }

        var pedido = new
        {
            items = new[]
            {
                new { productoId = b, cantidad = 1 },
                new { productoId = a, cantidad = 1 }
            }
        };
        using (var apagado = Host(false))
        using (var disabled = apagado.CreateClient())
        {
            disabled.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", Token("Comprador"));
            disabled.DefaultRequestHeaders.Add("Idempotency-Key", key);
            using var respuesta = await disabled.PostAsJsonAsync(route, pedido);
            Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        }

        // Aunque alguien habilite flags QA por error en Production, la ruta
        // no puede tocar Finance/Catalog ni invocar el simulador.
        using (var produccionSimulada = Host(true, "Production"))
        using (var bloqueado = produccionSimulada.CreateClient())
        {
            bloqueado.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", Token("Comprador"));
            bloqueado.DefaultRequestHeaders.Add("Idempotency-Key", key);
            using var prodRespuesta = await bloqueado.PostAsJsonAsync(route, pedido);
            Assert.Equal(HttpStatusCode.NotFound, prodRespuesta.StatusCode);
        }
        await using (var sinWritesQa = NewFinance())
        {
            Assert.Equal(0, await sinWritesQa.Ordenes.CountAsync());
            Assert.Equal(0, await sinWritesQa.IntentosPago.CountAsync());
        }

        using var servidor = Host(true);
        using (var sinLogin = servidor.CreateClient())
        using (var sinLoginResp = await sinLogin.PostAsJsonAsync(route, pedido))
            Assert.Equal(HttpStatusCode.Unauthorized, sinLoginResp.StatusCode);

        using (var noBuyer = servidor.CreateClient())
        {
            noBuyer.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", Token("Admin"));
            noBuyer.DefaultRequestHeaders.Add("Idempotency-Key", key);
            using var noBuyerResp = await noBuyer.PostAsJsonAsync(route, pedido);
            Assert.Equal(HttpStatusCode.Forbidden, noBuyerResp.StatusCode);
        }

        using var http = servidor.CreateClient();
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Token("Comprador"));
        http.DefaultRequestHeaders.Add("Idempotency-Key", key);

        // Tres interrupciones inyectadas JUSTO DESPUÉS de cada commit:
        // Finance, dos reservas, y mapa intento->reservas. Ninguna debe
        // llegar al simulador; un reintento con la misma clave no recompra.
        if (escenario is "afterFinance" or "afterReserve" or "afterMap")
        {
            using (var primera = await http.PostAsJsonAsync(route, pedido))
            {
                Assert.Equal(HttpStatusCode.ServiceUnavailable, primera.StatusCode);
                var cuerpo = await primera.Content.ReadAsStringAsync();
                Assert.Contains("QaCaida", cuerpo);
            }
            using (var reintento = await http.PostAsJsonAsync(route, pedido))
            {
                Assert.Equal(HttpStatusCode.Conflict, reintento.StatusCode);
                Assert.Contains("CheckoutEnProcesoNoReemitir",
                    await reintento.Content.ReadAsStringAsync());
            }

            fake.VerifyNoOtherCalls();
            await using var fin = NewFinance();
            var ordenInterrumpida = await fin.Ordenes.AsNoTracking().SingleAsync();
            var intentoInterrumpido = await fin.IntentosPago.AsNoTracking().SingleAsync();
            Assert.Equal(EstadoOrden.Pendiente, ordenInterrumpida.Estado);
            Assert.Equal(EstadoIntentoPago.Preparado, intentoInterrumpido.Estado);
            Assert.Equal(escenario == "afterMap" ? 2 : 0,
                await fin.IntentoReservasPago.CountAsync());
            Assert.Empty(await fin.SplitsPago.AsNoTracking().ToListAsync());
            Assert.Empty(await fin.ReembolsosPago.AsNoTracking().ToListAsync());

            await using var cat = NewCatalog();
            var esperado = escenario == "afterFinance" ? 0 : 2;
            var reservasInterrumpidas = await cat.ReservasStock.AsNoTracking()
                .Where(x => x.OrdenId == ordenInterrumpida.Id).ToListAsync();
            Assert.Equal(esperado, reservasInterrumpidas.Count);
            Assert.All(reservasInterrumpidas,
                x => Assert.Equal(EstadoReserva.Pendiente, x.Estado));
            var stocksInterrumpidos = await cat.StockFisico.AsNoTracking().ToListAsync();
            Assert.Equal(2, stocksInterrumpidos.Count);
            Assert.All(stocksInterrumpidos,
                x => Assert.Equal(esperado == 2 ? 1 : 0, x.CantidadReservada));

            // Reconciliacion administrativa SOLO QA, NUNCA con intento Enviado:
            // primera vez falla intencionalmente tras liberar el primer SKU;
            // una UNICA transaccion debe revertir Catalog + Finance.
            await using var adminRecovery = NewFinance();
            var recuperar = new QaPrePagoRecoveryService(
                adminRecovery, NullLoggerFactory.Instance);
            if (esperado == 2)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => recuperar.CancelarSinEnvioAsync(
                        ordenInterrumpida.Id, autorizacionExplicitaQa: true,
                        operadorQaId: OperadorQaAuditado,
                        motivo: MotivoCancelacionPreEnvioQa.VerificacionManualQa,
                        falloTrasLiberarNumeroQa: 1));
                await using var trasRollback = NewCatalog();
                Assert.Equal(2, await trasRollback.ReservasStock.CountAsync(
                    x => x.OrdenId == ordenInterrumpida.Id &&
                         x.Estado == EstadoReserva.Pendiente));
                Assert.All(await trasRollback.StockFisico.AsNoTracking()
                    .ToListAsync(), x => Assert.Equal(1, x.CantidadReservada));
                await using var finRollback = NewFinance();
                Assert.Equal(EstadoOrden.Pendiente,
                    (await finRollback.Ordenes.AsNoTracking().SingleAsync()).Estado);
                Assert.Equal(EstadoIntentoPago.Preparado,
                    (await finRollback.IntentosPago.AsNoTracking().SingleAsync()).Estado);
                // La auditoría tampoco puede existir si se hizo ROLLBACK.
                Assert.Empty(await finRollback.CancelacionesPreEnvioQa.ToListAsync());
            }

            Assert.Equal(esperado == 0
                    ? "CANCELADO_SIN_RESERVAS"
                    : "CANCELADO_RESERVAS_LIBERADAS",
                await recuperar.CancelarSinEnvioAsync(
                    ordenInterrumpida.Id, autorizacionExplicitaQa: true,
                    operadorQaId: OperadorQaAuditado,
                    motivo: escenario switch
                    {
                        "afterFinance" => MotivoCancelacionPreEnvioQa.FallaTrasFinance,
                        "afterReserve" => MotivoCancelacionPreEnvioQa.FallaTrasReservas,
                        "afterMap" => MotivoCancelacionPreEnvioQa.FallaTrasMapa,
                        _ => throw new InvalidOperationException("MOTIVO_QA_NO_RECONOCIDO")
                    }));
            // Es CanceladoAntesEnvio, JAMAS Rechazado por la pasarela.
            fake.VerifyNoOtherCalls();
            await using var postFin = NewFinance();
            Assert.Equal(EstadoOrden.Cancelada,
                (await postFin.Ordenes.AsNoTracking().SingleAsync()).Estado);
            Assert.Equal(EstadoIntentoPago.CanceladoAntesEnvio,
                (await postFin.IntentosPago.AsNoTracking().SingleAsync()).Estado);
            var bitacora = await postFin.CancelacionesPreEnvioQa.AsNoTracking().SingleAsync();
            Assert.Equal(ordenInterrumpida.Id, bitacora.OrdenId);
            Assert.Equal(OperadorQaAuditado, bitacora.OperadorQaId);
            Assert.Equal(esperado, bitacora.ReservasLiberadas);
            Assert.Equal(escenario switch
            {
                "afterFinance" => MotivoCancelacionPreEnvioQa.FallaTrasFinance,
                "afterReserve" => MotivoCancelacionPreEnvioQa.FallaTrasReservas,
                "afterMap" => MotivoCancelacionPreEnvioQa.FallaTrasMapa,
                _ => throw new InvalidOperationException("MOTIVO_QA_NO_RECONOCIDO")
            }, bitacora.Motivo);
            Assert.Empty(await postFin.SplitsPago.ToListAsync());
            await using var postCat = NewCatalog();
            var liberadas = await postCat.ReservasStock.AsNoTracking()
                .Where(x => x.OrdenId == ordenInterrumpida.Id).ToListAsync();
            Assert.Equal(esperado, liberadas.Count);
            Assert.All(liberadas, x =>
            {
                Assert.Equal(EstadoReserva.Liberada, x.Estado);
                Assert.Equal(x.CantidadOriginal, x.CantidadLiberada);
            });
            Assert.All(await postCat.StockFisico.AsNoTracking().ToListAsync(),
                x => Assert.Equal(0, x.CantidadReservada));
            // Verificar HTTP real (TestServer) + PostgreSQL tras el COMMIT:
            // el comprador ve CanceladoAntesEnvio solo con auditoría íntegra.
            using (var seguimiento = await http.GetAsync(
                $"/api/checkout/ordenes/{ordenInterrumpida.Id}/seguimiento"))
            {
                Assert.Equal(HttpStatusCode.OK, seguimiento.StatusCode);
                var dto = await seguimiento.Content
                    .ReadFromJsonAsync<SeguimientoCheckoutDto>();
                Assert.NotNull(dto);
                Assert.Equal("Cancelada", dto.EstadoOrden);
                Assert.Equal("CanceladoAntesEnvio", dto.EstadoPago);
                Assert.Equal(0, dto.CantidadReservadaPendiente);
                Assert.False(dto.RequiereRevision);
                Assert.Empty(dto.Alertas);
            }
            using (var replayCancelado = await http.PostAsJsonAsync(route, pedido))
            {
                Assert.Equal(HttpStatusCode.Conflict, replayCancelado.StatusCode);
                Assert.Contains("CheckoutCancelado",
                    await replayCancelado.Content.ReadAsStringAsync());
            }
            fake.VerifyNoOtherCalls();
            return;
        }

        // Dos CLAVES diferentes compiten simultáneamente por el último stock
        // de DOS SKU. El ganador hace un único pago sintético; el perdedor
        // nunca puede invocar al simulador ni consumir stock.
        if (escenario == "differentKeysLastStock")
        {
            using var segundaClave = servidor.CreateClient();
            segundaClave.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", Token("Comprador"));
            segundaClave.DefaultRequestHeaders.Add("Idempotency-Key",
                "QA-HTTP-V2-OTHER-CART-KEY-20261109");
            var colision = await Task.WhenAll(
                http.PostAsJsonAsync(route, pedido),
                segundaClave.PostAsJsonAsync(route, pedido));
            try
            {
                Assert.Single(colision, r => r.StatusCode == HttpStatusCode.OK);
                Assert.Single(colision, r => r.StatusCode == HttpStatusCode.Conflict);
            }
            finally
            {
                foreach (var response in colision) response.Dispose();
            }

            fake.Verify(x => x.SimularAsync(It.IsAny<Guid>(), 20m, "USD",
                    It.IsAny<string>()), Times.Once());
            fake.VerifyNoOtherCalls();

            await using var financeLast = NewFinance();
            Assert.Equal(1, await financeLast.Ordenes.CountAsync(
                o => o.Estado == EstadoOrden.Aprobada));
            Assert.Equal(1, await financeLast.IntentosPago.CountAsync(
                i => i.Estado == EstadoIntentoPago.Completado));
            Assert.Equal(1, await financeLast.SplitsPago.CountAsync());

            await using var catalogLast = NewCatalog();
            var reservasConfirmadas = await catalogLast.ReservasStock
                .AsNoTracking().Where(x => x.Estado == EstadoReserva.Confirmada)
                .ToListAsync();
            Assert.Equal(2, reservasConfirmadas.Count);
            var stocksLast = await catalogLast.StockFisico.AsNoTracking()
                .ToListAsync();
            Assert.Equal(2, stocksLast.Count);
            Assert.All(stocksLast, s =>
            {
                Assert.Equal(1, s.CantidadTotal);
                Assert.Equal(1, s.CantidadReservada);
            });
            return;
        }

        HttpResponseMessage[] respuestas;
        if (escenario == "concurrente")
        {
            respuestas = await Task.WhenAll(
                http.PostAsJsonAsync(route, pedido),
                http.PostAsJsonAsync(route, pedido));
            Assert.Contains(respuestas, x => x.StatusCode == HttpStatusCode.OK);
            Assert.All(respuestas, x =>
                Assert.Contains(x.StatusCode,
                    new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        }
        else
        {
            respuestas = new[] { await http.PostAsJsonAsync(route, pedido) };
            Assert.Equal(escenario == "approved"
                    ? HttpStatusCode.OK : HttpStatusCode.Conflict,
                respuestas[0].StatusCode);
        }
        foreach (var respuesta in respuestas) respuesta.Dispose();

        using (var replay = await http.PostAsJsonAsync(route, pedido))
        {
            Assert.Equal(escenario is "approved" or "concurrente"
                    ? HttpStatusCode.OK : HttpStatusCode.Conflict,
                replay.StatusCode);
        }
        using (var hashDistinto = await http.PostAsJsonAsync(route, new
        {
            items = new[]
            {
                new { productoId = a, cantidad = 2 },
                new { productoId = b, cantidad = 1 }
            }
        }))
            Assert.Equal(HttpStatusCode.Conflict, hashDistinto.StatusCode);

        // Una clave DISTINTA autoriza otra solicitud comercial, pero no
        // otro cobro cuando el primer pedido consumió el stock completo.
        if (escenario == "approved")
        {
            using var otraClave = servidor.CreateClient();
            otraClave.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", Token("Comprador"));
            otraClave.DefaultRequestHeaders.Add("Idempotency-Key",
                "QA-HTTP-V2-OTRA-CLAVE-20261109");
            using var sinStock = await otraClave.PostAsJsonAsync(route, pedido);
            Assert.Equal(HttpStatusCode.Conflict, sinStock.StatusCode);
        }

        fake.Verify(x => x.SimularAsync(It.IsAny<Guid>(), 20m, "USD",
                It.IsAny<string>()),
            escenario == "noStock" ? Times.Never() : Times.Once());
        fake.VerifyNoOtherCalls();

        await using var financeCheck = NewFinance();
        var order = await financeCheck.Ordenes.AsNoTracking()
            .SingleAsync(x => x.IdempotencyKey == key);
        var intento = await financeCheck.IntentosPago.AsNoTracking()
            .SingleAsync(x => x.OrdenId == order.Id);
        Assert.Equal(buyer, order.CompradorId);
        Assert.Equal(seller, order.VendedorId);
        Assert.Equal(2, await financeCheck.ItemsOrden.CountAsync(
            i => i.OrdenId == order.Id));
        Assert.Equal(escenario == "approved" ? 2 : 1,
            await financeCheck.IntentosPago.CountAsync());
        Assert.Equal(escenario == "approved" ? 2 : 1,
            await financeCheck.Ordenes.CountAsync());
        var splits = await financeCheck.SplitsPago.AsNoTracking().ToListAsync();
        await using var catalogCheck = NewCatalog();
        var reservas = await catalogCheck.ReservasStock.AsNoTracking()
            .Where(r => r.OrdenId == order.Id).ToListAsync();
        var stocks = await catalogCheck.StockFisico.AsNoTracking().ToListAsync();

        if (escenario is "approved" or "concurrente")
        {
            Assert.Equal(EstadoOrden.Aprobada, order.Estado);
            Assert.Equal(EstadoIntentoPago.Completado, intento.Estado);
            Assert.Single(splits);
            Assert.Equal(20m, splits[0].MontoTotal);
            Assert.Equal(2, reservas.Count);
            Assert.All(reservas, r => Assert.Equal(EstadoReserva.Confirmada, r.Estado));
            Assert.All(stocks, s => Assert.Equal(1, s.CantidadReservada));
        }
        else if (escenario == "rejected")
        {
            Assert.Equal(EstadoOrden.Cancelada, order.Estado);
            Assert.Equal(EstadoIntentoPago.Rechazado, intento.Estado);
            Assert.Empty(splits);
            Assert.Equal(2, reservas.Count);
            Assert.All(reservas, r => Assert.Equal(EstadoReserva.Liberada, r.Estado));
            Assert.All(stocks, s => Assert.Equal(0, s.CantidadReservada));
        }
        else if (escenario == "uncertain")
        {
            Assert.Equal(EstadoOrden.Pendiente, order.Estado);
            Assert.Equal(EstadoIntentoPago.Incierto, intento.Estado);
            Assert.Empty(splits);
            Assert.Equal(2, reservas.Count);
            Assert.All(reservas, r => Assert.Equal(EstadoReserva.Pendiente, r.Estado));
            Assert.All(stocks, s => Assert.Equal(1, s.CantidadReservada));
        }
        else
        {
            Assert.Equal(EstadoOrden.Cancelada, order.Estado);
            Assert.Equal(EstadoIntentoPago.Rechazado, intento.Estado);
            Assert.Empty(splits);
            Assert.Empty(reservas);
            Assert.All(stocks, s => Assert.Equal(0, s.CantidadReservada));
        }
    }

    [Fact]
    public async Task HttpV2QaCotizacion_Jwt_Roles_Idempotencia_PreciosServidor_YFlag()
    {
        if (!_created) throw new InvalidOperationException("No existe base PostgreSQL efímera.");
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var seller = Guid.NewGuid();
        var buyer = Guid.NewGuid();
        var location = Guid.NewGuid();
        await using (var catalog = NewCatalog())
        {
            catalog.UbicacionesAlmacen.Add(new UbicacionAlmacen
            {
                Id = location, Codigo = "QA-V2QUOTE-P-1-R-1-N-1-P-1",
                Pasillo = "QA", Rack = "1", Nivel = 1, Posicion = "1", EstaOcupada = true
            });
            foreach (var (id, precio) in new[] { (a, 12.50m), (b, 2m) })
            {
                catalog.Productos.Add(new Producto
                {
                    Id = id, VendedorId = seller, CategoriaId = Guid.NewGuid(),
                    Sku = "QA-QT-" + id.ToString("N"),
                    Nombre = "Cotizacion solo QA", CategoriaNombre = "QA",
                    PrecioB2C = precio, PrecioB2B = precio, Moneda = "USD",
                    EstaPublicado = true, EstadoProducto = "Activo",
                    StockInicial = 0, StockMinimo = 1, ProveedorNombre = "Proveedor QA",
                    PaisOrigen = "El Salvador", PesoKg = 1m, DimensionesCm = "1x1x1",
                    UnidadMedida = "Unidad"
                });
                catalog.StockFisico.Add(new StockFisico
                {
                    Id = Guid.NewGuid(), ProductoId = id,
                    UbicacionAlmacenId = location,
                    UbicacionCodigo = "QA-V2QUOTE-P-1-R-1-N-1-P-1",
                    CantidadTotal = 7
                });
            }
            await catalog.SaveChangesAsync();
        }

        const string issuer = "qa-v2-http-only";
        const string secret = "ONLY-QA-V2-HTTP-NO-REAL-PAYMENTS-2026-11-09-KEY";
        const string route = "/api/checkout/v2/qa/cotizar";
        const string clave = "QA-V2-COTIZACION-20261109";
        TestServer Servidor(bool habilitado, string entorno = "Development",
            bool pagosDeshabilitados = false)
        {
            return new TestServer(new WebHostBuilder()
                .UseEnvironment(entorno)
                .ConfigureAppConfiguration((_, config) =>
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Checkout:EnableMultiSkuQaQuote"] = habilitado ? "true" : "false"
                    }))
                .ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddDbContext<CatalogDbContext>(o => o.UseNpgsql(
                        _testConnection, x =>
                            x.MigrationsHistoryTable("__EFMigrationsHistory", "catalog")));
                    services.AddDbContext<FinanceDbContext>(o => o.UseNpgsql(
                        _testConnection, x =>
                            x.MigrationsHistoryTable("__EFMigrationsHistory", "finance")));
                    services.AddSingleton(new PaymentOperationsMode(pagosDeshabilitados));
                    services.AddMarketplaceSecurity(new JwtSettings
                    {
                        Key = secret, Issuer = issuer, Audience = issuer,
                        ExpirationMinutes = 5
                    });
                    services.AddControllers()
                        .AddApplicationPart(typeof(CheckoutV2QaQuoteController).Assembly);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                }));
        }
        string Token(Guid id, string role)
        {
            var jwt = new JwtSecurityToken(
                issuer: issuer, audience: issuer,
                claims: new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, id.ToString()),
                    new Claim(ClaimTypes.Role, role)
                },
                notBefore: DateTime.UtcNow.AddMinutes(-1),
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                    SecurityAlgorithms.HmacSha256));
            return new JwtSecurityTokenHandler().WriteToken(jwt);
        }

        var cuerpo = new
        {
            items = new[]
            {
                new { productoId = b, cantidad = 1 },
                new { productoId = a, cantidad = 2 },
                new { productoId = b, cantidad = 2 }
            }
        };

        using var servidor = Servidor(true);
        using var anonimo = servidor.CreateClient();
        using (var sinSesion = await anonimo.PostAsJsonAsync(route, cuerpo))
            Assert.Equal(HttpStatusCode.Unauthorized, sinSesion.StatusCode);

        using var administrador = servidor.CreateClient();
        administrador.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Token(Guid.NewGuid(), "Admin"));
        administrador.DefaultRequestHeaders.Add("Idempotency-Key", clave);
        using (var sinRol = await administrador.PostAsJsonAsync(route, cuerpo))
            Assert.Equal(HttpStatusCode.Forbidden, sinRol.StatusCode);

        using var sinIdentidad = servidor.CreateClient();
        sinIdentidad.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Token(Guid.Empty, "Comprador"));
        sinIdentidad.DefaultRequestHeaders.Add("Idempotency-Key", clave);
        using (var invalido = await sinIdentidad.PostAsJsonAsync(route, cuerpo))
            Assert.Equal(HttpStatusCode.Unauthorized, invalido.StatusCode);

        using var comprador = servidor.CreateClient();
        comprador.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Token(buyer, "Comprador"));
        using (var sinClave = await comprador.PostAsJsonAsync(route, cuerpo))
            Assert.Equal(HttpStatusCode.BadRequest, sinClave.StatusCode);
        comprador.DefaultRequestHeaders.Add("Idempotency-Key", clave);

        using (var campoPrecio = await comprador.PostAsJsonAsync(route, new
        {
            items = cuerpo.items,
            montoTotal = 0.01m
        }))
            Assert.Equal(HttpStatusCode.BadRequest, campoPrecio.StatusCode);

        using (var precioArticulo = await comprador.PostAsJsonAsync(route, new
        {
            items = new[]
            {
                new { productoId = a, cantidad = 2, precioUnitario = 0.01m },
                new { productoId = b, cantidad = 3, precioUnitario = 0.01m }
            }
        }))
            Assert.Equal(HttpStatusCode.BadRequest, precioArticulo.StatusCode);

        using var cotizacion = await comprador.PostAsJsonAsync(route, cuerpo);
        Assert.Equal(HttpStatusCode.OK, cotizacion.StatusCode);
        Assert.Equal("no-store", cotizacion.Headers.CacheControl?.ToString());
        using var json = System.Text.Json.JsonDocument.Parse(
            await cotizacion.Content.ReadAsStringAsync());
        var doc = json.RootElement;
        Assert.Equal("CotizacionQaNoReservada",
            doc.GetProperty("tipo").GetString());
        Assert.Equal(buyer.ToString(), doc.GetProperty("compradorId").GetGuid().ToString());
        Assert.Equal(seller, doc.GetProperty("vendedorId").GetGuid());
        Assert.Equal("USD", doc.GetProperty("moneda").GetString());
        Assert.Equal(31m, doc.GetProperty("montoTotal").GetDecimal());
        Assert.False(doc.GetProperty("stockReservado").GetBoolean());
        Assert.False(doc.GetProperty("pagoProcesado").GetBoolean());
        Assert.Equal(2, doc.GetProperty("lineas").GetArrayLength());
        var hash = doc.GetProperty("payloadHash").GetString();

        using (var cotizacionRepetida = await comprador.PostAsJsonAsync(route, new
        {
            items = new[]
            {
                new { productoId = b, cantidad = 3 },
                new { productoId = a, cantidad = 2 }
            }
        }))
        {
            Assert.Equal(HttpStatusCode.OK, cotizacionRepetida.StatusCode);
            using var json2 = System.Text.Json.JsonDocument.Parse(
                await cotizacionRepetida.Content.ReadAsStringAsync());
            Assert.Equal(hash, json2.RootElement.GetProperty("payloadHash").GetString());
        }

        using (var skuAusente = await comprador.PostAsJsonAsync(route, new
        {
            items = new[] {
                new { productoId = a, cantidad = 2 },
                new { productoId = Guid.NewGuid(), cantidad = 1 }
            }
        }))
            Assert.Equal(HttpStatusCode.Conflict, skuAusente.StatusCode);

        // Una clave ya consumida por una orden real nunca recotiza el precio.
        await using (var finance = NewFinance())
        {
            finance.Ordenes.Add(new Orden
            {
                Id = Guid.NewGuid(),
                NumeroOrden = "QA-QUOTE-BLOCKED-" + Guid.NewGuid().ToString("N"),
                CompradorId = buyer, VendedorId = seller,
                IdempotencyKey = clave, CheckoutPayloadHash = hash!,
                SubTotal = 31m, MontoTotal = 31m, Moneda = "USD",
                Estado = EstadoOrden.Pendiente
            });
            await finance.SaveChangesAsync();
        }
        using (var utilizada = await comprador.PostAsJsonAsync(route, cuerpo))
            Assert.Equal(HttpStatusCode.Conflict, utilizada.StatusCode);

        using var sinFlag = Servidor(false);
        using var sinFlagCliente = sinFlag.CreateClient();
        sinFlagCliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Token(buyer, "Comprador"));
        sinFlagCliente.DefaultRequestHeaders.Add("Idempotency-Key", clave);
        using (var disabled = await sinFlagCliente.PostAsJsonAsync(route, cuerpo))
            Assert.Equal(HttpStatusCode.NotFound, disabled.StatusCode);

        using var prod = Servidor(true, "Production");
        using var prodCliente = prod.CreateClient();
        prodCliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Token(buyer, "Comprador"));
        prodCliente.DefaultRequestHeaders.Add("Idempotency-Key", clave);
        using (var disabled = await prodCliente.PostAsJsonAsync(route, cuerpo))
            Assert.Equal(HttpStatusCode.NotFound, disabled.StatusCode);

        using var paymentsOff = Servidor(true, "Development", true);
        using var paymentsOffClient = paymentsOff.CreateClient();
        paymentsOffClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Token(buyer, "Comprador"));
        paymentsOffClient.DefaultRequestHeaders.Add("Idempotency-Key", clave);
        using (var off = await paymentsOffClient.PostAsJsonAsync(route, cuerpo))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, off.StatusCode);

        await using var verifyCatalog = NewCatalog();
        Assert.Empty(await verifyCatalog.ReservasStock.ToListAsync());
        Assert.Empty(await verifyCatalog.MovimientosInventario.ToListAsync());
        Assert.All(await verifyCatalog.StockFisico.ToListAsync(), s =>
        {
            Assert.Equal(7, s.CantidadTotal);
            Assert.Equal(0, s.CantidadReservada);
        });
        await using var verifyFinance = NewFinance();
        Assert.Single(await verifyFinance.Ordenes.ToListAsync());
        Assert.Empty(await verifyFinance.IntentosPago.ToListAsync());
        Assert.Empty(await verifyFinance.SplitsPago.ToListAsync());
    }

    [Theory]
    [InlineData(false, "approved")]
    [InlineData(true, "approved")]
    [InlineData(false, "rejected")]
    [InlineData(false, "uncertain")]
    [InlineData(false, "noStock")]
    [InlineData(true, "lastStock")]
    [InlineData(false, "commissionFailure")]
    [InlineData(false, "confirmFailure")]
    [InlineData(false, "attemptPersistFailure")]
    [InlineData(false, "accountingFailure")]
    [InlineData(false, "finalizeOrderFailure")]
    public async Task HttpJwt_CompraYReintentoMismaClave_NoDuplicanPagoNiReserva(
        bool simultaneos, string escenario)
    {
        if (!_created) throw new InvalidOperationException("No se creó la base temporal.");
        var productoId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        await using (var catalog = NewCatalog())
        {
            catalog.Productos.Add(new Producto
            {
                Id = productoId, VendedorId = sellerId, CategoriaId = Guid.NewGuid(),
                Sku = "QA-CHECKOUT-" + Guid.NewGuid().ToString("N")[..8],
                Nombre = "Producto sintético local", CategoriaNombre = "Auditoría",
                PrecioB2C = 12.50m, PrecioB2B = 10m, Moneda = "USD",
                EstaPublicado = true, EstadoProducto = "Activo",
                StockInicial = 0, StockMinimo = 1, ProveedorNombre = "Proveedor sintético",
                PaisOrigen = "El Salvador", PesoKg = 1m,
                DimensionesCm = "10x10x10", UnidadMedida = "Unidad",
                Descripcion = "Solo ensayo; no se despliega"
            });
            catalog.UbicacionesAlmacen.Add(new UbicacionAlmacen
            {
                Id = locationId, Codigo = "QA-CHK-PAS-1-R-1-N-1-P-1", Pasillo = "QA",
                Rack = "1", Nivel = 1, Posicion = "1", EstaOcupada = true
            });
            catalog.StockFisico.Add(new StockFisico
            {
                Id = stockId, ProductoId = productoId, UbicacionAlmacenId = locationId,
                UbicacionCodigo = "QA-CHK-PAS-1-R-1-N-1-P-1",
                CantidadTotal = escenario == "noStock" ? 1 : escenario == "lastStock" ? 2 : 6
            });
            await catalog.SaveChangesAsync();
        }

        const string issuer = "local-checkout-audit";
        const string secret = "ONLY-LOCAL-AUDIT-CHECKOUT-JWT-DO-NOT-DEPLOY-2026";
        var fakePayments = new Mock<IPaymentGateway>(MockBehavior.Strict);
        if (escenario == "uncertain")
            fakePayments.Setup(x => x.ProcesarPagoAsync(It.IsAny<PagoRequest>()))
                .ThrowsAsync(new IOException("Tiempo de espera de pasarela SIMULADO"));
        else
            fakePayments.Setup(x => x.ProcesarPagoAsync(It.IsAny<PagoRequest>()))
                .ReturnsAsync(escenario == "rejected"
                    ? new PagoResult(false, null, "Rechazo SIMULADO")
                    : new PagoResult(true, "AUDIT-SIM-TRANSACTION-001", null));
        var fakeCommissions = new Mock<ICommissionService>(MockBehavior.Strict);
        var fakeAccounting = new Mock<IContabilidadService>(MockBehavior.Strict);
        if (escenario == "accountingFailure")
            fakeAccounting.Setup(x => x.GenerarAsientoVentaAsync(
                    It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Fallo de asiento SIMULADO después del cobro"));
        if (escenario == "commissionFailure")
            fakeCommissions.Setup(x => x.CalcularComisionAsync(
                    It.IsAny<Guid>(), It.IsAny<decimal>()))
                .ThrowsAsync(new InvalidOperationException("Fallo de comisión SIMULADO tras cobro"));
        else
            fakeCommissions.Setup(x => x.CalcularComisionAsync(
                    It.IsAny<Guid>(), It.IsAny<decimal>()))
                .ReturnsAsync((Guid _, decimal total) => decimal.Round(total * .10m, 2));

        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddDbContext<CatalogDbContext>(o => o.UseNpgsql(_testConnection,
                    x => x.MigrationsHistoryTable("__EFMigrationsHistory", "catalog")));
                services.AddDbContext<FinanceDbContext>(o =>
                {
                    o.UseNpgsql(_testConnection, x => x.MigrationsHistoryTable("__EFMigrationsHistory", "finance"));
                    if (escenario == "attemptPersistFailure")
                        o.AddInterceptors(new FailAttemptInsertInterceptor());
                    if (escenario == "finalizeOrderFailure")
                        o.AddInterceptors(new FailOrderFinalizeInterceptor());
                });
                services.AddScoped<IContabilidadService>(sp =>
                    escenario == "accountingFailure"
                        ? fakeAccounting.Object
                        : new ContabilidadService(
                            sp.GetRequiredService<FinanceDbContext>(),
                            sp.GetRequiredService<ILogger<ContabilidadService>>()));
                services.AddScoped<IStockService>(sp =>
                {
                    var inner = (IStockService)ActivatorUtilities.CreateInstance(sp,
                        typeof(CatalogDbContext).Assembly.GetType(
                            "MarketplaceERP.Catalog.Services.StockService", true)!);
                    if (escenario != "confirmFailure") return inner;
                    var injected = new Mock<IStockService>(MockBehavior.Strict);
                    injected.Setup(s => s.ReservarStockAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<Guid>()))
                        .Returns((Guid id, int qty, Guid order) => inner.ReservarStockAsync(id, qty, order));
                    injected.Setup(s => s.ConfirmarReservaAsync(It.IsAny<Guid>()))
                        .ThrowsAsync(new InvalidOperationException("Fallo de confirmación SIMULADO tras split"));
                    injected.Setup(s => s.LiberarStockAsync(It.IsAny<Guid>()))
                        .Returns((Guid id) => inner.LiberarStockAsync(id));
                    return injected.Object;
                });
                services.AddSingleton(fakePayments.Object);
                services.AddSingleton(fakeCommissions.Object);
                services.AddSingleton(new PaymentOperationsMode(false));
                services.AddScoped<CheckoutOrchestrator>();
                services.AddMarketplaceSecurity(new JwtSettings
                {
                    Key = secret, Issuer = issuer, Audience = issuer,
                    ExpirationMinutes = 5
                });
                services.AddControllers()
                    .AddApplicationPart(typeof(CheckoutController).Assembly);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            }));

        using var anonymous = server.CreateClient();
        using (var unauth = await anonymous.PostAsJsonAsync(
            "/api/checkout", new CheckoutRequest(productoId, 2)))
            Assert.Equal(HttpStatusCode.Unauthorized, unauth.StatusCode);

        var jwt = new JwtSecurityToken(
            issuer: issuer, audience: issuer,
            claims: new[]
            {
                new Claim(ClaimTypes.NameIdentifier, buyerId.ToString()),
                new Claim(ClaimTypes.Role, "Comprador")
            },
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                SecurityAlgorithms.HmacSha256));
        using var http = server.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
        http.DefaultRequestHeaders.Add("Idempotency-Key", "AUDIT-CHECKOUT-KEY-000001");

        var approvedBuyerId = buyerId;
        var expectedReplayStatus = HttpStatusCode.OK;
        if (escenario == "approved")
        {
            HttpResponseMessage first;
            HttpResponseMessage second;
            if (simultaneos)
            {
                // Ejecutar dos POST con idéntica clave ANTES de esperar cualquiera.
                var results = await Task.WhenAll(
                    http.PostAsJsonAsync("/api/checkout", new CheckoutRequest(productoId, 2)),
                    http.PostAsJsonAsync("/api/checkout", new CheckoutRequest(productoId, 2)));
                first = results[0];
                second = results[1];
                Assert.Contains(first.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
                Assert.Contains(second.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
                Assert.True(first.StatusCode == HttpStatusCode.OK || second.StatusCode == HttpStatusCode.OK);
            }
            else
            {
                first = await http.PostAsJsonAsync("/api/checkout", new CheckoutRequest(productoId, 2));
                second = await http.PostAsJsonAsync("/api/checkout", new CheckoutRequest(productoId, 2));
                Assert.Equal(HttpStatusCode.OK, first.StatusCode);
                Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            }
            first.Dispose();
            second.Dispose();
        }
        else if (escenario == "lastStock")
        {
            // Dos compradores distintos y dos claves distintas compiten por
            // las dos últimas unidades disponibles del mismo producto.
            var buyer2 = Guid.NewGuid();
            var token2 = new JwtSecurityToken(
                issuer: issuer, audience: issuer,
                claims: new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, buyer2.ToString()),
                    new Claim(ClaimTypes.Role, "Comprador")
                },
                notBefore: DateTime.UtcNow.AddMinutes(-1),
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                    SecurityAlgorithms.HmacSha256));
            using var otherHttp = server.CreateClient();
            otherHttp.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token2));
            otherHttp.DefaultRequestHeaders.Add("Idempotency-Key", "AUDIT-OTHER-BUYER-KEY-00002");

            var responses = await Task.WhenAll(
                http.PostAsJsonAsync("/api/checkout", new CheckoutRequest(productoId, 2)),
                otherHttp.PostAsJsonAsync("/api/checkout", new CheckoutRequest(productoId, 2)));
            try
            {
                Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
                Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
                expectedReplayStatus = responses[0].StatusCode;
                approvedBuyerId = expectedReplayStatus == HttpStatusCode.OK ? buyerId : buyer2;
            }
            finally
            {
                foreach (var response in responses) response.Dispose();
            }
        }
        else if (escenario is "accountingFailure" or "finalizeOrderFailure")
        {
            using var response = await http.PostAsJsonAsync(
                "/api/checkout", new CheckoutRequest(productoId, 2));
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            using var reconciliationBody = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());
            Assert.Equal("PagoPendienteConciliacion",
                reconciliationBody.RootElement.GetProperty("tipo").GetString());
        }
        else
        {
            using var response = await http.PostAsJsonAsync(
                "/api/checkout", new CheckoutRequest(productoId, 2));
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var responseBody = await response.Content.ReadAsStringAsync();
            var expectedType = escenario switch
            {
                "rejected" => "PagoRechazado",
                "uncertain" or "commissionFailure" or "confirmFailure" or
                    "attemptPersistFailure" or "accountingFailure" => "PagoPendienteConciliacion",
                _ => "StockInsuficiente"
            };
            Assert.Contains(expectedType, responseBody);
        }

        using (var replay = await http.PostAsJsonAsync(
            "/api/checkout", new CheckoutRequest(productoId, 2)))
            Assert.Equal(escenario is "approved" or "lastStock"
                ? expectedReplayStatus : HttpStatusCode.Conflict, replay.StatusCode);
        using (var differentPayload = await http.PostAsJsonAsync(
            "/api/checkout", new CheckoutRequest(productoId, 3)))
            Assert.Equal(HttpStatusCode.Conflict, differentPayload.StatusCode);
        fakePayments.Verify(x => x.ProcesarPagoAsync(It.IsAny<PagoRequest>()),
            escenario is "noStock" or "attemptPersistFailure" ? Times.Never() : Times.Once());

        await using var financeVerify = NewFinance();
        await using var catalogVerify = NewCatalog();
        Assert.Equal(escenario == "lastStock" ? 2 : 1, await financeVerify.Ordenes.CountAsync());
        Assert.Equal(escenario is "noStock" or "attemptPersistFailure" ? 0 : 1,
            await financeVerify.IntentosPago.CountAsync());
        Assert.Equal(escenario is "approved" or "lastStock" or "confirmFailure" or
            "accountingFailure" or "finalizeOrderFailure" ? 1 : 0,
            await financeVerify.SplitsPago.CountAsync());
        var order = escenario == "lastStock"
            ? await financeVerify.Ordenes.SingleAsync(o => o.Estado == EstadoOrden.Aprobada)
            : await financeVerify.Ordenes.SingleAsync();
        var expectedOrderState = escenario switch
        {
            "approved" or "lastStock" => EstadoOrden.Aprobada,
            "uncertain" or "commissionFailure" or "confirmFailure" or "attemptPersistFailure" or
                "accountingFailure" or "finalizeOrderFailure" => EstadoOrden.Pendiente,
            _ => EstadoOrden.Cancelada
        };
        Assert.Equal(expectedOrderState, order.Estado);
        Assert.Equal(25m, order.MontoTotal);
        Assert.Equal(approvedBuyerId, order.CompradorId);

        if (escenario != "noStock" && escenario != "attemptPersistFailure")
        {
            var expectedAttemptState = escenario switch
            {
                "approved" or "lastStock" or "accountingFailure" or
                    "finalizeOrderFailure" => EstadoIntentoPago.Completado,
                "rejected" => EstadoIntentoPago.Rechazado,
                "uncertain" => EstadoIntentoPago.Incierto,
                _ => EstadoIntentoPago.RequiereConciliacion
            };
            Assert.Equal(expectedAttemptState,
                (await financeVerify.IntentosPago.SingleAsync()).Estado);
        }

        var expectedSaleJournals = escenario is "approved" or "lastStock" ? 1 : 0;
        Assert.Equal(expectedSaleJournals,
            await financeVerify.PartidasContables.CountAsync(x =>
                x.TipoOperacion == "Venta" && x.ReferenciaOrigenId == order.Id));
        if (expectedSaleJournals == 1)
        {
            var saleJournal = await financeVerify.PartidasContables.Include(x => x.Lineas)
                .SingleAsync(x => x.TipoOperacion == "Venta" && x.ReferenciaOrigenId == order.Id);
            Assert.Equal(25m, saleJournal.TotalDebe);
            Assert.Equal(25m, saleJournal.TotalHaber);
            Assert.Equal(22.50m,
                saleJournal.Lineas.Single(x => x.CodigoCuenta == "2108").Haber);
            Assert.Equal(2.50m,
                saleJournal.Lineas.Single(x => x.CodigoCuenta == "4101").Haber);
        }
        if (escenario == "accountingFailure")
            fakeAccounting.Verify(x => x.GenerarAsientoVentaAsync(
                It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once());

        if (escenario is "approved" or "accountingFailure" or "finalizeOrderFailure")
        {
            var recoveryGateway = new Mock<IPaymentRecoveryGateway>(MockBehavior.Strict);
            // El cobro está confirmado, pero el cierre comercial/contable
            // quedó pendiente. El evaluador exige revisión manual sin consultar
            // ni mutar la pasarela.
            var evaluator = new EvaluadorConciliacionPago(financeVerify,
                recoveryGateway.Object, NullLogger<EvaluadorConciliacionPago>.Instance);
            var dictamen = await evaluator.EvaluarAsync(order.Id);
            Assert.Equal(escenario == "approved"
                ? DecisionConciliacionPago.YaCerrado
                : DecisionConciliacionPago.RequiereRevisionManual, dictamen.Decision);
            recoveryGateway.VerifyNoOtherCalls();
        }

        var reservations = await catalogVerify.ReservasStock.ToListAsync();
        Assert.Equal(escenario == "noStock" ? 0 : 1, reservations.Count);
        if (reservations.Count == 1)
        {
            Assert.Equal(order.Id, reservations[0].OrdenId);
            Assert.Equal(2, reservations[0].CantidadOriginal);
        }
        // Preflight de cierre es solo diagnóstico: no modifica el pedido ni autoriza
        // ninguna mutación posterior sin bloqueos transaccionales adicionales.
        var preflight = new CierreCheckoutPreflight(financeVerify, catalogVerify);
        var aptitud = await preflight.EvaluarAsync(order.Id);
        if (escenario is "accountingFailure" or "finalizeOrderFailure")
            Assert.True(aptitud.DatosCoherentes,
                "El pago está confirmado; el cierre QA puede recuperar el asiento y la aprobación.");
        else
            Assert.False(aptitud.DatosCoherentes);
        Assert.Equal(expectedOrderState,
            (await financeVerify.Ordenes.AsNoTracking().SingleAsync(o => o.Id == order.Id)).Estado);

        var stock = await catalogVerify.StockFisico.SingleAsync();
        Assert.Equal(escenario == "noStock" ? 1 : escenario == "lastStock" ? 2 : 6, stock.CantidadTotal);
        Assert.Equal(escenario is "approved" or "uncertain" or "lastStock" or
            "commissionFailure" or "confirmFailure" or "accountingFailure" or "finalizeOrderFailure" ? 2 : 0,
            stock.CantidadReservada);
        if (escenario is "accountingFailure" or "finalizeOrderFailure")
        {
            using var tracking = await http.GetAsync($"/api/checkout/ordenes/{order.Id}/seguimiento");
            Assert.Equal(HttpStatusCode.OK, tracking.StatusCode);
            var observed = await tracking.Content.ReadFromJsonAsync<SeguimientoCheckoutDto>();
            Assert.NotNull(observed);
            Assert.Equal("Completado", observed.EstadoPago);
            Assert.True(observed.RequiereRevision);
            Assert.Contains("COBRO_SIN_CIERRE_COMERCIAL", observed.Alertas);
            Assert.DoesNotContain("PAGO_REQUIERE_CONCILIACION", observed.Alertas);
        }

        if (escenario == "attemptPersistFailure")
        {
            Assert.Single(reservations);
            Assert.Equal(0, reservations[0].CantidadPendiente);
            Assert.Empty(fakePayments.Invocations);
            using var tracking = await http.GetAsync($"/api/checkout/ordenes/{order.Id}/seguimiento");
            Assert.Equal(HttpStatusCode.OK, tracking.StatusCode);
            var observed = await tracking.Content.ReadFromJsonAsync<SeguimientoCheckoutDto>();
            Assert.NotNull(observed);
            Assert.Equal("SinIntento", observed.EstadoPago);
            Assert.False(observed.RequiereRevision); // todavía dentro del margen de operación
            // Simula una operación antigua sin intento, no una compra en progreso.
            order.FechaCreacion = DateTime.UtcNow.AddMinutes(-15);
            await financeVerify.SaveChangesAsync();
            using var staleTracking = await http.GetAsync($"/api/checkout/ordenes/{order.Id}/seguimiento");
            Assert.Equal(HttpStatusCode.OK, staleTracking.StatusCode);
            var stale = await staleTracking.Content.ReadFromJsonAsync<SeguimientoCheckoutDto>();
            Assert.NotNull(stale);
            Assert.True(stale.RequiereRevision);
            Assert.Contains("CHECKOUT_PENDIENTE_SIN_INTENTO", stale.Alertas);
            using var forbidden = await http.GetAsync("/api/checkout/pendientes-conciliacion?limite=10");
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            var adminJwt = new JwtSecurityToken(issuer: issuer, audience: issuer,
                claims: new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                    new Claim(ClaimTypes.Role, "Admin") },
                notBefore: DateTime.UtcNow.AddMinutes(-1), expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256));
            using var adminHttp = server.CreateClient();
            adminHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", new JwtSecurityTokenHandler().WriteToken(adminJwt));
            using var queue = await adminHttp.GetAsync("/api/checkout/pendientes-conciliacion?limite=10");
            Assert.Equal(HttpStatusCode.OK, queue.StatusCode);
            var candidates = await queue.Content.ReadFromJsonAsync<List<SeguimientoCheckoutDto>>();
            Assert.NotNull(candidates);
            Assert.Contains(candidates, x => x.OrdenId == order.Id &&
                x.Alertas.Contains("CHECKOUT_PENDIENTE_SIN_INTENTO"));
            // Una orden antigua que NO procede del checkout con clave no debe
            // convertirse en falso positivo de esta alerta.
            var ordenExternaId = Guid.NewGuid();
            financeVerify.Ordenes.Add(new Orden
            {
                Id = ordenExternaId,
                NumeroOrden = "QA-ORDEN-EXTERNA-" + ordenExternaId.ToString("N"),
                CompradorId = buyerId, VendedorId = sellerId,
                Estado = EstadoOrden.Pendiente,
                Moneda = "USD", MontoTotal = 25m, SubTotal = 25m,
                FechaCreacion = DateTime.UtcNow.AddMinutes(-15),
                IdempotencyKey = null
            });
            await financeVerify.SaveChangesAsync();
            using var externaTracking = await http.GetAsync($"/api/checkout/ordenes/{ordenExternaId}/seguimiento");
            Assert.Equal(HttpStatusCode.OK, externaTracking.StatusCode);
            var externa = await externaTracking.Content.ReadFromJsonAsync<SeguimientoCheckoutDto>();
            Assert.NotNull(externa);
            Assert.DoesNotContain("CHECKOUT_PENDIENTE_SIN_INTENTO", externa.Alertas);
            using var queue2 = await adminHttp.GetAsync("/api/checkout/pendientes-conciliacion?limite=10");
            Assert.Equal(HttpStatusCode.OK, queue2.StatusCode);
            var candidatos2 = await queue2.Content.ReadFromJsonAsync<List<SeguimientoCheckoutDto>>();
            Assert.NotNull(candidatos2);
            Assert.DoesNotContain(candidatos2, x => x.OrdenId == ordenExternaId);
        }
        if (escenario == "finalizeOrderFailure")
        {
            // Mutaciones de datos sintéticos SOLO en la base temporal de este test.
            // Un dictamen positivo no debe sobrevivir a divergencias relevantes.
            var split = await financeVerify.SplitsPago.SingleAsync();
            split.MontoMarketplace += 1m;
            await financeVerify.SaveChangesAsync();
            Assert.Contains("SPLIT_NO_CONCILIADO",
                (await preflight.EvaluarAsync(order.Id)).Bloqueos);
            split.MontoMarketplace -= 1m;
            await financeVerify.SaveChangesAsync();

            var reserva = await catalogVerify.ReservasStock.SingleAsync();
            reserva.Estado = EstadoReserva.Pendiente;
            await catalogVerify.SaveChangesAsync();
            Assert.Contains("RESERVA_NO_CONCILIADA",
                (await preflight.EvaluarAsync(order.Id)).Bloqueos);
            reserva.Estado = EstadoReserva.Confirmada;
            await catalogVerify.SaveChangesAsync();

            stock.CantidadReservada = 0;
            await catalogVerify.SaveChangesAsync();
            Assert.Contains("SALDOS_WMS_INCONSISTENTES",
                (await preflight.EvaluarAsync(order.Id)).Bloqueos);
            stock.CantidadReservada = 2;
            await catalogVerify.SaveChangesAsync();

            var pickId = Guid.NewGuid();
            var pick = new PickList
            {
                Id = pickId, OrdenId = order.Id, NumeroOrden = order.NumeroOrden,
                NumeroPickList = "QA-CIERRE-PICK-" + pickId.ToString("N")
            };
            catalogVerify.PickLists.Add(pick);
            await catalogVerify.SaveChangesAsync();
            Assert.Contains("WMS_O_RMA_YA_INICIADO",
                (await preflight.EvaluarAsync(order.Id)).Bloqueos);
            catalogVerify.PickLists.Remove(pick);
            await catalogVerify.SaveChangesAsync();

            var refund = new ReembolsoPago
            {
                OrdenId = order.Id, ReservaId = reserva.Id,
                ClaveIdempotencia = $"REF-{order.Id:N}",
                TransaccionId = split.TransaccionId,
                Monto = order.MontoTotal, Moneda = order.Moneda,
                Estado = EstadoReembolsoPago.Preparado
            };
            financeVerify.ReembolsosPago.Add(refund);
            await financeVerify.SaveChangesAsync();
            Assert.Contains("REEMBOLSO_EXISTENTE",
                (await preflight.EvaluarAsync(order.Id)).Bloqueos);
            financeVerify.ReembolsosPago.Remove(refund);
            await financeVerify.SaveChangesAsync();
            var restaurado = await preflight.EvaluarAsync(order.Id);
            Assert.True(restaurado.DatosCoherentes,
                "El cierre QA debe poder recuperar el asiento y la aprobación tras restaurar el fixture.");
        }
        if (escenario is "commissionFailure" or "confirmFailure")
        {
            var attempt = await financeVerify.IntentosPago.SingleAsync();
            Assert.Equal(EstadoIntentoPago.RequiereConciliacion, attempt.Estado);
            Assert.Equal("AUDIT-SIM-TRANSACTION-001", attempt.TransaccionId);
            Assert.Single(fakePayments.Invocations);
        }
        if (escenario == "finalizeOrderFailure")
        {
            var attempt = await financeVerify.IntentosPago.SingleAsync();
            Assert.Equal(EstadoIntentoPago.Completado, attempt.Estado);
            Assert.Equal("AUDIT-SIM-TRANSACTION-001", attempt.TransaccionId);
            Assert.Equal(EstadoReserva.Confirmada, Assert.Single(reservations).Estado);
            Assert.Single(fakePayments.Invocations);
            // Con split financiero registrado, no se autoriza una devolución
            // automática de la operación pendiente: requiere revisión contable.
            var recoveryGateway = new Mock<IPaymentRecoveryGateway>(MockBehavior.Strict);
            var recoveryStock = new Mock<IStockService>(MockBehavior.Strict);
            var recoveryEvaluator = new EvaluadorConciliacionPago(financeVerify,
                recoveryGateway.Object, NullLogger<EvaluadorConciliacionPago>.Instance);
            var recoveryService = new RecuperacionFinancieraService(financeVerify,
                recoveryEvaluator, recoveryGateway.Object, recoveryStock.Object,
                NullLogger<RecuperacionFinancieraService>.Instance);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => recoveryService.ReembolsarSeguroAsync(order.Id));
            Assert.Empty(await financeVerify.ReembolsosPago.ToListAsync());
            recoveryGateway.VerifyNoOtherCalls();
            recoveryStock.VerifyNoOtherCalls();
            using var tracking = await http.GetAsync($"/api/checkout/ordenes/{order.Id}/seguimiento");
            Assert.Equal(HttpStatusCode.OK, tracking.StatusCode);
            var observed = await tracking.Content.ReadFromJsonAsync<SeguimientoCheckoutDto>();
            Assert.NotNull(observed);
            Assert.Equal("Completado", observed.EstadoPago);
            Assert.True(observed.RequiereRevision);
            Assert.Contains("COBRO_SIN_CIERRE_COMERCIAL", observed.Alertas);
            Assert.DoesNotContain("PAGO_REQUIERE_CONCILIACION", observed.Alertas);
            var adminJwt = new JwtSecurityToken(issuer: issuer, audience: issuer,
                claims: new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                    new Claim(ClaimTypes.Role, "Admin") },
                notBefore: DateTime.UtcNow.AddMinutes(-1), expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256));
            using var adminHttp = server.CreateClient();
            adminHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", new JwtSecurityTokenHandler().WriteToken(adminJwt));
            using var queue = await adminHttp.GetAsync("/api/checkout/pendientes-conciliacion?limite=10");
            Assert.Equal(HttpStatusCode.OK, queue.StatusCode);
            var candidates = await queue.Content.ReadFromJsonAsync<List<SeguimientoCheckoutDto>>();
            Assert.NotNull(candidates);
            Assert.Contains(candidates, x => x.OrdenId == order.Id &&
                x.Alertas.Contains("COBRO_SIN_CIERRE_COMERCIAL"));

            // El prototipo no es accesible por HTTP y rechaza llamadas sin
            // autorización explícita aun en PostgreSQL de ensayo.
            var qa = new CierreCheckoutAdminQaService(financeVerify);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => qa.CerrarAsync(order.Id, autorizacionExplicitaQa: false));
            Assert.Equal(EstadoOrden.Pendiente,
                (await financeVerify.Ordenes.AsNoTracking()
                    .SingleAsync(o => o.Id == order.Id)).Estado);

            // Un error inyectado justo en SaveChanges del cierre debe revertir
            // la transacción: no puede dejar aprobación parcial ni nuevo cobro.
            await using (var dbFallo = new FinanceDbContext(
                new DbContextOptionsBuilder<FinanceDbContext>()
                    .UseNpgsql(_testConnection!, x =>
                        x.MigrationsHistoryTable("__EFMigrationsHistory", "finance"))
                    .AddInterceptors(new FailOrderFinalizeInterceptor()).Options))
            {
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => new CierreCheckoutAdminQaService(dbFallo)
                        .CerrarAsync(order.Id, autorizacionExplicitaQa: true));
            }
            Assert.Equal(EstadoOrden.Pendiente,
                (await financeVerify.Ordenes.AsNoTracking()
                    .SingleAsync(o => o.Id == order.Id)).Estado);
            Assert.Single(fakePayments.Invocations);

            // Carrera controlada con el lock EXACTO que utiliza FulfillmentService
            // al crear un PickList. El cierre QA DEBE esperar la transacción WMS,
            // volver a leer su commit y negarse a aprobar el pedido.
            await using (var writerCatalog = NewCatalog())
            {
                await using var pickTx = await writerCatalog.Database.BeginTransactionAsync();
                long llaveOrden = BitConverter.ToInt64(order.Id.ToByteArray(), 0);
                await writerCatalog.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock({llaveOrden})");
                var pickIdConcurrente = Guid.NewGuid();
                writerCatalog.PickLists.Add(new PickList
                {
                    Id = pickIdConcurrente, OrdenId = order.Id,
                    NumeroOrden = order.NumeroOrden,
                    NumeroPickList = "QA-RACE-" + pickIdConcurrente.ToString("N")
                });
                await writerCatalog.SaveChangesAsync();

                await using var closingDb = NewFinance();
                var cierreMientrasPick = new CierreCheckoutAdminQaService(closingDb)
                    .CerrarAsync(order.Id, autorizacionExplicitaQa: true);
                var primero = await Task.WhenAny(cierreMientrasPick, Task.Delay(350));
                Assert.NotSame(cierreMientrasPick, primero); // sin lock compartido, fallo reproducible
                await pickTx.CommitAsync();
                await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await cierreMientrasPick);
                Assert.Equal(EstadoOrden.Pendiente,
                    (await closingDb.Ordenes.AsNoTracking()
                        .SingleAsync(o => o.Id == order.Id)).Estado);
                var pickInsertado = await writerCatalog.PickLists.SingleAsync(p => p.Id == pickIdConcurrente);
                writerCatalog.PickLists.Remove(pickInsertado);
                await writerCatalog.SaveChangesAsync();
            }

            // Segundo espacio de advisory lock: RmaController usa clave compuesta
            // (6173153, int32 OrdenId). Simulamos una escritura Catalog directa
            // bajo dicho candado (NO un RMA válido por HTTP para orden Pendiente).
            await using (var writerRma = NewCatalog())
            {
                await using var rmaTx = await writerRma.Database.BeginTransactionAsync();
                int rmaKey = BitConverter.ToInt32(order.Id.ToByteArray(), 0);
                await writerRma.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(6173153, {rmaKey})");
                var rmaIdRace = Guid.NewGuid();
                writerRma.DevolucionesRMA.Add(new DevolucionRMA
                {
                    Id = rmaIdRace, OrdenId = order.Id, ProductoId = productoId,
                    NumeroRMA = "QA-RMA-" + rmaIdRace.ToString("N"),
                    Cantidad = 1, MotivoCliente = "Ensayo de colisión RMA"
                });
                await writerRma.SaveChangesAsync();
                await using var closingRmaDb = NewFinance();
                var cierreMientrasRma = new CierreCheckoutAdminQaService(closingRmaDb)
                    .CerrarAsync(order.Id, autorizacionExplicitaQa: true);
                var primeroRma = await Task.WhenAny(cierreMientrasRma, Task.Delay(350));
                Assert.NotSame(cierreMientrasRma, primeroRma);
                await rmaTx.CommitAsync();
                await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await cierreMientrasRma);
                Assert.Equal(EstadoOrden.Pendiente,
                    (await closingRmaDb.Ordenes.AsNoTracking()
                        .SingleAsync(o => o.Id == order.Id)).Estado);
                writerRma.DevolucionesRMA.Remove(
                    await writerRma.DevolucionesRMA.SingleAsync(r => r.Id == rmaIdRace));
                await writerRma.SaveChangesAsync();
            }

            // Dos administradores QA con conexiones independientes compiten
            // por cerrar exactamente la misma orden. Solo uno la aprueba.
            await using var dbA = NewFinance();
            await using var dbB = NewFinance();
            var t1 = new CierreCheckoutAdminQaService(dbA)
                .CerrarAsync(order.Id, autorizacionExplicitaQa: true);
            var t2 = new CierreCheckoutAdminQaService(dbB)
                .CerrarAsync(order.Id, autorizacionExplicitaQa: true);
            var cierres = await Task.WhenAll(t1, t2);
            Assert.Single(cierres, x => x == "APROBADA");
            Assert.Single(cierres, x => x == "YA_APROBADA");
            await using var dbComprobar = NewFinance();
            var cerrada = await dbComprobar.Ordenes.AsNoTracking()
                .SingleAsync(o => o.Id == order.Id);
            Assert.Equal(EstadoOrden.Aprobada, cerrada.Estado);
            Assert.NotNull(cerrada.FechaAprobacion);
            Assert.Equal("YA_APROBADA", await new CierreCheckoutAdminQaService(dbComprobar)
                .CerrarAsync(order.Id, autorizacionExplicitaQa: true));
            Assert.Single(await dbComprobar.IntentosPago.ToListAsync());
            Assert.Single(await dbComprobar.SplitsPago.ToListAsync());
            var asientoRecuperado = await dbComprobar.PartidasContables.Include(x => x.Lineas)
                .SingleAsync(x => x.TipoOperacion == "Venta" && x.ReferenciaOrigenId == order.Id);
            Assert.Equal(25m, asientoRecuperado.TotalDebe);
            Assert.Equal(25m, asientoRecuperado.TotalHaber);
            Assert.Equal(22.50m,
                asientoRecuperado.Lineas.Single(x => x.CodigoCuenta == "2108").Haber);
            Assert.Equal(2.50m,
                asientoRecuperado.Lineas.Single(x => x.CodigoCuenta == "4101").Haber);
            Assert.Single(fakePayments.Invocations);
            using var terminado = await http.GetAsync($"/api/checkout/ordenes/{order.Id}/seguimiento");
            Assert.Equal(HttpStatusCode.OK, terminado.StatusCode);
            var final = await terminado.Content.ReadFromJsonAsync<SeguimientoCheckoutDto>();
            Assert.NotNull(final);
            Assert.False(final.RequiereRevision);
        }
    }

    [Fact]
    public async Task QaCarritosOrdenInverso_CaidaTrasSegundaReserva_NoDejanStockParcialNiDobleReserva()
    {
        if (!_created) throw new InvalidOperationException("BD QA inexistente.");
        var seller = Guid.NewGuid();
        var skuA = Guid.Parse("00000000-0000-0000-0000-0000000000A1");
        var skuB = Guid.Parse("00000000-0000-0000-0000-0000000000B1");
        var location = Guid.NewGuid();
        await using (var seed = NewCatalog())
        {
            seed.UbicacionesAlmacen.Add(new UbicacionAlmacen
            {
                Id = location,
                Codigo = "QA-INVERSO-A-1-1-1",
                Pasillo = "QA", Rack = "1", Nivel = 1, Posicion = "1",
                EstaOcupada = true
            });
            foreach (var sku in new[] { skuA, skuB })
            {
                seed.Productos.Add(new Producto
                {
                    Id = sku, VendedorId = seller, CategoriaId = Guid.NewGuid(),
                    Sku = "QA-INV-" + sku.ToString("N"),
                    Nombre = "Producto carrera QA", CategoriaNombre = "Auditoria",
                    PrecioB2C = 10m, PrecioB2B = 8m, Moneda = "USD",
                    EstaPublicado = true, EstadoProducto = "Activo",
                    StockInicial = 0, StockMinimo = 1,
                    ProveedorNombre = "QA test", PaisOrigen = "El Salvador",
                    PesoKg = 1m, DimensionesCm = "10x10x10", UnidadMedida = "Unidad"
                });
                seed.StockFisico.Add(new StockFisico
                {
                    Id = Guid.NewGuid(), ProductoId = sku, UbicacionAlmacenId = location,
                    UbicacionCodigo = "QA-INVERSO-A-1-1-1",
                    CantidadTotal = 1
                });
            }
            await seed.SaveChangesAsync();
        }

        var derecho = new List<ItemReservaCarritoQa> {
            new(skuA, 1), new(skuB, 1)
        };
        var inverso = derecho.AsEnumerable().Reverse().ToList();

        // Un error después de escribir la SEGUNDA reserva aún debe deshacer
        // todas las escrituras, incluidas las del primer SKU y sus movimientos.
        await using (var crashDb = NewCatalog())
        {
            var qa = new ReservaCarritoAtomicaQaService(
                crashDb, NullLoggerFactory.Instance);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                qa.ReservarAsync(Guid.NewGuid(), inverso,
                    autorizacionExplicitaQa: true,
                    falloTrasReservaNumeroQa: 2));
            Assert.Contains("QA_FALLO_TRAS_RESERVA_NUMERO_2", error.Message);
        }
        await using (var clean = NewCatalog())
        {
            Assert.Empty(await clean.ReservasStock.AsNoTracking().ToListAsync());
            Assert.Empty(await clean.MovimientosInventario.AsNoTracking().ToListAsync());
            Assert.All(await clean.StockFisico.AsNoTracking().ToListAsync(),
                x => Assert.Equal(0, x.CantidadReservada));
        }

        // Dos carritos diferentes pidieron SKUs en orden opuesto. El servicio
        // los bloquea SIEMPRE por ProductoId, nunca por orden del navegador.
        var orden1 = Guid.NewGuid();
        var orden2 = Guid.NewGuid();
        async Task<bool> CompetirAsync(Guid orden, List<ItemReservaCarritoQa> pedido)
        {
            await using var catalog = NewCatalog();
            try
            {
                var recibido = await new ReservaCarritoAtomicaQaService(
                    catalog, NullLoggerFactory.Instance)
                    .ReservarAsync(orden, pedido, autorizacionExplicitaQa: true);
                Assert.Equal(2, recibido.ReservaIds.Count);
                return true;
            }
            catch (InvalidOperationException ex) when (
                ex.Message.StartsWith("STOCK_INSUFICIENTE_MULTISKU_", StringComparison.Ordinal))
            {
                return false;
            }
        }
        var ganadores = await Task.WhenAll(
            CompetirAsync(orden1, derecho),
            CompetirAsync(orden2, inverso));
        Assert.Single(ganadores, x => x);

        await using var final = NewCatalog();
        var reservas = await final.ReservasStock.AsNoTracking().ToListAsync();
        Assert.Equal(2, reservas.Count);
        Assert.Single(reservas.Select(x => x.OrdenId).Distinct());
        Assert.Equal(new[] { skuA, skuB },
            reservas.Select(x => x.ProductoId).OrderBy(x => x).ToArray());
        Assert.All(reservas, x => Assert.Equal(EstadoReserva.Pendiente, x.Estado));
        Assert.Equal(2, await final.MovimientosInventario.CountAsync());
        Assert.All(await final.StockFisico.AsNoTracking().ToListAsync(), x =>
        {
            Assert.Equal(1, x.CantidadTotal);
            Assert.Equal(1, x.CantidadReservada);
        });
        await using var finance = NewFinance();
        Assert.Empty(await finance.IntentosPago.ToListAsync());
        Assert.Empty(await finance.SplitsPago.ToListAsync());
    }

    [Fact]
    public async Task QaCarritoMultiSku_ReservaAtomica_Rollback_IdempotenciaYConcurrencia()
    {
        if (!_created) throw new InvalidOperationException("No existe BD temporal.");
        var seller = Guid.NewGuid();
        var productoA = new Guid("00000000-0000-0000-0000-000000000001");
        var productoB = new Guid("00000000-0000-0000-0000-000000000002");
        var locationId = Guid.NewGuid();
        var ordenId = Guid.NewGuid();
        var items = new List<ItemReservaCarritoQa>
        {
            new(productoA, 2), new(productoB, 2)
        };
        await using (var seed = NewCatalog())
        {
            foreach (var id in new[] { productoA, productoB })
                seed.Productos.Add(new Producto
                {
                    Id = id, VendedorId = seller, CategoriaId = Guid.NewGuid(),
                    Sku = "QA-CART-" + id.ToString("N"),
                    Nombre = "SKU sintético carrito", CategoriaNombre = "Auditoría",
                    PrecioB2C = 10m, PrecioB2B = 8m, Moneda = "USD",
                    EstaPublicado = true, EstadoProducto = "Activo",
                    StockInicial = 0, StockMinimo = 1,
                    ProveedorNombre = "Proveedor sintético", PaisOrigen = "El Salvador",
                    PesoKg = 1m, DimensionesCm = "10x10x10", UnidadMedida = "Unidad"
                });
            seed.UbicacionesAlmacen.Add(new UbicacionAlmacen
            {
                Id = locationId, Codigo = "QA-CARRITO-PAS-1-R-1-N-1-P-1",
                Pasillo = "QA", Rack = "1", Nivel = 1, Posicion = "1",
                EstaOcupada = true
            });
            seed.StockFisico.Add(new StockFisico
            {
                Id = Guid.NewGuid(), ProductoId = productoA,
                UbicacionAlmacenId = locationId,
                UbicacionCodigo = "QA-CARRITO-PAS-1-R-1-N-1-P-1",
                CantidadTotal = 5
            });
            seed.StockFisico.Add(new StockFisico
            {
                Id = Guid.NewGuid(), ProductoId = productoB,
                UbicacionAlmacenId = locationId,
                UbicacionCodigo = "QA-CARRITO-PAS-1-R-1-N-1-P-1",
                CantidadTotal = 1
            });
            await seed.SaveChangesAsync();
        }

        await using (var catalog = NewCatalog())
        {
            var qa = new ReservaCarritoAtomicaQaService(catalog, NullLoggerFactory.Instance);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => qa.ReservarAsync(ordenId, items, autorizacionExplicitaQa: false));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => qa.ReservarAsync(ordenId, items, autorizacionExplicitaQa: true));
            Assert.Empty(await catalog.ReservasStock.ToListAsync());
            Assert.Empty(await catalog.MovimientosInventario.ToListAsync());
            Assert.All(await catalog.StockFisico.ToListAsync(),
                x => Assert.Equal(0, x.CantidadReservada));
        }

        await using (var seed = NewCatalog())
        {
            var stockB = await seed.StockFisico.SingleAsync(s => s.ProductoId == productoB);
            stockB.CantidadTotal = 5;
            await seed.SaveChangesAsync();
        }

        await using (var catalog = NewCatalog())
        {
            var qa = new ReservaCarritoAtomicaQaService(catalog, NullLoggerFactory.Instance);
            var primero = await qa.ReservarAsync(ordenId, items, autorizacionExplicitaQa: true);
            Assert.Equal(2, primero.ReservaIds.Count);

            // Intento financiero PREPARADO y orden de 2 SKU, SIN proveedor.
            // La relación intento → 2 reservas debe persistirse durablemente
            // antes de llamar a cualquier pasarela.
            var intentoMultiId = Guid.NewGuid();
            await using (var seedFinance = NewFinance())
            {
                seedFinance.Ordenes.Add(new Orden
                {
                    Id = ordenId, NumeroOrden = "QA-MULTISKU-" + ordenId.ToString("N"),
                    CompradorId = Guid.NewGuid(), VendedorId = seller,
                    IdempotencyKey = "QA-MULTISKU-MAP-20261108",
                    CheckoutPayloadHash = new string('A', 64),
                    SubTotal = 40m, MontoTotal = 40m, Moneda = "USD",
                    Estado = EstadoOrden.Pendiente,
                    Items = new List<ItemOrden>
                    {
                        new() {
                            Id = Guid.NewGuid(), OrdenId = ordenId,
                            ProductoId = productoA, SkuReferencia = "QA-PROD-A",
                            NombreProducto = "SKU A", Cantidad = 2, PrecioUnitario = 10m
                        },
                        new() {
                            Id = Guid.NewGuid(), OrdenId = ordenId,
                            ProductoId = productoB, SkuReferencia = "QA-PROD-B",
                            NombreProducto = "SKU B", Cantidad = 2, PrecioUnitario = 10m
                        }
                    }
                });
                seedFinance.IntentosPago.Add(new IntentoPago
                {
                    Id = intentoMultiId, OrdenId = ordenId, ReservaId = null,
                    ReferenciaOperacion = $"PAY-{ordenId:N}",
                    Monto = 40m, Moneda = "USD", Estado = EstadoIntentoPago.Preparado
                });
                await seedFinance.SaveChangesAsync();
            }

            var links = (await catalog.ReservasStock.AsNoTracking()
                .Where(r => r.OrdenId == ordenId)
                .OrderBy(r => r.ProductoId).ToListAsync())
                .Select(r => new QaVinculoReserva(
                    r.Id, r.ProductoId, r.CantidadOriginal)).ToList();
            await using (var mappingDb = NewFinance())
            {
                var mapper = new QaIntentoReservasMapService(mappingDb);
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => mapper.RegistrarAsync(intentoMultiId, links,
                        autorizacionExplicitaQa: false));
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => mapper.RegistrarAsync(intentoMultiId, links,
                        autorizacionExplicitaQa: true,
                        inyectarFalloTrasPrimerInsertQa: true));
                Assert.Empty(await mappingDb.IntentoReservasPago
                    .AsNoTracking().Where(x => x.IntentoPagoId == intentoMultiId)
                    .ToListAsync());

                Assert.Equal("REGISTRADO", await mapper.RegistrarAsync(
                    intentoMultiId, links, autorizacionExplicitaQa: true));
                Assert.Equal("YA_REGISTRADO", await mapper.RegistrarAsync(
                    intentoMultiId, links, autorizacionExplicitaQa: true));
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => mapper.RegistrarAsync(intentoMultiId,
                        new List<QaVinculoReserva> {
                            links[0] with { Cantidad = 3 }, links[1]
                        }, autorizacionExplicitaQa: true));
            }
            await using (var nuevoFinance = NewFinance())
            {
                var persisted = await nuevoFinance.IntentoReservasPago.AsNoTracking()
                    .Where(x => x.IntentoPagoId == intentoMultiId)
                    .OrderBy(x => x.ProductoId).ToListAsync();
                Assert.Equal(2, persisted.Count);
                Assert.Equal(links.Select(x => x.ReservaId),
                    persisted.Select(x => x.ReservaId));
                Assert.All(persisted, x => Assert.Equal("MultiSkuQa", x.Procedencia));
                Assert.Equal(EstadoIntentoPago.Preparado,
                    (await nuevoFinance.IntentosPago.SingleAsync(x => x.Id == intentoMultiId)).Estado);
                Assert.Empty(await nuevoFinance.SplitsPago.ToListAsync());
            }

            // Un único callback de PAGO SIMULADO, jamás IPaymentGateway.
            // Enviado se hace durable ANTES del callback: el reintento no
            // vuelve a invocar el simulador, aunque no haya cierre comercial.
            var paymentStub = new Mock<IQaPagoSimulador>(MockBehavior.Strict);
            paymentStub.Setup(x => x.SimularAsync(
                    ordenId, 40m, "USD", $"PAY-{ordenId:N}"))
                .ReturnsAsync(new QaRespuestaPagoSimulado(
                    true, "QA-SIM-TRANSACCION-UNA-VEZ"));
            await using (var pagoDb = NewFinance())
            {
                var oneCall = new QaMultiSkuPagoSimuladoService(
                    pagoDb, paymentStub.Object);
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => oneCall.SimularUnaVezAsync(intentoMultiId,
                        autorizacionExplicitaQa: false));
                Assert.Equal("APROBADO_SIMULADO_SIN_CIERRE_COMERCIAL",
                    await oneCall.SimularUnaVezAsync(intentoMultiId,
                        autorizacionExplicitaQa: true));
                Assert.Equal("YA_EXISTE_INTENTO_NO_REEMITIR",
                    await oneCall.SimularUnaVezAsync(intentoMultiId,
                        autorizacionExplicitaQa: true));
            }
            paymentStub.Verify(x => x.SimularAsync(
                ordenId, 40m, "USD", $"PAY-{ordenId:N}"), Times.Once());
            paymentStub.VerifyNoOtherCalls();
            await using (var pagoCheckDb = NewFinance())
            {
                var paymentState = await pagoCheckDb.IntentosPago.AsNoTracking()
                    .SingleAsync(x => x.Id == intentoMultiId);
                Assert.Equal(EstadoIntentoPago.Aprobado, paymentState.Estado);
                Assert.Equal("QA-SIM-TRANSACCION-UNA-VEZ", paymentState.TransaccionId);
                Assert.Equal(2, await pagoCheckDb.IntentoReservasPago.CountAsync(
                    x => x.IntentoPagoId == intentoMultiId));
                Assert.Empty(await pagoCheckDb.SplitsPago.ToListAsync());
                Assert.Equal(EstadoOrden.Pendiente,
                    (await pagoCheckDb.Ordenes.SingleAsync(x => x.Id == ordenId)).Estado);
            }

            var repetido = await qa.ReservarAsync(ordenId, items, autorizacionExplicitaQa: true);
            Assert.Equal(primero.ReservaIds, repetido.ReservaIds);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => qa.ReservarAsync(ordenId,
                    new List<ItemReservaCarritoQa> { new(productoA, 3), new(productoB, 2) },
                    autorizacionExplicitaQa: true));
            Assert.Equal(2, await catalog.ReservasStock.CountAsync());
            Assert.Equal(2, await catalog.MovimientosInventario.CountAsync());
            Assert.All(await catalog.StockFisico.ToListAsync(),
                x => Assert.Equal(2, x.CantidadReservada));

            // Simular falla DESPUÉS de confirmar la primera reserva, justo
            // ANTES de confirmar la segunda. El rollback deshace ambas.
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => qa.ConfirmarAsync(ordenId, primero.ReservaIds,
                    autorizacionExplicitaQa: true, falloEnReservaNumeroQa: 2));
            Assert.All(await catalog.ReservasStock.AsNoTracking()
                .Where(r => r.OrdenId == ordenId).ToListAsync(),
                x => Assert.Equal(EstadoReserva.Pendiente, x.Estado));
            Assert.Equal(2, await catalog.MovimientosInventario.CountAsync());

            Assert.Equal("CONFIRMADAS", await qa.ConfirmarAsync(
                ordenId, primero.ReservaIds, autorizacionExplicitaQa: true));
            Assert.All(await catalog.ReservasStock.AsNoTracking()
                .Where(r => r.OrdenId == ordenId).ToListAsync(),
                x => Assert.Equal(EstadoReserva.Confirmada, x.Estado));
            Assert.Equal("YA_CONFIRMADAS", await qa.ConfirmarAsync(
                ordenId, primero.ReservaIds, autorizacionExplicitaQa: true));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => qa.ReservarAsync(ordenId, items, autorizacionExplicitaQa: true));
            Assert.All(await catalog.StockFisico.ToListAsync(),
                x => Assert.Equal(2, x.CantidadReservada));
        }

        // Dos peticiones simultáneas con la misma orden NO pueden reservar
        // dos veces: cada una recibe exactamente los mismos IDs de reserva.
        var ordenConcurrente = Guid.NewGuid();
        await using var db1 = NewCatalog();
        await using var db2 = NewCatalog();
        var task1 = new ReservaCarritoAtomicaQaService(db1, NullLoggerFactory.Instance)
            .ReservarAsync(ordenConcurrente, items, autorizacionExplicitaQa: true);
        var task2 = new ReservaCarritoAtomicaQaService(db2, NullLoggerFactory.Instance)
            .ReservarAsync(ordenConcurrente, items, autorizacionExplicitaQa: true);
        var resultados = await Task.WhenAll(task1, task2);
        Assert.Equal(resultados[0].ReservaIds, resultados[1].ReservaIds);
        await using var comprobar = NewCatalog();
        Assert.Equal(2, await comprobar.ReservasStock.CountAsync(r => r.OrdenId == ordenConcurrente));
        Assert.Equal(4, await comprobar.ReservasStock.CountAsync());
        Assert.Equal(4, await comprobar.MovimientosInventario.CountAsync());
        Assert.All(await comprobar.StockFisico.ToListAsync(),
            x => Assert.Equal(4, x.CantidadReservada));

        // Con una unidad restante de cada SKU, dos carritos DIFERENTES
        // compiten. Solo uno puede reservar el par completo: nunca sobreventa.
        async Task<bool> ReservarUltimoParAsync(Guid order)
        {
            await using var db = NewCatalog();
            try
            {
                await new ReservaCarritoAtomicaQaService(db, NullLoggerFactory.Instance)
                    .ReservarAsync(order, new List<ItemReservaCarritoQa>
                    {
                        new(productoA, 1), new(productoB, 1)
                    }, autorizacionExplicitaQa: true);
                return true;
            }
            catch (InvalidOperationException ex) when (
                ex.Message.StartsWith("STOCK_INSUFICIENTE_MULTISKU_", StringComparison.Ordinal))
            {
                return false;
            }
        }
        var competencias = await Task.WhenAll(
            ReservarUltimoParAsync(Guid.NewGuid()),
            ReservarUltimoParAsync(Guid.NewGuid()));
        Assert.Single(competencias, x => x);
        await using var conteoFinal = NewCatalog();
        Assert.Equal(6, await conteoFinal.ReservasStock.CountAsync());
        Assert.Equal(6, await conteoFinal.MovimientosInventario.CountAsync());
        Assert.All(await conteoFinal.StockFisico.ToListAsync(), x =>
        {
            Assert.Equal(5, x.CantidadTotal);
            Assert.Equal(5, x.CantidadReservada);
        });

        // No se mezclan vendedores: el modelo de liquidación actual asigna
        // un solo VendedorId por orden. Ningún SKU debe quedar reservado.
        var productoOtroVendedor = await conteoFinal.Productos.SingleAsync(p => p.Id == productoB);
        productoOtroVendedor.VendedorId = Guid.NewGuid();
        await conteoFinal.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ReservaCarritoAtomicaQaService(conteoFinal, NullLoggerFactory.Instance)
                .ReservarAsync(Guid.NewGuid(), items, autorizacionExplicitaQa: true));
        Assert.Equal(6, await conteoFinal.ReservasStock.CountAsync());

        // Rechazo explícito sintético: NO liberar si la respuesta del pago
        // fuese incierta. Se requieren ambos flags QA, sin pasarela.
        await using var compensacion = NewCatalog();
        var qaRechazo = new ReservaCarritoAtomicaQaService(
            compensacion, NullLoggerFactory.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => qaRechazo.LiberarTrasRechazoAsync(
                ordenConcurrente, resultados[0].ReservaIds,
                autorizacionExplicitaQa: true, rechazoExplicitoSimulado: false));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => qaRechazo.LiberarTrasRechazoAsync(
                ordenConcurrente, resultados[0].ReservaIds,
                autorizacionExplicitaQa: true, rechazoExplicitoSimulado: true,
                falloEnReservaNumeroQa: 2));
        Assert.All(await compensacion.ReservasStock.AsNoTracking()
            .Where(r => r.OrdenId == ordenConcurrente).ToListAsync(),
            x => Assert.Equal(EstadoReserva.Pendiente, x.Estado));
        Assert.Equal(6, await compensacion.MovimientosInventario.CountAsync());
        Assert.All(await compensacion.StockFisico.ToListAsync(),
            x => Assert.Equal(5, x.CantidadReservada));

        Assert.Equal("LIBERADAS", await qaRechazo.LiberarTrasRechazoAsync(
            ordenConcurrente, resultados[0].ReservaIds,
            autorizacionExplicitaQa: true, rechazoExplicitoSimulado: true));
        Assert.All(await compensacion.ReservasStock.AsNoTracking()
            .Where(r => r.OrdenId == ordenConcurrente).ToListAsync(),
            x => Assert.Equal(EstadoReserva.Liberada, x.Estado));
        Assert.Equal(8, await compensacion.MovimientosInventario.CountAsync());
        Assert.All(await compensacion.StockFisico.ToListAsync(),
            x => Assert.Equal(3, x.CantidadReservada));
        Assert.Equal("YA_LIBERADAS", await qaRechazo.LiberarTrasRechazoAsync(
            ordenConcurrente, resultados[0].ReservaIds,
            autorizacionExplicitaQa: true, rechazoExplicitoSimulado: true));
        Assert.Equal(8, await compensacion.MovimientosInventario.CountAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => qaRechazo.ConfirmarAsync(ordenConcurrente, resultados[0].ReservaIds,
                autorizacionExplicitaQa: true));
    }

    [Theory]
    [InlineData("sinPago")]
    [InlineData("pagoIncierto")]
    [InlineData("pagoRechazado")]
    [InlineData("mapaFaltante")]
    [InlineData("stockInvalido")]
    [InlineData("wmsIniciado")]
    [InlineData("vendedorIncorrecto")]
    [InlineData("ordenCancelada")]
    [InlineData("montoIncorrecto")]
    [InlineData("splitPreexistente")]
    public async Task QaCierreMultiSku_RechazaEstadosInsegurosSinMutarInventario(
        string defecto)
    {
        var (ordenId, intentoId, reservaIds) = await PrepararCarritoPagoSimuladoAsync();
        if (defecto != "sinPago")
        {
            var mock = new Mock<IQaPagoSimulador>(MockBehavior.Strict);
            if (defecto == "pagoIncierto")
                mock.Setup(s => s.SimularAsync(
                    ordenId, 20m, "USD", $"PAY-{ordenId:N}"))
                    .ThrowsAsync(new IOException("Timeout del simulador QA"));
            else
                mock.Setup(s => s.SimularAsync(
                    ordenId, 20m, "USD", $"PAY-{ordenId:N}"))
                    .ReturnsAsync(defecto == "pagoRechazado"
                        ? new QaRespuestaPagoSimulado(false, null)
                        : new QaRespuestaPagoSimulado(true, "QA-SIM-ESTADO-NEGATIVO"));

            await using var paymentDb = NewFinance();
            await new QaMultiSkuPagoSimuladoService(paymentDb, mock.Object)
                .SimularUnaVezAsync(intentoId, autorizacionExplicitaQa: true);
            mock.Verify(s => s.SimularAsync(ordenId, 20m, "USD",
                $"PAY-{ordenId:N}"), Times.Once());
        }

        if (defecto is "mapaFaltante" or "ordenCancelada" or
            "montoIncorrecto" or "splitPreexistente")
        {
            await using var finance = NewFinance();
            if (defecto == "mapaFaltante")
            {
                var row = await finance.IntentoReservasPago
                    .FirstAsync(x => x.IntentoPagoId == intentoId);
                finance.IntentoReservasPago.Remove(row);
            }
            else if (defecto == "ordenCancelada")
            {
                var order = await finance.Ordenes.SingleAsync(x => x.Id == ordenId);
                order.Estado = EstadoOrden.Cancelada;
                order.FechaCancelacion = DateTime.UtcNow;
            }
            else if (defecto == "montoIncorrecto")
            {
                var order = await finance.Ordenes.SingleAsync(x => x.Id == ordenId);
                order.MontoTotal = 21m;
            }
            else if (defecto == "splitPreexistente")
            {
                finance.SplitsPago.Add(new SplitPago
                {
                    Id = Guid.NewGuid(), OrdenId = ordenId,
                    MontoTotal = 20m, MontoVendedor = 18m,
                    MontoMarketplace = 2m,
                    PorcentajeVendedor = 90m,
                    PorcentajeMarketplace = 10m,
                    TransaccionId = "QA-SIM-ESTADO-NEGATIVO"
                });
            }
            await finance.SaveChangesAsync();
        }
        if (defecto is "stockInvalido" or "wmsIniciado" or "vendedorIncorrecto")
        {
            await using var catalog = NewCatalog();
            if (defecto == "stockInvalido")
            {
                (await catalog.StockFisico.FirstAsync()).CantidadReservada = 0;
            }
            else if (defecto == "vendedorIncorrecto")
            {
                var producto = await catalog.Productos.FirstAsync();
                producto.VendedorId = Guid.NewGuid();
            }
            else
            {
                catalog.PickLists.Add(new PickList
                {
                    Id = Guid.NewGuid(), OrdenId = ordenId,
                    NumeroOrden = "QA-WMS-NEG-" + ordenId.ToString("N"),
                    NumeroPickList = "QA-PICK-NEG-" + ordenId.ToString("N")
                });
            }
            await catalog.SaveChangesAsync();
        }

        await using (var cierreDb = NewFinance())
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new QaMultiSkuCierreTransaccionalService(cierreDb)
                    .CerrarAsync(intentoId, autorizacionExplicitaQa: true));

        await using var checkFinance = NewFinance();
        var afterIntent = await checkFinance.IntentosPago.AsNoTracking()
            .SingleAsync(x => x.Id == intentoId);
        var afterOrder = await checkFinance.Ordenes.AsNoTracking()
            .SingleAsync(x => x.Id == ordenId);
        Assert.NotEqual(EstadoIntentoPago.Completado, afterIntent.Estado);
        Assert.NotEqual(EstadoOrden.Aprobada, afterOrder.Estado);
        Assert.Equal(defecto == "splitPreexistente" ? 1 : 0,
            await checkFinance.SplitsPago.CountAsync(x => x.OrdenId == ordenId));

        await using var checkCatalog = NewCatalog();
        var afterReservas = await checkCatalog.ReservasStock.AsNoTracking()
            .Where(x => x.OrdenId == ordenId).ToListAsync();
        Assert.Equal(2, afterReservas.Count);
        Assert.All(afterReservas,
            x => Assert.Equal(EstadoReserva.Pendiente, x.Estado));
        Assert.Equal(reservaIds.OrderBy(x => x),
            afterReservas.Select(x => x.Id).OrderBy(x => x));
        Assert.Equal(2, await checkCatalog.MovimientosInventario.CountAsync());
    }

    [Fact]
    public async Task QaCierreMultiSku_NoPuedeUsarBasePostgresPrincipal()
    {
        Assert.NotNull(_rootConnection);
        await using var db = new FinanceDbContext(
            new DbContextOptionsBuilder<FinanceDbContext>()
                .UseNpgsql(_rootConnection).Options);
        var fallo = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new QaMultiSkuCierreTransaccionalService(db)
                .CerrarAsync(Guid.NewGuid(), autorizacionExplicitaQa: true));
        Assert.Contains("PROHIBIDO_FUERA_DE_BASE_EFIMERA_LOCAL", fallo.Message);
    }

    [Theory]
    [InlineData("exito")]
    [InlineData("trasSplit")]
    [InlineData("trasPrimeraReserva")]
    [InlineData("antesDeCommit")]
    [InlineData("concurrente")]
    public async Task QaCierreMultiSku_SplitReservasYOrden_SonAtomicosEIdempotentes(
        string escenario)
    {
        var (ordenId, intentoId, reservaIds) = await PrepararCarritoPagoSimuladoAsync();
        var stub = new Mock<IQaPagoSimulador>(MockBehavior.Strict);
        stub.Setup(s => s.SimularAsync(
            ordenId, 20m, "USD", $"PAY-{ordenId:N}"))
            .ReturnsAsync(new QaRespuestaPagoSimulado(
                true, "QA-SIM-CIERRE-ATOMICO"));

        await using (var pay = NewFinance())
            Assert.Equal("APROBADO_SIMULADO_SIN_CIERRE_COMERCIAL",
                await new QaMultiSkuPagoSimuladoService(pay, stub.Object)
                    .SimularUnaVezAsync(intentoId, autorizacionExplicitaQa: true));
        stub.Verify(s => s.SimularAsync(ordenId, 20m, "USD",
            $"PAY-{ordenId:N}"), Times.Once());

        if (escenario is "trasSplit" or "trasPrimeraReserva" or "antesDeCommit")
        {
            await using var fallo = NewFinance();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new QaMultiSkuCierreTransaccionalService(fallo)
                    .CerrarAsync(intentoId, autorizacionExplicitaQa: true,
                        puntoFalloQa: escenario));

            // Ni split, ni una sola reserva confirmada, ni orden aprobada.
            await using var verifyFailure = NewFinance();
            Assert.Empty(await verifyFailure.SplitsPago.Where(s =>
                s.OrdenId == ordenId).ToListAsync());
            Assert.Equal(EstadoOrden.Pendiente,
                (await verifyFailure.Ordenes.AsNoTracking()
                    .SingleAsync(x => x.Id == ordenId)).Estado);
            Assert.Equal(EstadoIntentoPago.Aprobado,
                (await verifyFailure.IntentosPago.AsNoTracking()
                    .SingleAsync(x => x.Id == intentoId)).Estado);
            await using var catalogFailure = NewCatalog();
            Assert.All(await catalogFailure.ReservasStock.AsNoTracking()
                .Where(x => x.OrdenId == ordenId).ToListAsync(),
                x => Assert.Equal(EstadoReserva.Pendiente, x.Estado));
            Assert.All(await catalogFailure.StockFisico.AsNoTracking()
                .ToListAsync(), x => Assert.Equal(1, x.CantidadReservada));
        }

        if (escenario == "concurrente")
        {
            await using var financeA = NewFinance();
            await using var financeB = NewFinance();
            var taskA = new QaMultiSkuCierreTransaccionalService(financeA)
                .CerrarAsync(intentoId, autorizacionExplicitaQa: true);
            var taskB = new QaMultiSkuCierreTransaccionalService(financeB)
                .CerrarAsync(intentoId, autorizacionExplicitaQa: true);
            var responses = await Task.WhenAll(taskA, taskB);
            Assert.Single(responses, x => x == "FINALIZADA_SIMULADA");
            Assert.Single(responses, x => x == "YA_FINALIZADA");
        }
        else
        {
            await using var cierre = NewFinance();
            Assert.Equal("FINALIZADA_SIMULADA",
                await new QaMultiSkuCierreTransaccionalService(cierre)
                    .CerrarAsync(intentoId, autorizacionExplicitaQa: true));
        }

        // Reanudación desde otro contexto: siempre idempotente, sin recargos.
        await using (var retryDb = NewFinance())
            Assert.Equal("YA_FINALIZADA",
                await new QaMultiSkuCierreTransaccionalService(retryDb)
                    .CerrarAsync(intentoId, autorizacionExplicitaQa: true));
        stub.Verify(s => s.SimularAsync(ordenId, 20m, "USD",
            $"PAY-{ordenId:N}"), Times.Once());
        stub.VerifyNoOtherCalls();

        await using var financeCheck = NewFinance();
        var orden = await financeCheck.Ordenes.AsNoTracking()
            .SingleAsync(x => x.Id == ordenId);
        var intento = await financeCheck.IntentosPago.AsNoTracking()
            .SingleAsync(x => x.Id == intentoId);
        var split = await financeCheck.SplitsPago.AsNoTracking()
            .SingleAsync(x => x.OrdenId == ordenId);
        Assert.Equal(EstadoOrden.Aprobada, orden.Estado);
        Assert.NotNull(orden.FechaAprobacion);
        Assert.Equal(EstadoIntentoPago.Completado, intento.Estado);
        Assert.Equal("QA-SIM-CIERRE-ATOMICO", intento.TransaccionId);
        Assert.Equal("QA-SIM-CIERRE-ATOMICO", split.TransaccionId);
        Assert.Equal(20m, split.MontoTotal);
        Assert.Equal(18m, split.MontoVendedor);
        Assert.Equal(2m, split.MontoMarketplace);
        Assert.Equal(1, await financeCheck.SplitsPago
            .CountAsync(x => x.OrdenId == ordenId));
        Assert.Equal(2, await financeCheck.IntentoReservasPago
            .CountAsync(x => x.IntentoPagoId == intentoId));
        Assert.Empty(await financeCheck.Liquidaciones.ToListAsync());
        Assert.Empty(await financeCheck.ReembolsosPago.ToListAsync());

        await using var catalogCheck = NewCatalog();
        var reservas = await catalogCheck.ReservasStock.AsNoTracking()
            .Where(x => x.OrdenId == ordenId).ToListAsync();
        Assert.Equal(reservaIds.OrderBy(x => x),
            reservas.Select(x => x.Id).OrderBy(x => x));
        Assert.All(reservas, x =>
        {
            Assert.Equal(EstadoReserva.Confirmada, x.Estado);
            Assert.Equal(0, x.CantidadConsumida);
            Assert.Equal(0, x.CantidadLiberada);
        });
        Assert.All(await catalogCheck.StockFisico.AsNoTracking()
            .ToListAsync(), x =>
        {
            Assert.Equal(4, x.CantidadTotal);
            Assert.Equal(1, x.CantidadReservada);
        });
        Assert.Equal(2, await catalogCheck.MovimientosInventario.CountAsync());
    }

    [Theory]
    [InlineData(EstadoIntentoPago.Enviado)]
    [InlineData(EstadoIntentoPago.Incierto)]
    [InlineData(EstadoIntentoPago.Aprobado)]
    [InlineData(EstadoIntentoPago.Completado)]
    public async Task QaRecuperacionPrePago_NoCompensaSiIntentoFueEnviadoOEsIncierto(
        EstadoIntentoPago estado)
    {
        var (ordenId, intentoId, reservaIds) = await PrepararCarritoPagoSimuladoAsync();
        await using (var cambiar = NewFinance())
        {
            var intento = await cambiar.IntentosPago.SingleAsync(i => i.Id == intentoId);
            intento.Estado = estado;
            await cambiar.SaveChangesAsync();
        }

        await using var db = NewFinance();
        var recovery = new QaPrePagoRecoveryService(db, NullLoggerFactory.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            recovery.CancelarSinEnvioAsync(ordenId, autorizacionExplicitaQa: false,
                operadorQaId: OperadorQaAuditado,
                motivo: MotivoCancelacionPreEnvioQa.VerificacionManualQa));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            recovery.CancelarSinEnvioAsync(ordenId, autorizacionExplicitaQa: true,
                operadorQaId: OperadorQaAuditado,
                motivo: MotivoCancelacionPreEnvioQa.VerificacionManualQa));

        await using var financeCheck = NewFinance();
        Assert.Equal(EstadoOrden.Pendiente,
            (await financeCheck.Ordenes.AsNoTracking().SingleAsync()).Estado);
        Assert.Equal(estado,
            (await financeCheck.IntentosPago.AsNoTracking().SingleAsync()).Estado);
        Assert.Empty(await financeCheck.SplitsPago.ToListAsync());
        await using var catalogCheck = NewCatalog();
        Assert.Equal(2, await catalogCheck.ReservasStock
            .CountAsync(x => x.OrdenId == ordenId && x.Estado == EstadoReserva.Pendiente));
        Assert.All(await catalogCheck.StockFisico.AsNoTracking().ToListAsync(),
            x => Assert.Equal(1, x.CantidadReservada));
    }

    [Fact]
    public async Task QaRecuperacionPrePago_ConMapaCorruptoNoLiberaNiCancela()
    {
        var (ordenId, intentoId, _) = await PrepararCarritoPagoSimuladoAsync();
        await using (var corromper = NewFinance())
        {
            var link = await corromper.IntentoReservasPago
                .FirstAsync(x => x.IntentoPagoId == intentoId);
            link.Cantidad += 1;
            await corromper.SaveChangesAsync();
        }

        await using var fin = NewFinance();
        var recovery = new QaPrePagoRecoveryService(fin, NullLoggerFactory.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            recovery.CancelarSinEnvioAsync(ordenId, autorizacionExplicitaQa: true,
                operadorQaId: OperadorQaAuditado,
                motivo: MotivoCancelacionPreEnvioQa.VerificacionManualQa));
        await using var checkFin = NewFinance();
        Assert.Equal(EstadoOrden.Pendiente,
            (await checkFin.Ordenes.AsNoTracking().SingleAsync()).Estado);
        Assert.Equal(EstadoIntentoPago.Preparado,
            (await checkFin.IntentosPago.AsNoTracking().SingleAsync()).Estado);
        await using var checkCat = NewCatalog();
        Assert.Equal(2, await checkCat.ReservasStock
            .CountAsync(x => x.OrdenId == ordenId && x.Estado == EstadoReserva.Pendiente));
        Assert.All(await checkCat.StockFisico.AsNoTracking().ToListAsync(),
            x => Assert.Equal(1, x.CantidadReservada));
    }

    [Theory]
    [InlineData("registroConciliacion")]
    [InlineData("picklistWms")]
    public async Task QaRecuperacionPrePago_BloqueaSiHayTrazasDeConciliacionOWms(
        string caso)
    {
        var (ordenId, intentoId, _) = await PrepararCarritoPagoSimuladoAsync();
        if (caso == "registroConciliacion")
        {
            await using var finance = NewFinance();
            finance.ConciliacionesPago.Add(new ConciliacionPago
            {
                Id = Guid.NewGuid(), OrdenId = ordenId, IntentoPagoId = intentoId,
                Decision = "QA_REVISION_OBLIGATORIA"
            });
            await finance.SaveChangesAsync();
        }
        else
        {
            await using var catalog = NewCatalog();
            catalog.PickLists.Add(new PickList
            {
                Id = Guid.NewGuid(), OrdenId = ordenId,
                NumeroOrden = "QA-WMS-INICIADO",
                NumeroPickList = "QA-PICK-NO-LIBERAR-" + ordenId.ToString("N")
            });
            await catalog.SaveChangesAsync();
        }

        await using var admin = NewFinance();
        var recuperar = new QaPrePagoRecoveryService(
            admin, NullLoggerFactory.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            recuperar.CancelarSinEnvioAsync(
                ordenId, autorizacionExplicitaQa: true,
                operadorQaId: OperadorQaAuditado,
                motivo: MotivoCancelacionPreEnvioQa.VerificacionManualQa));
        await using var fin = NewFinance();
        Assert.Equal(EstadoOrden.Pendiente,
            (await fin.Ordenes.AsNoTracking().SingleAsync()).Estado);
        Assert.Equal(EstadoIntentoPago.Preparado,
            (await fin.IntentosPago.AsNoTracking().SingleAsync()).Estado);
        await using var cat = NewCatalog();
        Assert.Equal(2, await cat.ReservasStock.CountAsync(
            x => x.OrdenId == ordenId && x.Estado == EstadoReserva.Pendiente));
        Assert.All(await cat.StockFisico.AsNoTracking().ToListAsync(),
            x => Assert.Equal(1, x.CantidadReservada));
    }

    [Fact]
    public async Task QaRecuperacionPrePago_DosOperadoresParalelos_NoDuplicanLiberacion()
    {
        var (ordenId, _, _) = await PrepararCarritoPagoSimuladoAsync();

        async Task<bool> CancelarUno()
        {
            await using var ctx = NewFinance();
            try
            {
                var resultado = await new QaPrePagoRecoveryService(
                    ctx, NullLoggerFactory.Instance)
                    .CancelarSinEnvioAsync(ordenId, autorizacionExplicitaQa: true,
                        operadorQaId: OperadorQaAuditado,
                        motivo: MotivoCancelacionPreEnvioQa.VerificacionManualQa);
                return resultado == "CANCELADO_RESERVAS_LIBERADAS";
            }
            catch (InvalidOperationException)
            {
                // El competidor debe ver la orden Cancelada DESPUES del
                // commit protegido por advisory lock y no repetir acciones.
                return false;
            }
        }

        var resultados = await Task.WhenAll(CancelarUno(), CancelarUno());
        Assert.Single(resultados, resultado => resultado);
        await using var finance = NewFinance();
        Assert.Equal(EstadoOrden.Cancelada,
            (await finance.Ordenes.AsNoTracking().SingleAsync()).Estado);
        Assert.Equal(EstadoIntentoPago.CanceladoAntesEnvio,
            (await finance.IntentosPago.AsNoTracking().SingleAsync()).Estado);
        var registroUnico = Assert.Single(
            await finance.CancelacionesPreEnvioQa.AsNoTracking().ToListAsync());
        Assert.Equal(OperadorQaAuditado, registroUnico.OperadorQaId);
        Assert.Equal(2, registroUnico.ReservasLiberadas);
        Assert.Equal(MotivoCancelacionPreEnvioQa.VerificacionManualQa, registroUnico.Motivo);
        Assert.Empty(await finance.SplitsPago.ToListAsync());
        await using var catalog = NewCatalog();
        Assert.Equal(2, await catalog.ReservasStock.CountAsync(
            r => r.OrdenId == ordenId && r.Estado == EstadoReserva.Liberada));
        Assert.All(await catalog.StockFisico.AsNoTracking().ToListAsync(),
            s => Assert.Equal(0, s.CantidadReservada));
        // Dos movimientos iniciales de reserva y SOLO dos liberaciones.
        Assert.Equal(4, await catalog.MovimientosInventario.CountAsync());
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task QaRecuperacionPrePago_NoAceptaActorOMotivoSinAuditar(
        bool actorValido, bool motivoValido)
    {
        var (ordenId, _, _) = await PrepararCarritoPagoSimuladoAsync();
        await using var db = NewFinance();
        var recovery = new QaPrePagoRecoveryService(db, NullLoggerFactory.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            recovery.CancelarSinEnvioAsync(
                ordenId, autorizacionExplicitaQa: true,
                operadorQaId: actorValido ? OperadorQaAuditado : Guid.Empty,
                motivo: motivoValido
                    ? MotivoCancelacionPreEnvioQa.VerificacionManualQa
                    : (MotivoCancelacionPreEnvioQa)999));

        await using var fin = NewFinance();
        Assert.Equal(EstadoOrden.Pendiente,
            (await fin.Ordenes.AsNoTracking().SingleAsync()).Estado);
        Assert.Equal(EstadoIntentoPago.Preparado,
            (await fin.IntentosPago.AsNoTracking().SingleAsync()).Estado);
        Assert.Empty(await fin.CancelacionesPreEnvioQa.ToListAsync());
        await using var cat = NewCatalog();
        Assert.Equal(2, await cat.ReservasStock.CountAsync(
            x => x.OrdenId == ordenId && x.Estado == EstadoReserva.Pendiente));
        Assert.All(await cat.StockFisico.AsNoTracking().ToListAsync(),
            x => Assert.Equal(1, x.CantidadReservada));
    }

    [Theory]
    [InlineData("antesDeSimular")]
    [InlineData("timeout")]
    [InlineData("rechazado")]
    [InlineData("sinReferencia")]
    [InlineData("despuesDeSimular")]
    [InlineData("concurrente")]
    public async Task QaPagoMultiSku_EmisionUnicaYEstadosDurablesAnteFallo(
        string modo)
    {
        var (ordenId, intentoId, reservaIds) = await PrepararCarritoPagoSimuladoAsync();
        var stub = new Mock<IQaPagoSimulador>(MockBehavior.Strict);
        if (modo != "antesDeSimular")
        {
            if (modo == "timeout")
                stub.Setup(s => s.SimularAsync(ordenId, 20m, "USD", $"PAY-{ordenId:N}"))
                    .ThrowsAsync(new IOException("Timeout del SIMULADOR QA"));
            else
                stub.Setup(s => s.SimularAsync(ordenId, 20m, "USD", $"PAY-{ordenId:N}"))
                    .ReturnsAsync(modo switch
                    {
                        "rechazado" => new QaRespuestaPagoSimulado(false, null),
                        "sinReferencia" => new QaRespuestaPagoSimulado(true, null),
                        _ => new QaRespuestaPagoSimulado(true, "QA-SIM-MULTI-FALLO")
                    });
        }

        await using var dbA = NewFinance();
        var procesador = new QaMultiSkuPagoSimuladoService(dbA, stub.Object);
        EstadoIntentoPago esperado;
        if (modo == "concurrente")
        {
            await using var dbB = NewFinance();
            var t1 = procesador.SimularUnaVezAsync(intentoId, true);
            var t2 = new QaMultiSkuPagoSimuladoService(dbB, stub.Object)
                .SimularUnaVezAsync(intentoId, true);
            var respuestas = await Task.WhenAll(t1, t2);
            Assert.Single(respuestas,
                x => x == "APROBADO_SIMULADO_SIN_CIERRE_COMERCIAL");
            Assert.Single(respuestas,
                x => x == "YA_EXISTE_INTENTO_NO_REEMITIR");
            esperado = EstadoIntentoPago.Aprobado;
        }
        else if (modo == "antesDeSimular")
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => procesador.SimularUnaVezAsync(intentoId, true,
                    falloDespuesDeMarcarEnviadoQa: true));
            esperado = EstadoIntentoPago.Enviado;
        }
        else if (modo == "despuesDeSimular")
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => procesador.SimularUnaVezAsync(intentoId, true,
                    falloDespuesDeRespuestaQa: true));
            esperado = EstadoIntentoPago.Enviado;
        }
        else
        {
            var resultado = await procesador.SimularUnaVezAsync(intentoId, true);
            Assert.Equal(modo switch
            {
                "timeout" => "INCIERTO_NO_REEMITIR",
                "rechazado" => "RECHAZADO_SIMULADO_RESERVAS_AUN_PENDIENTES",
                _ => "INCIERTO_SIN_REFERENCIA"
            }, resultado);
            esperado = modo == "rechazado"
                ? EstadoIntentoPago.Rechazado
                : EstadoIntentoPago.Incierto;
        }

        // Ningún reintento vuelve a invocar un simulador tras Enviado,
        // aunque no haya referencia ni resultado comprobable.
        Assert.Equal("YA_EXISTE_INTENTO_NO_REEMITIR",
            await procesador.SimularUnaVezAsync(intentoId, true));
        stub.Verify(s => s.SimularAsync(
                ordenId, 20m, "USD", $"PAY-{ordenId:N}"),
            modo == "antesDeSimular" ? Times.Never() : Times.Once());
        stub.VerifyNoOtherCalls();

        await using var verificarFinance = NewFinance();
        var intento = await verificarFinance.IntentosPago.AsNoTracking()
            .SingleAsync(x => x.Id == intentoId);
        Assert.Equal(esperado, intento.Estado);
        Assert.Equal(2, await verificarFinance.IntentoReservasPago.CountAsync(
            x => x.IntentoPagoId == intentoId));
        Assert.Equal(EstadoOrden.Pendiente,
            (await verificarFinance.Ordenes.AsNoTracking()
                .SingleAsync(x => x.Id == ordenId)).Estado);
        Assert.Empty(await verificarFinance.SplitsPago.ToListAsync());
        Assert.Empty(await verificarFinance.ReembolsosPago.ToListAsync());
        if (modo == "concurrente")
            Assert.Equal("QA-SIM-MULTI-FALLO", intento.TransaccionId);
        else
            Assert.Null(intento.TransaccionId);

        await using var verificarCatalog = NewCatalog();
        var reservas = await verificarCatalog.ReservasStock.AsNoTracking()
            .Where(x => x.OrdenId == ordenId).ToListAsync();
        Assert.Equal(2, reservas.Count);
        Assert.Equal(reservaIds.OrderBy(x => x),
            reservas.Select(x => x.Id).OrderBy(x => x));
        Assert.All(reservas, x => Assert.Equal(EstadoReserva.Pendiente, x.Estado));
        Assert.All(await verificarCatalog.StockFisico.AsNoTracking()
            .ToListAsync(), x => Assert.Equal(1, x.CantidadReservada));
    }

    private async Task<(Guid OrdenId, Guid IntentoId, List<Guid> ReservaIds)>
        PrepararCarritoPagoSimuladoAsync()
    {
        if (!_created) throw new InvalidOperationException("BD de prueba no iniciada");
        var orderId = Guid.NewGuid();
        var intentoId = Guid.NewGuid();
        var vendedor = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var location = Guid.NewGuid();
        await using (var catalog = NewCatalog())
        {
            foreach (var productoId in new[] { a, b })
            {
                catalog.Productos.Add(new Producto
                {
                    Id = productoId, VendedorId = vendedor,
                    CategoriaId = Guid.NewGuid(),
                    Sku = "QA-STUB-" + productoId.ToString("N")[..16],
                    Nombre = "SKU de ensayo", CategoriaNombre = "QA",
                    PrecioB2C = 10m, PrecioB2B = 9m, Moneda = "USD",
                    EstaPublicado = true, EstadoProducto = "Activo",
                    StockInicial = 0, StockMinimo = 1,
                    ProveedorNombre = "Proveedor QA",
                    PaisOrigen = "El Salvador", PesoKg = 1m,
                    DimensionesCm = "10x10x10", UnidadMedida = "Unidad"
                });
            }
            catalog.UbicacionesAlmacen.Add(new UbicacionAlmacen
            {
                Id = location, Codigo = "QA-STUB-P-1-R-1-N-1-P-1",
                Pasillo = "QA", Rack = "1", Nivel = 1, Posicion = "1",
                EstaOcupada = true
            });
            foreach (var productoId in new[] { a, b })
                catalog.StockFisico.Add(new StockFisico
                {
                    Id = Guid.NewGuid(), ProductoId = productoId,
                    UbicacionAlmacenId = location,
                    UbicacionCodigo = "QA-STUB-P-1-R-1-N-1-P-1",
                    CantidadTotal = 4
                });
            await catalog.SaveChangesAsync();
        }

        List<QaVinculoReserva> links;
        await using (var catalog = NewCatalog())
        {
            var reserva = await new ReservaCarritoAtomicaQaService(
                catalog, NullLoggerFactory.Instance).ReservarAsync(
                orderId, new List<ItemReservaCarritoQa> {
                    new(a, 1), new(b, 1)
                }, autorizacionExplicitaQa: true);
            Assert.Equal(2, reserva.ReservaIds.Count);
            links = (await catalog.ReservasStock.AsNoTracking()
                .Where(x => x.OrdenId == orderId)
                .ToListAsync())
                .Select(x => new QaVinculoReserva(
                    x.Id, x.ProductoId, x.CantidadOriginal)).ToList();
        }

        await using (var finance = NewFinance())
        {
            finance.Ordenes.Add(new Orden
            {
                Id = orderId, NumeroOrden = "QA-STUB-" + orderId.ToString("N"),
                CompradorId = Guid.NewGuid(), VendedorId = vendedor,
                IdempotencyKey = "QA-MULTISKU-STUB-20261108",
                CheckoutPayloadHash = new string('B', 64),
                SubTotal = 20m, MontoTotal = 20m,
                Moneda = "USD", Estado = EstadoOrden.Pendiente,
                Items = new List<ItemOrden>
                {
                    new() { Id = Guid.NewGuid(), OrdenId = orderId,
                        ProductoId = a, SkuReferencia = "QA-A",
                        NombreProducto = "A", Cantidad = 1, PrecioUnitario = 10m },
                    new() { Id = Guid.NewGuid(), OrdenId = orderId,
                        ProductoId = b, SkuReferencia = "QA-B",
                        NombreProducto = "B", Cantidad = 1, PrecioUnitario = 10m }
                }
            });
            finance.IntentosPago.Add(new IntentoPago
            {
                Id = intentoId, OrdenId = orderId, ReservaId = null,
                ReferenciaOperacion = $"PAY-{orderId:N}",
                Monto = 20m, Moneda = "USD", Estado = EstadoIntentoPago.Preparado
            });
            await finance.SaveChangesAsync();
        }
        await using (var finance = NewFinance())
            Assert.Equal("REGISTRADO",
                await new QaIntentoReservasMapService(finance).RegistrarAsync(
                    intentoId, links, autorizacionExplicitaQa: true));

        return (orderId, intentoId, links.Select(x => x.ReservaId).ToList());
    }

    [Fact]
    public async Task QaMigracionMultiSku_BackfillSoloOrdenInequivocaYPreservaLegacy()
    {
        // BD adicional efímera para probar ACTUALIZACIÓN desde el esquema antiguo.
        // El fixture ya validó que _rootConnection es localhost:5432/postgres.
        Assert.NotNull(_rootConnection);
        var nombre = "ci_checkout_http_legacy_" + Guid.NewGuid().ToString("N");
        var descriptor = new NpgsqlConnectionStringBuilder(_rootConnection)
        {
            Database = nombre
        };
        var conexionLegacy = descriptor.ConnectionString;
        await using (var administrador = new NpgsqlConnection(_rootConnection))
        {
            await administrador.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "CREATE DATABASE \"" + nombre + "\"", administrador);
            await cmd.ExecuteNonQueryAsync();
        }

        try
        {
            await using var finance = new FinanceDbContext(
                new DbContextOptionsBuilder<FinanceDbContext>()
                    .UseNpgsql(conexionLegacy, x =>
                        x.MigrationsHistoryTable("__EFMigrationsHistory", "finance"))
                    .Options);
            var migrador = finance.Database.GetService<IMigrator>();
            await migrador.MigrateAsync("20261107022000_FIN07DetalleFacturaProveedor");

            var singleOrderId = Guid.NewGuid();
            var ambigOrderId = Guid.NewGuid();
            var singleAttemptId = Guid.NewGuid();
            var ambiguousAttemptId = Guid.NewGuid();
            var singleReservaId = Guid.NewGuid();
            var ambigReservaId = Guid.NewGuid();
            var singleSkuId = Guid.NewGuid();
            var ambigSkuA = Guid.NewGuid();
            var ambigSkuB = Guid.NewGuid();
            var seller = Guid.NewGuid();
            var buyer = Guid.NewGuid();
            finance.Ordenes.Add(new Orden
            {
                Id = singleOrderId, NumeroOrden = "LEG-ONE-" + singleOrderId.ToString("N"),
                CompradorId = buyer, VendedorId = seller,
                Estado = EstadoOrden.Aprobada, SubTotal = 25m,
                MontoTotal = 25m, Moneda = "USD",
                Items = new List<ItemOrden> {
                    new() { Id = Guid.NewGuid(), OrdenId = singleOrderId,
                        ProductoId = singleSkuId, Cantidad = 2,
                        PrecioUnitario = 12.5m }
                }
            });
            finance.Ordenes.Add(new Orden
            {
                Id = ambigOrderId, NumeroOrden = "LEG-MULTI-" + ambigOrderId.ToString("N"),
                CompradorId = buyer, VendedorId = seller,
                Estado = EstadoOrden.Pendiente, SubTotal = 50m,
                MontoTotal = 50m, Moneda = "USD",
                Items = new List<ItemOrden> {
                    new() { Id = Guid.NewGuid(), OrdenId = ambigOrderId,
                        ProductoId = ambigSkuA, Cantidad = 2,
                        PrecioUnitario = 10m },
                    new() { Id = Guid.NewGuid(), OrdenId = ambigOrderId,
                        ProductoId = ambigSkuB, Cantidad = 3,
                        PrecioUnitario = 10m }
                }
            });
            finance.IntentosPago.Add(new IntentoPago
            {
                Id = singleAttemptId, OrdenId = singleOrderId,
                ReservaId = singleReservaId,
                ReferenciaOperacion = $"PAY-{singleOrderId:N}",
                Monto = 25m, Moneda = "USD", Estado = EstadoIntentoPago.Completado
            });
            finance.IntentosPago.Add(new IntentoPago
            {
                Id = ambiguousAttemptId, OrdenId = ambigOrderId,
                ReservaId = ambigReservaId,
                ReferenciaOperacion = $"PAY-{ambigOrderId:N}",
                Monto = 50m, Moneda = "USD", Estado = EstadoIntentoPago.RequiereConciliacion
            });
            await finance.SaveChangesAsync();

            await migrador.MigrateAsync();
            finance.ChangeTracker.Clear();

            var legado = await finance.IntentoReservasPago.AsNoTracking()
                .SingleAsync(r => r.IntentoPagoId == singleAttemptId);
            Assert.Equal(singleReservaId, legado.ReservaId);
            Assert.Equal(singleSkuId, legado.ProductoId);
            Assert.Equal(2, legado.Cantidad);
            Assert.Equal("LegacySinVerificar", legado.Procedencia);
            Assert.Empty(await finance.IntentoReservasPago.AsNoTracking()
                .Where(r => r.IntentoPagoId == ambiguousAttemptId).ToListAsync());
            Assert.Equal(singleReservaId,
                (await finance.IntentosPago.AsNoTracking()
                    .SingleAsync(i => i.Id == singleAttemptId)).ReservaId);
            Assert.Equal(ambigReservaId,
                (await finance.IntentosPago.AsNoTracking()
                    .SingleAsync(i => i.Id == ambiguousAttemptId)).ReservaId);
            // Reaplicar la migración es idempotente (0 nuevos registros).
            await migrador.MigrateAsync();
            Assert.Single(await finance.IntentoReservasPago.AsNoTracking().ToListAsync());

            // La BD impide reutilizar la misma reserva física dos veces.
            finance.IntentoReservasPago.Add(new IntentoReservaPago
            {
                Id = Guid.NewGuid(), IntentoPagoId = singleAttemptId,
                ReservaId = singleReservaId, ProductoId = Guid.NewGuid(),
                Cantidad = 1, Procedencia = "MultiSkuQa"
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => finance.SaveChangesAsync());
            finance.ChangeTracker.Clear();
            Assert.Single(await finance.IntentoReservasPago.AsNoTracking().ToListAsync());

            // El CHECK de cantidad impide vínculos con cantidad 0.
            finance.IntentoReservasPago.Add(new IntentoReservaPago
            {
                Id = Guid.NewGuid(), IntentoPagoId = singleAttemptId,
                ReservaId = Guid.NewGuid(), ProductoId = Guid.NewGuid(),
                Cantidad = 0, Procedencia = "MultiSkuQa"
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => finance.SaveChangesAsync());
            finance.ChangeTracker.Clear();
            Assert.Single(await finance.IntentoReservasPago.AsNoTracking().ToListAsync());
        }
        finally
        {
            // Descarta SOLO esta BD de nombre generado por el propio test.
            await using var admin = new NpgsqlConnection(_rootConnection);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand(
                "DROP DATABASE IF EXISTS \"" + nombre + "\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task QaSnapshotFinanciero_NoGeneraMigracionInesperada()
    {
        await using var finance = NewFinance();
        Assert.False(finance.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task QaMultiSku_RechazaBasePostgresPrincipalSinEjecutarSql()
    {
        Assert.NotNull(_rootConnection);
        await using var db = new CatalogDbContext(
            new DbContextOptionsBuilder<CatalogDbContext>()
                .UseNpgsql(_rootConnection).Options);
        var carrito = new ReservaCarritoAtomicaQaService(db, NullLoggerFactory.Instance);
        var fallo = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            carrito.ReservarAsync(Guid.NewGuid(),
                new List<ItemReservaCarritoQa>
                {
                    new(Guid.NewGuid(), 1), new(Guid.NewGuid(), 1)
                }, autorizacionExplicitaQa: true));
        Assert.Contains("PROHIBIDO_FUERA_DE_BASE_EFIMERA_LOCAL", fallo.Message);
    }

    [Fact]
    public async Task CierreQa_RechazaBasePostgresPrincipalSinEjecutarSql()
    {
        // Se construye un DbContext apuntando a postgres@localhost, pero
        // el servicio valida la conexión ANTES de abrirla o ejecutar SQL.
        Assert.NotNull(_rootConnection);
        await using var db = new FinanceDbContext(
            new DbContextOptionsBuilder<FinanceDbContext>()
                .UseNpgsql(_rootConnection).Options);
        var servicio = new CierreCheckoutAdminQaService(db);
        var fallo = await Assert.ThrowsAsync<InvalidOperationException>(
            () => servicio.CerrarAsync(Guid.NewGuid(), autorizacionExplicitaQa: true));
        Assert.Contains("PROHIBIDO", fallo.Message);
    }

    private sealed class FailOrderFinalizeInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<Orden>()
                .Any(x => x.State == EntityState.Modified &&
                    x.Entity.Estado == EstadoOrden.Aprobada) == true)
                throw new InvalidOperationException("Fallo cierre de orden SIMULADO tras cobro");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class FailAttemptInsertInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<IntentoPago>()
                .Any(x => x.State == EntityState.Added) == true)
                throw new InvalidOperationException("Fallo de INSERT intento SIMULADO antes de pago");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    public async Task DisposeAsync()
    {
        if (!_created || _rootConnection is null ||
            !_database.StartsWith("ci_checkout_http_", StringComparison.Ordinal))
            return;
        if (_testConnection is not null)
        {
            using var pool = new NpgsqlConnection(_testConnection);
            NpgsqlConnection.ClearPool(pool);
        }
        await using var admin = new NpgsqlConnection(_rootConnection);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand(
            "DROP DATABASE IF EXISTS \"" + _database + "\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
        _created = false;
    }
}
