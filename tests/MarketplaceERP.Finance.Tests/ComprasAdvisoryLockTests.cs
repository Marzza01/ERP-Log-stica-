using Npgsql;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public class ComprasAdvisoryLockTests
{
    [Fact]
    public async Task DosConexiones_NoPuedenRecibirLaMismaPoAlMismoTiempo()
    {
        var cadena = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION");
        if (string.IsNullOrWhiteSpace(cadena))
            return; // Prueba PostgreSQL ejecutada cuando CI suministra la cadena.

        var id = Guid.NewGuid().ToString("N");
        const string sqlAdquirir =
            "SELECT pg_try_advisory_lock(hashtext('erp-po-recibir'), hashtext(@po))";
        const string sqlLiberar =
            "SELECT pg_advisory_unlock(hashtext('erp-po-recibir'), hashtext(@po))";

        await using var primera = new NpgsqlConnection(cadena);
        await using var segunda = new NpgsqlConnection(cadena);
        await primera.OpenAsync();
        await segunda.OpenAsync();

        async Task<bool> EjecutarAsync(NpgsqlConnection conexion, string sql)
        {
            await using var comando = new NpgsqlCommand(sql, conexion);
            comando.Parameters.AddWithValue("po", id);
            return (bool)(await comando.ExecuteScalarAsync())!;
        }

        Assert.True(await EjecutarAsync(primera, sqlAdquirir));
        try
        {
            // Otra petición en otra conexión no puede adquirir el mismo candado.
            Assert.False(await EjecutarAsync(segunda, sqlAdquirir));
        }
        finally
        {
            Assert.True(await EjecutarAsync(primera, sqlLiberar));
        }

        // Una vez liberado, otra petición sí puede continuar.
        Assert.True(await EjecutarAsync(segunda, sqlAdquirir));
        Assert.True(await EjecutarAsync(segunda, sqlLiberar));
    }
}
