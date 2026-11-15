namespace FiscalDocs.UnitTests.Support;

internal static class SampleFiles
{
    public static Stream Open(string fileName) =>
        File.OpenRead(Path.Combine(TestContext.CurrentContext.TestDirectory, "samples", fileName));

    public static string ReadAllText(string fileName) =>
        File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "samples", fileName));
}
