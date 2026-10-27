/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using FluentAssertions;
using MarketplaceERP.Core.Entities;
using MarketplaceERP.Core.Services;
using Xunit;

namespace MarketplaceERP.Core.Tests;

public class ProveedorLogisticaMatcherTests
{
    [Fact]
    public void CrearClave_NormalizaAcentosPuntuacionYSufijoLegal()
    {
        ProveedorLogisticaMatcher.CrearClave("Distribuidora Tecnológica, S.A. de C.V.")
            .Should().Be("DISTRIBUIDORA TECNOLOGICA");
    }

    [Fact]
    public void Resolver_DebeVincularPorRazonSocialNormalizada()
    {
        var proveedor = CrearProveedor("DT Comercial", "Distribuidora Tecnológica S.A. de C.V.");

        ProveedorLogisticaMatcher.Resolver([proveedor], "Distribuidora Tecnologica")
            .Should().BeSameAs(proveedor);
    }

    [Fact]
    public void Resolver_DebeVincularPorNombreComercial()
    {
        var proveedor = CrearProveedor("Tech Wholesale El Salvador", "Tecnología Mayorista de El Salvador, S.A. de C.V.");

        ProveedorLogisticaMatcher.Resolver([proveedor], "Tech Wholesale El Salvador S.A.")
            .Should().BeSameAs(proveedor);
    }

    [Fact]
    public void Resolver_NoDebeVincularProveedorInactivoNiCoincidenciaParcial()
    {
        var inactivo = CrearProveedor("Samsung", "Samsung Electronics", activo: false);
        var activoDistinto = CrearProveedor("Samsung Parts", "Samsung Parts Logistics");

        ProveedorLogisticaMatcher.Resolver([inactivo, activoDistinto], "Samsung")
            .Should().BeNull();
    }

    private static Proveedor CrearProveedor(string comercial, string razon, bool activo = true) => new()
    {
        Id = Guid.NewGuid(),
        NombreComercial = comercial,
        RazonSocial = razon,
        Activo = activo
    };
}
