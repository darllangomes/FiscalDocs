namespace FiscalDocs.IntegrationTests.Support;

internal static class SampleFiles
{
    public static string ReadAllText(string fileName) =>
        File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "samples", fileName));
}