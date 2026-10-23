using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using OctoSupply.Api.Data;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace OctoSupply.Api.Tests;

public sealed class CartTestDatabase : IAsyncLifetime
{
    private readonly string? _previousFile = Environment.GetEnvironmentVariable("DB_FILE");
    private readonly string? _previousMigrations = Environment.GetEnvironmentVariable("DB_MIGRATIONS_DIR");
    public string FilePath { get; } = Path.Combine(Path.GetTempPath(), $"octosupply-cart-{Guid.NewGuid():N}.db");
    public SqliteConnectionFactory Factory { get; } = new(NullLogger<SqliteConnectionFactory>.Instance);

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("DB_FILE", FilePath);
        Environment.SetEnvironmentVariable("DB_MIGRATIONS_DIR",
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "database", "migrations")));
        await new DatabaseMigrator(Factory, NullLogger<DatabaseMigrator>.Instance).InitializeAsync(false);
        await ExecuteAsync("""
            INSERT INTO suppliers (supplier_id, name) VALUES (1, 'Cart test supplier');
            INSERT INTO products (product_id, supplier_id, name, price, sku, unit, discount)
            VALUES (1, 1, 'Discounted product', 19.99, 'CART-1', 'each', 0.15),
                   (2, 1, 'Rounding product', 1.01, 'CART-2', 'each', 0.5),
                   (3, 1, 'Regular product', 10.10, 'CART-3', 'each', 0);
            """);
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = await Factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable("DB_FILE", _previousFile);
        Environment.SetEnvironmentVariable("DB_MIGRATIONS_DIR", _previousMigrations);
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = FilePath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
        return Task.CompletedTask;
    }
}
