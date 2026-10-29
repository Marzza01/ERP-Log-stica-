/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using FluentAssertions;
using MarketplaceERP.Catalog.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MarketplaceERP.Catalog.Tests;

/// <summary>
/// Tests unitarios para el StockServiceMock (Equipo 2).
/// </summary>
public class StockServiceMockTests
{
    private readonly StockServiceMock _sut;

    public StockServiceMockTests()
    {
        _sut = new StockServiceMock(NullLogger<StockServiceMock>.Instance);
    }

    [Fact]
    public async Task ReservarStockAsync_DebeRetornarExitoso()
    {
        // Arrange
        var productoId = Guid.NewGuid();
        var ordenId = Guid.NewGuid();

        // Act
        var result = await _sut.ReservarStockAsync(productoId, 5, ordenId);

        // Assert
        result.Exitoso.Should().BeTrue();
        result.ReservaId.Should().NotBeEmpty();
        result.MensajeError.Should().BeNull();
    }

    [Fact]
    public async Task ConfirmarReservaAsync_DebeCompletarSinConsumirReserva()
    {
        // Arrange
        var reserva = await _sut.ReservarStockAsync(Guid.NewGuid(), 1, Guid.NewGuid());

        // Act
        var act = async () => await _sut.ConfirmarReservaAsync(reserva.ReservaId);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task LiberarStockAsync_DebeCompletarSinExcepcion()
    {
        // Arrange
        var reserva = await _sut.ReservarStockAsync(Guid.NewGuid(), 3, Guid.NewGuid());

        // Act
        var act = async () => await _sut.LiberarStockAsync(reserva.ReservaId);

        // Assert
        await act.Should().NotThrowAsync();
    }
}
