using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace FiscalDocs.IntegrationTests.Api;

[TestFixture]
public class HealthTimeoutTests
{
    private static readonly TimeSpan MaxCheckTime = TimeSpan.FromSeconds(5);

    private TcpListener _listener = null!;
    private CancellationTokenSource _stopping = null!;
    private readonly List<Socket> _accepted = [];

    // Accepts connections and never answers, like a database that froze.
    [SetUp]
    public void StartSilentDatabase()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _stopping = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                while (!_stopping.IsCancellationRequested)
                {
                    var socket = await _listener.AcceptSocketAsync(_stopping.Token);
                    lock (_accepted)
                    {
                        _accepted.Add(socket);
                    }
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
            }
        });
    }

    [TearDown]
    public void StopSilentDatabase()
    {
        _stopping.Cancel();
        _listener.Stop();
        _listener.Dispose();
        lock (_accepted)
        {
            foreach (var socket in _accepted)
            {
                socket.Dispose();
            }
        }

        _stopping.Dispose();
    }

    private string SilentDatabase =>
        $"Host=127.0.0.1;Port={((IPEndPoint)_listener.LocalEndpoint).Port};Database=x;Username=x;Password=x";

    [Test]
    public async Task Ready_WhenTheDatabaseNeverAnswers_Returns503WithinTheCheckTimeout()
    {
        // Arrange
        using var factory = new ApiFactory(connectionString: SilentDatabase);
        using var client = factory.CreateClient();
        var stopwatch = Stopwatch.StartNew();

        // Act
        var response = await client.GetAsync("/health/ready");
        stopwatch.Stop();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().Be("Unhealthy");
        stopwatch.Elapsed.Should().BeLessThan(MaxCheckTime + TimeSpan.FromSeconds(3));
    }

    [Test]
    public void ReadyChecks_HaveAShortTimeout()
    {
        // Arrange
        using var factory = new ApiFactory();
        _ = factory.Server;

        // Act
        var registrations = factory.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations;

        // Assert
        registrations.Should().NotBeEmpty();
        registrations.Should().OnlyContain(r => r.Timeout <= MaxCheckTime);
    }
}
