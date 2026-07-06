namespace Kiji.Tests.TestSite;

public static class TestSitePaths
{
    public static string StaticDirectory => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "Kiji.Tests",
        "TestSite",
        "wwwroot"));
}