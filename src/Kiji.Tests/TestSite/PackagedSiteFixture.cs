using System.Diagnostics;
using System.Reflection;
using System.Security;
using System.Text.RegularExpressions;
using Xunit;

namespace Kiji.Tests.TestSite;

// Real SDK consumers catch import-order and packaging bugs that hand-written
// manifests cannot. Pack once for this class; each test owns a separate site.
public sealed class PackagedSiteFixture : IAsyncLifetime
{
    private string _version = "";
    public string Root { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Kiji.slnx")))
        {
            repository = repository.Parent;
        }
        Assert.NotNull(repository);
        Root = Path.Combine(repository.FullName, "artifacts", "package-tests", Guid.NewGuid().ToString("N"));
        Write(Root, "Directory.Build.props", "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>");
        Write(Root, "Directory.Build.targets", "<Project />");
        Write(Root, "Directory.Packages.props", "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>");
        var configuration = typeof(PackagedSiteFixture).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        await DotnetAsync(repository.FullName, "pack", "src/Kiji", "-c", configuration, "--no-build", "-o", Path.Combine(Root, "feed"));
        var package = Directory.GetFiles(Path.Combine(Root, "feed"), "*.nupkg").Single();
        _version = Path.GetFileNameWithoutExtension(package)["Kiji.".Length..];
        Write(Root, "NuGet.Config", $"<configuration><packageSources><clear/><add key=\"local\" value=\"{SecurityElement.Escape(Path.Combine(Root, "feed"))}\"/><add key=\"nuget\" value=\"https://api.nuget.org/v3/index.json\"/></packageSources></configuration>");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask; // Keep logs/fixtures in ignored artifacts for failed CI runs.

    public string CreateSite(string name, bool assets = false, bool worker = false)
    {
        var site = Path.Combine(Root, name);
        Write(site, "Site.csproj", $"""
            <Project Sdk="Microsoft.NET.Sdk.Razor">
              <PropertyGroup><OutputType>Exe</OutputType><AssemblyName>AssetSite</AssemblyName><RestorePackagesPath>{SecurityElement.Escape(Path.Combine(Root, "packages"))}</RestorePackagesPath></PropertyGroup>
              <ItemGroup><PackageReference Include="Kiji" Version="{_version}" /></ItemGroup>
              {(worker ? "<PropertyGroup><ServiceWorkerAssetsManifest>service-worker-assets.js</ServiceWorkerAssetsManifest></PropertyGroup><ItemGroup><ServiceWorker Include=\"wwwroot/service-worker.js\" PublishedContent=\"service-worker.published.js\" /></ItemGroup>" : "")}
            </Project>
            """);
        Write(site, "Program.cs", """
            using Kiji;
            var site = StaticSite.Create(args);
            site.Info = new() { Name = "SDK regression", BaseUrl = new Uri("https://example.test/kiji/") };
            site.AddStaticPages();
            await site.RunAsync();
            """);
        Write(site, "Home.razor", "@page \"/\"\n<h1>SDK regression</h1>" + (assets ? "\n<a id=\"asset\" href=\"@Assets[\"app.js\"]\">asset</a>\n<Microsoft.AspNetCore.Components.ImportMap />" : ""));
        if (assets)
        {
            Write(site, "wwwroot/app.js", "export const value = 'original';\n" + new string(' ', 2048));
            Write(site, "Home.razor.css", "h1 { color: teal; }");
        }
        if (worker)
        {
            Write(site, "wwwroot/service-worker.js", "// development worker");
            Write(site, "service-worker.published.js", "// published worker");
        }
        return site;
    }

    public Task<string> PublishAsync(string site, params string[] options) =>
        DotnetAsync(site, ["publish", "-c", "Release", "-o", "dist", .. options]);

    public async Task<string> DotnetAsync(string workingDirectory, params string[] arguments)
    {
        var start = StartInfo(workingDirectory, arguments);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
        var log = await output + await errors;
        var path = Path.Combine(Root, $"{Path.GetFileName(workingDirectory)}-{arguments[0]}-{Guid.NewGuid():N}.log");
        await File.WriteAllTextAsync(path, log);
        Assert.True(process.ExitCode == 0, $"dotnet {string.Join(' ', arguments)} failed. See {path}\n{log[^Math.Min(log.Length, 4000)..]}");
        return log;
    }

    public static async Task WithServerAsync(string site, Func<HttpClient, Task> check, bool watch = false, bool browserRefresh = false,
        string? workingDirectory = null, string? assemblyPath = null)
    {
        string[] arguments = watch
            ? ["watch", "--project", site, "--non-interactive", "--", "--urls", "http://127.0.0.1:0"]
            : ["exec", assemblyPath ?? Path.Combine(site, "bin/Release/net10.0/AssetSite.dll"), "--urls", "http://127.0.0.1:0"];
        using var process = new Process { StartInfo = StartInfo(workingDirectory ?? site, arguments) };
        process.StartInfo.Environment["DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER"] = browserRefresh ? "0" : "1";
        if (browserRefresh)
        {
            process.StartInfo.Environment["DOTNET_WATCH_SUPPRESS_BROWSER_REFRESH"] = "0";
        }
        var ready = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new System.Collections.Concurrent.ConcurrentQueue<string>();
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null) { output.Enqueue(args.Data); }
            var match = Regex.Match(args.Data ?? "", @"http://127\.0\.0\.1:\d+");
            if (match.Success) { ready.TrySetResult(new Uri(match.Value)); }
        };
        process.Start();
        var errors = process.StandardError.ReadToEndAsync();
        process.BeginOutputReadLine();
        try
        {
            var address = await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = System.Net.DecompressionMethods.None })
            {
                BaseAddress = address,
                Timeout = TimeSpan.FromSeconds(15),
            };
            await check(client);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); }
            await process.WaitForExitAsync();
            process.WaitForExit();
            await File.WriteAllLinesAsync(Path.Combine(site, "server.log"), output);
            await File.WriteAllTextAsync(Path.Combine(site, "server-errors.log"), await errors);
        }
    }

    public static void Write(string directory, string relativePath, string text)
    {
        var path = Path.Combine(directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static ProcessStartInfo StartInfo(string directory, string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) { start.ArgumentList.Add(argument); }
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        start.Environment.Remove("KIJI_OUTPUT");
        return start;
    }
}
