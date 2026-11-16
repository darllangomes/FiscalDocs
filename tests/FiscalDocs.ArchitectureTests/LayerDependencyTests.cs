using System.Reflection;
using FiscalDocs.Application.Ingestion;
using FiscalDocs.Domain.Entities;
using FiscalDocs.Infrastructure.Persistence;
using FluentAssertions;
using NetArchTest.Rules;

namespace FiscalDocs.ArchitectureTests;

[TestFixture]
public class LayerDependencyTests
{
    private const string DomainNamespace = "FiscalDocs.Domain";
    private const string ApplicationNamespace = "FiscalDocs.Application";
    private const string InfrastructureNamespace = "FiscalDocs.Infrastructure";
    private const string ApiNamespace = "FiscalDocs.Api";
    private const string WorkerNamespace = "FiscalDocs.Worker";

    private static readonly string[] Frameworks =
    [
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "RabbitMQ.Client",
        "Microsoft.AspNetCore"
    ];

    private static readonly Assembly Domain = typeof(FiscalDocument).Assembly;
    private static readonly Assembly Application = typeof(IngestDocumentHandler).Assembly;
    private static readonly Assembly Infrastructure = typeof(FiscalDocsDbContext).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;
    private static readonly Assembly Worker = typeof(FiscalDocs.Worker.Worker).Assembly;

    private static string Violations(TestResult result) =>
        string.Join(", ", result.FailingTypeNames ?? []);

    [Test]
    public void Domain_DoesNotDependOnAnyOtherLayer()
    {
        // Act
        var result = Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny(ApplicationNamespace, InfrastructureNamespace, ApiNamespace, WorkerNamespace)
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(Violations(result));
    }

    [Test]
    public void Domain_DoesNotDependOnFrameworks()
    {
        // Act
        var result = Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny(Frameworks)
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(Violations(result));
    }

    [Test]
    public void Application_DoesNotDependOnOuterLayers()
    {
        // Act
        var result = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOnAny(InfrastructureNamespace, ApiNamespace, WorkerNamespace)
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(Violations(result));
    }

    [Test]
    public void Application_DoesNotDependOnFrameworks()
    {
        // Act
        var result = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOnAny(Frameworks)
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(Violations(result));
    }

    [Test]
    public void Infrastructure_DoesNotDependOnTheHosts()
    {
        // Act
        var result = Types.InAssembly(Infrastructure)
            .ShouldNot()
            .HaveDependencyOnAny(ApiNamespace, WorkerNamespace)
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(Violations(result));
    }

    [Test]
    public void Api_DoesNotDependOnTheWorker()
    {
        // Act
        var result = Types.InAssembly(Api)
            .ShouldNot()
            .HaveDependencyOn(WorkerNamespace)
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(Violations(result));
    }

    [Test]
    public void Worker_DoesNotDependOnTheApi()
    {
        // Act
        var result = Types.InAssembly(Worker)
            .ShouldNot()
            .HaveDependencyOn(ApiNamespace)
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(Violations(result));
    }

    [Test]
    public void Controllers_DoNotDependOnInfrastructure()
    {
        // Act
        var result = Types.InAssembly(Api)
            .That()
            .ResideInNamespace($"{ApiNamespace}.Controllers")
            .ShouldNot()
            .HaveDependencyOn(InfrastructureNamespace)
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(Violations(result));
    }

    [Test]
    public void Handlers_AreSealed()
    {
        // Act
        var result = Types.InAssembly(Application)
            .That()
            .HaveNameEndingWith("Handler")
            .Should()
            .BeSealed()
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(Violations(result));
    }

    [Test]
    public void ApplicationAbstractions_AreInterfaces()
    {
        // Act
        var result = Types.InAssembly(Application)
            .That()
            .ResideInNamespace($"{ApplicationNamespace}.Abstractions")
            .Should()
            .BeInterfaces()
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(Violations(result));
    }

    // Positive controls: if a namespace string above had a typo the rules would pass vacuously.
    // "ShouldNot" fails when at least one type has the dependency, which is what proves it is detected.

    private static bool AnyTypeDependsOn(Assembly assembly, string dependency) =>
        !Types.InAssembly(assembly).ShouldNot().HaveDependencyOn(dependency).GetResult().IsSuccessful;

    [Test]
    public void Control_InfrastructureDoesDependOnApplicationAndTheDataFrameworks()
    {
        // Act & Assert
        foreach (var dependency in new[] { ApplicationNamespace, "Microsoft.EntityFrameworkCore", "Npgsql", "RabbitMQ.Client" })
        {
            AnyTypeDependsOn(Infrastructure, dependency).Should().BeTrue($"Infrastructure should reference {dependency}");
        }
    }

    [Test]
    public void Control_ApiAndWorkerDoDependOnInfrastructure()
    {
        // Act & Assert
        foreach (var host in new[] { Api, Worker })
        {
            AnyTypeDependsOn(host, InfrastructureNamespace).Should()
                .BeTrue($"{host.GetName().Name} should reference Infrastructure");
        }
    }

    [Test]
    public void Control_ApiDoesDependOnAspNetCore()
    {
        // Act & Assert
        AnyTypeDependsOn(Api, "Microsoft.AspNetCore").Should().BeTrue();
    }

    [Test]
    public void Control_DomainTypesAreDiscovered()
    {
        // Act
        var count = Types.InAssembly(Domain).That().ResideInNamespace(DomainNamespace).GetTypes().Count();

        // Assert
        count.Should().BeGreaterThan(5);
    }
}
