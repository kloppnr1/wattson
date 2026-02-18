using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Infrastructure.Tests;

/// <summary>
/// Shared database fixture for integration tests.
/// Spins up a PostgreSQL + TimescaleDB container once, shared across all tests in the collection.
/// </summary>
public class DatabaseFixture : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("timescale/timescaledb:latest-pg16")
            .WithDatabase("wattson_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        // Create TimescaleDB extension and apply migrations
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS timescaledb;");
        await context.Database.MigrateAsync();
    }

    public WattsOnDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<WattsOnDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new WattsOnDbContext(options);
    }

    /// <summary>Creates a fresh context and cleans all data (keeps schema)</summary>
    public async Task<WattsOnDbContext> CreateCleanContext()
    {
        var ctx = CreateContext();
        // Truncate all tables to get a clean state
        await ctx.Database.ExecuteSqlRawAsync(@"
            DO $$ DECLARE
                r RECORD;
            BEGIN
                FOR r IN (SELECT tablename FROM pg_tables WHERE schemaname = 'public' AND tablename != '__EFMigrationsHistory') LOOP
                    EXECUTE 'TRUNCATE TABLE ' || quote_ident(r.tablename) || ' CASCADE';
                END LOOP;
            END $$;
        ");
        return ctx;
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}

[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture> { }
