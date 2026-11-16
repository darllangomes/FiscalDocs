using System.Net;
using System.Text;
using FiscalDocs.IntegrationTests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FiscalDocs.IntegrationTests.Api;

[TestFixture]
public class MigrateOnStartupTests
{
    private const string Route = "/api/v1/documents";

    private string _databaseName = null!;
    private string _connectionString = null!;

    [SetUp]
    public async Task SetUp()
    {
        _databaseName = $"migrate_{Guid.NewGuid():N}";
        var builder = new NpgsqlConnectionStringBuilder(InfrastructureFixture.ConnectionString);
        await ExecuteAdminAsync($"CREATE DATABASE \"{_databaseName}\"");
        builder.Database = _databaseName;
        _connectionString = builder.ConnectionString;
    }

    [TearDown]
    public async Task TearDown()
    {
        NpgsqlConnection.ClearAllPools();
        await ExecuteAdminAsync($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)");
    }

    private static async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(InfrastructureFixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private Infrastructure.Persistence.FiscalDocsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<Infrastructure.Persistence.FiscalDocsDbContext>()
            .UseNpgsql(_connectionString).Options);

    [Test]
    public async Task Startup_WithMigrateOnStartup_AppliesEveryMigrationToAnEmptyDatabase()
    {
        // Arrange
        using var factory = new ApiFactory(connectionString: _connectionString, migrateOnStartup: true);

        // Act
        using var client = factory.CreateAuthenticatedClient();

        // Assert
        await using var context = CreateContext();
        var all = context.Database.GetMigrations();
        (await context.Database.GetAppliedMigrationsAsync()).Should().Equal(all);
        (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    [Test]
    public async Task Startup_WithMigrateOnStartup_ServesRequestsRightAway()
    {
        // Arrange
        using var factory = new ApiFactory(connectionString: _connectionString, migrateOnStartup: true);
        using var client = factory.CreateAuthenticatedClient();
        var xml = new StringContent(SampleFiles.ReadAllText("nfe-cnpj-numerico.xml"), Encoding.UTF8, "application/xml");

        // Act
        var response = await client.PostAsync(Route, xml);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Test]
    public void Startup_WithMigrateOnStartup_IsSafeWhenTheSchemaIsAlreadyUpToDate()
    {
        // Arrange
        using (var first = new ApiFactory(connectionString: _connectionString, migrateOnStartup: true))
        {
            using var firstClient = first.CreateAuthenticatedClient();
        }

        // Act
        var act = () =>
        {
            using var second = new ApiFactory(connectionString: _connectionString, migrateOnStartup: true);
            using var secondClient = second.CreateAuthenticatedClient();
        };

        // Assert
        act.Should().NotThrow();
    }

    [Test]
    public async Task Startup_ByDefault_LeavesTheSchemaUntouched()
    {
        // Arrange
        using var factory = new ApiFactory(connectionString: _connectionString);

        // Act
        using var client = factory.CreateAuthenticatedClient();

        // Assert
        await using var context = CreateContext();
        (await context.Database.GetAppliedMigrationsAsync()).Should().BeEmpty();
    }
}
