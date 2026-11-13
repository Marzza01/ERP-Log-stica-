using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.API.Security;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Core.Entities;
using MarketplaceERP.Shared.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public class UsuariosRolesCanonicosTests
{
    private const string Password = "ClavePruebasSegura_2026!";
    private static CoreDbContext Db() => new(
        new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase("roles-canonicos-" + Guid.NewGuid()).Options);

    private static AuthController Auth(CoreDbContext db) => new(
        db,
        Options.Create(new JwtSettings
        {
            Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
            Issuer = "roles-tests.invalid",
            Audience = "roles-client.invalid",
            ExpirationMinutes = 30
        }),
        NullLogger<AuthController>.Instance);

    [Theory]
    [InlineData("admin", "Admin")]
    [InlineData(" ADMIN ", "Admin")]
    [InlineData("vendedor", "Vendedor")]
    [InlineData(" VENDEDOR ", "Vendedor")]
    [InlineData("COMPRADOR", "Comprador")]
    [InlineData(" Comprador ", "Comprador")]
    public async Task CrearUsuarioYLogin_CanonizanRolesReconocidos(string rolEnviado, string rolCanonico)
    {
        await using var db = Db();
        var sellerId = Guid.NewGuid();
        if (rolCanonico == "Vendedor")
        {
            db.Vendedores.Add(new Vendedor
            {
                Id = sellerId, NombreEmpresa = "Vendedor CI", Email = "seller@ci.invalid"
            });
            await db.SaveChangesAsync();
        }

        var controller = new UsuariosController(db, NullLogger<UsuariosController>.Instance);
        var email = $"usuario-{Guid.NewGuid():N}@ci.invalid";
        var created = await controller.Crear(
            new CrearUsuarioRequest(email, Password, rolEnviado, "Cuenta de ensayo",
                rolCanonico == "Vendedor" ? sellerId : null),
            CancellationToken.None);
        var createdResult = Assert.IsType<ObjectResult>(created);
        Assert.Equal(201, createdResult.StatusCode);
        var entity = Assert.Single(db.Usuarios);
        Assert.Equal(rolCanonico, entity.Rol);
        Assert.Equal(rolCanonico == "Vendedor" ? sellerId : (Guid?)null, entity.VendedorId);

        if (rolCanonico == "Comprador")
        {
            var comprador = Assert.Single(db.Compradores);
            Assert.Equal(entity.Id, comprador.Id);
            Assert.Equal(entity.Email, comprador.Email);
            Assert.Equal(entity.Nombre, comprador.NombreCompleto);
            Assert.True(comprador.EstaActivo);
        }
        else
        {
            Assert.Empty(db.Compradores);
        }

        var authResult = await Auth(db).Login(
            new LoginRequest(email, Password), CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(authResult);
        var login = Assert.IsType<LoginResponse>(ok.Value);
        Assert.Equal(rolCanonico, login.Role);
        Assert.Equal(rolCanonico, login.Rol);

        var claims = new JwtSecurityTokenHandler().ReadJwtToken(login.Token).Claims;
        Assert.Contains(claims, claim =>
            claim.Type == ClaimTypes.Role && claim.Value == rolCanonico);
        if (rolCanonico == "Comprador")
            Assert.Contains(claims, claim =>
                claim.Type == ClaimTypes.NameIdentifier && claim.Value == entity.Id.ToString());
        if (rolCanonico == "Vendedor")
            Assert.Contains(claims, claim =>
                claim.Type == "VendedorId" && claim.Value == sellerId.ToString());
        else
            Assert.DoesNotContain(claims, claim => claim.Type == "VendedorId");
    }

    [Fact]
    public async Task CrearUsuarioComprador_ReutilizaPerfilActivoPorEmail()
    {
        await using var db = Db();
        var buyerId = Guid.NewGuid();
        var email = $"perfil-{Guid.NewGuid():N}@ci.invalid";
        db.Compradores.Add(new Comprador
        {
            Id = buyerId, Email = email, NombreCompleto = "Comprador existente",
            DireccionEnvio = "Dirección de prueba", EstaActivo = true
        });
        await db.SaveChangesAsync();

        var controller = new UsuariosController(db, NullLogger<UsuariosController>.Instance);
        var response = await controller.Crear(
            new CrearUsuarioRequest(email, Password, "Comprador", "Cuenta nueva"),
            CancellationToken.None);

        var created = Assert.IsType<ObjectResult>(response);
        Assert.Equal(201, created.StatusCode);
        var user = Assert.Single(db.Usuarios);
        var profile = Assert.Single(db.Compradores);
        Assert.Equal(buyerId, user.Id);
        Assert.Equal(buyerId, profile.Id);
        Assert.Equal("Comprador existente", profile.NombreCompleto);
        Assert.Equal("Dirección de prueba", profile.DireccionEnvio);
    }

    [Fact]
    public async Task CrearUsuarioComprador_PerfilInactivoNoSeReactivaAutomaticamente()
    {
        await using var db = Db();
        db.Compradores.Add(new Comprador
        {
            Id = Guid.NewGuid(), Email = "inactivo@ci.invalid",
            NombreCompleto = "Perfil inactivo", EstaActivo = false
        });
        await db.SaveChangesAsync();

        var controller = new UsuariosController(db, NullLogger<UsuariosController>.Instance);
        var response = await controller.Crear(
            new CrearUsuarioRequest("inactivo@ci.invalid", Password, "Comprador", "Cuenta"),
            CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(response);
        Assert.Empty(db.Usuarios);
        Assert.False(Assert.Single(db.Compradores).EstaActivo);
    }

    [Fact]
    public async Task CrearUsuarioVendedor_NoPermiteVincularVendedorInactivo()
    {
        await using var db = Db();
        var sellerId = Guid.NewGuid();
        db.Vendedores.Add(new Vendedor
        {
            Id = sellerId, NombreEmpresa = "Vendedor inactivo",
            Email = "seller-inactivo@ci.invalid", EstaActivo = false
        });
        await db.SaveChangesAsync();

        var controller = new UsuariosController(db, NullLogger<UsuariosController>.Instance);
        var response = await controller.Crear(
            new CrearUsuarioRequest("seller-user@ci.invalid", Password, "Vendedor", "Cuenta",
                sellerId),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(response);
        Assert.Empty(db.Usuarios);
    }

    [Fact]
    public async Task LoginDeUsuarioHistoricoConRolMinusculo_EmiteRolCanonico()
    {
        await using var db = Db();
        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "historico@ci.invalid",
            Nombre = "Usuario histórico",
            Rol = "vendedor",
            VendedorId = Guid.NewGuid(),
            Activo = true
        };
        usuario.PasswordHash = new PasswordHasher<Usuario>().HashPassword(usuario, Password);
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();

        var result = await Auth(db).Login(
            new LoginRequest(usuario.Email, Password), CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var login = Assert.IsType<LoginResponse>(ok.Value);
        Assert.Equal("Vendedor", login.Role);
        var claims = new JwtSecurityTokenHandler().ReadJwtToken(login.Token).Claims;
        Assert.Contains(claims, c => c.Type == ClaimTypes.Role && c.Value == "Vendedor");
        Assert.Contains(claims, c => c.Type == "VendedorId" &&
                                     c.Value == usuario.VendedorId!.Value.ToString());
    }

    [Fact]
    public async Task LoginConRolDesconocido_DenegadoSinEmitirJwt()
    {
        await using var db = Db();
        var usuario = new Usuario
        {
            Id = Guid.NewGuid(), Email = "rol-desconocido@ci.invalid",
            Nombre = "Cuenta insegura", Rol = "SuperAdmin", Activo = true
        };
        usuario.PasswordHash = new PasswordHasher<Usuario>().HashPassword(usuario, Password);
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();

        Assert.IsType<UnauthorizedObjectResult>(
            await Auth(db).Login(new LoginRequest(usuario.Email, Password),
                CancellationToken.None));
    }

    [Theory]
    [InlineData("Root")]
    [InlineData("Admin,Vendedor")]
    [InlineData(" ")]
    public async Task CrearUsuario_RolDesconocidoNoPersiste(string rol)
    {
        await using var db = Db();
        var controller = new UsuariosController(db, NullLogger<UsuariosController>.Instance);
        var response = await controller.Crear(
            new CrearUsuarioRequest("no@ci.invalid", Password, rol, "No creado"),
            CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(response);
        Assert.Empty(db.Usuarios);
    }

    [Fact]
    public async Task CrearUsuario_NombreVacioNoPersiste()
    {
        await using var db = Db();
        var controller = new UsuariosController(db, NullLogger<UsuariosController>.Instance);
        var response = await controller.Crear(
            new CrearUsuarioRequest("noname@ci.invalid", Password, "admin", "   "),
            CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(response);
        Assert.Empty(db.Usuarios);
    }

    [Theory]
    [InlineData("admin", "Admin")]
    [InlineData("vendedor", "Vendedor")]
    [InlineData("comprador", "Comprador")]
    [InlineData(" admin ", "Admin")]
    [InlineData("VEnDeDoR", "Vendedor")]
    [InlineData("administrador", null)]
    [InlineData("", null)]
    public void Canonizar_AceptaSoloTresRoles(string entrada, string? esperado) =>
        Assert.Equal(esperado, UsuarioRoles.Canonizar(entrada));
}
