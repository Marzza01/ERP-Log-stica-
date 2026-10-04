/**
 * ERP desarrollado por Marcelo Zepeda
 * Todos los derechos reservados © 2026
 */

using FluentAssertions;
using MarketplaceERP.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MarketplaceERP.Core.Tests;

/// <summary>
/// Tests unitarios para el CommissionServiceMock (Equipo 1).
/// </summary>
public class CommissionServiceMockTests
{
    private readonly CommissionServiceMock _sut;

    public CommissionServiceMockTests()
    {
        _sut = new CommissionServiceMock(NullLogger<CommissionServiceMock>.Instance);
    }

    [Fact]
    public async Task CalcularComisionAsync_DebeRetornar10Porciento()
    {
        // Arrange
        var vendedorId = Guid.NewGuid();
        var montoTotal = 100m;

        // Act
        var comision = await _sut.CalcularComisionAsync(vendedorId, montoTotal);

        // Assert
        comision.Should().Be(10m);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 5)]
    [InlineData(199.99, 20.00)]
    [InlineData(1000, 100)]
    public async Task CalcularComisionAsync_DebeCalcularCorrectamente(decimal monto, decimal comisionEsperada)
    {
        // Act
        var comision = await _sut.CalcularComisionAsync(Guid.NewGuid(), monto);

        // Assert
        comision.Should().BeApproximately(comisionEsperada, 0.01m);
    }

    [Fact]
    public async Task CalcularComisionAsync_ComisionNuncaDebeSerNegativa()
    {
        // Arrange
        var montoTotal = 0m;

        // Act
        var comision = await _sut.CalcularComisionAsync(Guid.NewGuid(), montoTotal);

        // Assert
        comision.Should().BeGreaterThanOrEqualTo(0);
    }
}
