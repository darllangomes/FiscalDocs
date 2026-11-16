using System.Text.RegularExpressions;
using FluentAssertions;

namespace FiscalDocs.IntegrationTests.Api;

// O guia de testes manuais é o que terceiros usam para validar a API. Estes testes só impedem que ele apodreça:
// um sample citado que não existe mais, ou uma rota nova que ninguém documentou.
[TestFixture]
public class ManualTestingGuideTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FiscalDocs.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("FiscalDocs.sln not found.");
    }

    private static string Guide() => File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "manual-testing.md"));

    [Test]
    public void Guide_Exists_AndTheReadmeLinksToIt()
    {
        // Act
        var readme = File.ReadAllText(Path.Combine(RepositoryRoot(), "README.md"));

        // Assert
        Guide().Should().NotBeNullOrWhiteSpace();
        readme.Should().Contain("docs/manual-testing.md");
    }

    [Test]
    public void Guide_OnlyMentionsSamplesThatExist()
    {
        // Act
        var mentioned = Regex.Matches(Guide(), @"samples/([\w.\-]+\.xml)")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        // Assert
        mentioned.Should().NotBeEmpty();
        foreach (var file in mentioned)
            File.Exists(Path.Combine(RepositoryRoot(), "samples", file)).Should().BeTrue($"samples/{file} is cited by the guide");
    }

    [TestCase("POST /api/v1/documents")]
    [TestCase("GET /api/v1/documents")]
    [TestCase("GET /api/v1/documents/{id}")]
    [TestCase("GET /api/v1/documents/{id}/xml")]
    [TestCase("PATCH /api/v1/documents/{id}")]
    [TestCase("DELETE /api/v1/documents/{id}")]
    [TestCase("/health/ready")]
    public void Guide_CoversEveryEndpoint(string route)
    {
        // Act & Assert
        Guide().Should().Contain(route);
    }

    [TestCase("201")]
    [TestCase("409")]
    [TestCase("412")]
    [TestCase("428")]
    [TestCase("422")]
    [TestCase("429")]
    [TestCase("If-Match")]
    [TestCase("issuerDocument")]
    [TestCase("***.982.247-**")]
    public void Guide_ExplainsTheExpectedResults(string expected)
    {
        // Act & Assert
        Guide().Should().Contain(expected);
    }
}
