/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Net;
using System.Net.Http.Json;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Core.Entities;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace MarketplaceERP.API.Tests;

/// <summary>
/// Tests de integración de la Fase 2 / 2b:
/// - Login correcto contra BD por Email
/// - Login incorrecto retorna 401 genérico
/// - Rate limiting (429 al 6to intento)
/// - Startup en Production con Key placeholder lanza excepción
/// - Vendedor A no ve orden de Vendedor B (404), Admin sí ve la orden (200)
/// - CORS en Production sin orígenes configurados bloquea Origin
/// - Login solo por Email rechaza coincidencia por Nombre
/// - CRUD POST /api/usuarios (401 sin token, 403 Vendedor/Comprador, 201 Admin, 409 duplicado, login exitoso)
/// </summary>
public class Fase2AuthenticationAndSecurityTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public Fase2AuthenticationAndSecurityTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_CredencialesCorrectas_Retorna200YTokenConClaims()
    {
        var email = $"vendedor_{Guid.NewGuid():N}@test.com";
        var password = "PasswordSeguro123!";
        var vendedorId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            var hasher = new PasswordHasher<Usuario>();
            var usuario = new Usuario
            {
                Id = Guid.NewGuid(),
                Email = email,
                Nombre = "Vendedor Test",
                Rol = "Vendedor",
                Activo = true,
                VendedorId = vendedorId
            };
            usuario.PasswordHash = hasher.HashPassword(usuario, password);
            db.Usuarios.Add(usuario);
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = email,
            Password = password
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        Assert.NotEmpty(body.Token);
        Assert.Equal("Vendedor", body.Role);
    }

    [Fact]
    public async Task Login_SoloPorEmail_RechazaCoincidenciaPorNombre()
    {
        var email = $"login_email_{Guid.NewGuid():N}@test.com";
        var nombreUnico = $"NombreExclusivo_{Guid.NewGuid():N}";
        var password = "PasswordSeguro123!";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            var hasher = new PasswordHasher<Usuario>();
            var usuario = new Usuario
            {
                Id = Guid.NewGuid(),
                Email = email,
                Nombre = nombreUnico,
                Rol = "Comprador",
                Activo = true
            };
            usuario.PasswordHash = hasher.HashPassword(usuario, password);
            db.Usuarios.Add(usuario);
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();

        // Intento de login usando Nombre en lugar de Email debe fallar con 401
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = nombreUnico,
            Password = password
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_CredencialesIncorrectas_Retorna401Generico()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = "usuario_inexistente@test.com",
            Password = "PasswordErroneo999!"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(body);
        Assert.True(body.ContainsKey("mensaje"));
        Assert.Equal("Credenciales inválidas.", body["mensaje"]);
    }

    [Fact]
    public async Task Login_MasDeCincoIntentosPorMinuto_Retorna429()
    {
        var client = _factory.CreateClient();
        HttpResponseMessage? ultimoResponse = null;

        for (int i = 0; i < 6; i++)
        {
            ultimoResponse = await client.PostAsJsonAsync("/api/auth/login", new
            {
                Email = $"rate_limit_{Guid.NewGuid():N}@test.com",
                Password = "pwd"
            });
        }

        Assert.NotNull(ultimoResponse);
        Assert.Equal(HttpStatusCode.TooManyRequests, ultimoResponse.StatusCode);
    }

    [Fact]
    public void Startup_EnProductionConKeyPlaceholder_LanzaExcepcion()
    {
        var factoryProd = new CustomWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Jwt:Key", "CAMBIA_ESTA_CLAVE_SECRETA_EN_PRODUCCION_MINIMO_32_CHARS!!");
                builder.UseSetting("ADMIN_EMAIL", "admin@prod.com");
                builder.UseSetting("ADMIN_PASSWORD", "SuperPassword123!");
            });

        Assert.Throws<InvalidOperationException>(() =>
        {
            // Forzar arranque del host
            _ = factoryProd.CreateClient();
        });
    }

    [Fact]
    public async Task Cors_EnProductionSinOrigenesConfigurados_NoRetornaHeaderAllowOrigin()
    {
        var factoryProd = new CustomWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Jwt:Key", "ESTA_ES_UNA_CLAVE_VALIDA_DE_PRODUCCION_SIN_PLACEHOLDERS_64CHARS!!");
                builder.UseSetting("ADMIN_EMAIL", "admin@prod.com");
                builder.UseSetting("ADMIN_PASSWORD", "SuperPassword123!");
                builder.UseSetting("Cors:AllowedOrigins:0", ""); // Vacío
            });

        var client = factoryProd.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", "http://malicious-site.com");

        var response = await client.SendAsync(request);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task VendedorA_NoVeOrdenDeVendedorB_Retorna404_AdminVeOrden_Retorna200()
    {
        var vendedorAId = Guid.NewGuid();
        var vendedorBId = Guid.NewGuid();
        var ordenBId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var financeDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var orden = new Orden
            {
                Id = ordenBId,
                NumeroOrden = $"ORD-{Guid.NewGuid():N}",
                CompradorId = Guid.NewGuid(),
                VendedorId = vendedorBId,
                MontoTotal = 150m,
                Moneda = "USD",
                Estado = EstadoOrden.Pendiente,
                FechaCreacion = DateTime.UtcNow
            };
            financeDb.Ordenes.Add(orden);
            await financeDb.SaveChangesAsync();
        }

        // Vendedor A intenta consultar la orden de Vendedor B
        var clientVendedorA = _factory.CreateClientWithAuth("Vendedor", vendedorId: vendedorAId.ToString());
        var resVendedorA = await clientVendedorA.GetAsync($"/api/ordenes/{ordenBId}");
        Assert.Equal(HttpStatusCode.NotFound, resVendedorA.StatusCode);

        // Vendedor B consulta su propia orden
        var clientVendedorB = _factory.CreateClientWithAuth("Vendedor", vendedorId: vendedorBId.ToString());
        var resVendedorB = await clientVendedorB.GetAsync($"/api/ordenes/{ordenBId}");
        Assert.Equal(HttpStatusCode.OK, resVendedorB.StatusCode);

        // Admin consulta la orden de Vendedor B
        var clientAdmin = _factory.CreateClientWithAuth("Admin");
        var resAdmin = await clientAdmin.GetAsync($"/api/ordenes/{ordenBId}");
        Assert.Equal(HttpStatusCode.OK, resAdmin.StatusCode);
    }

    [Fact]
    public async Task Usuarios_Crear_SinToken_Retorna401()
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/usuarios", new
        {
            Email = "alguno@test.com",
            Password = "PasswordValido123!",
            Rol = "Comprador",
            Nombre = "Sin Auth"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Usuarios_Crear_ConRolVendedorOComprador_Retorna403()
    {
        var clientVendedor = _factory.CreateClientWithAuth("Vendedor");
        var resVendedor = await clientVendedor.PostAsJsonAsync("/api/usuarios", new
        {
            Email = "nuevo@test.com",
            Password = "PasswordValido123!",
            Rol = "Comprador",
            Nombre = "Intento Vendedor"
        });
        Assert.Equal(HttpStatusCode.Forbidden, resVendedor.StatusCode);

        var clientComprador = _factory.CreateClientWithAuth("Comprador");
        var resComprador = await clientComprador.PostAsJsonAsync("/api/usuarios", new
        {
            Email = "nuevo@test.com",
            Password = "PasswordValido123!",
            Rol = "Comprador",
            Nombre = "Intento Comprador"
        });
        Assert.Equal(HttpStatusCode.Forbidden, resComprador.StatusCode);
    }

    [Fact]
    public async Task Usuarios_Crear_ConAdmin_Retorna201_DuplicadoRetorna409_YPermiteLogin()
    {
        var email = $"nuevo_usuario_{Guid.NewGuid():N}@test.com";
        var password = "PasswordValido1234!";
        var clientAdmin = _factory.CreateClientWithAuth("Admin");

        // 1. Crear exitosamente con rol Admin -> 201
        var req = new CrearUsuarioRequest(
            Email: email,
            Password: password,
            Rol: "Comprador",
            Nombre: "Usuario Creado Por Admin"
        );
        var resCrear = await clientAdmin.PostAsJsonAsync("/api/usuarios", req);
        Assert.Equal(HttpStatusCode.Created, resCrear.StatusCode);

        var body = await resCrear.Content.ReadFromJsonAsync<UsuarioResponse>();
        Assert.NotNull(body);
        Assert.Equal(email.ToLower(), body.Email);
        Assert.Equal("Comprador", body.Rol);

        // 2. Email duplicado -> 409 Conflict
        var resDuplicado = await clientAdmin.PostAsJsonAsync("/api/usuarios", req);
        Assert.Equal(HttpStatusCode.Conflict, resDuplicado.StatusCode);

        // 3. Login exitoso con el usuario creado
        var clientAnon = _factory.CreateClient();
        var resLogin = await clientAnon.PostAsJsonAsync("/api/auth/login", new
        {
            Email = email,
            Password = password
        });
        Assert.Equal(HttpStatusCode.OK, resLogin.StatusCode);
        var loginBody = await resLogin.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(loginBody);
        Assert.NotEmpty(loginBody.Token);
    }
}
