using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kiji.PackageTests;

public sealed class PackageFixture : IAsyncLifetime
{
    private FileStream? _lease;
    private ScenarioWatchdog? _watchdog;
    private string _repository = "";
    private string _root = "";
    private string _version = "";
    private string _globalPackages = "";
    private string Work => Path.Combine(_root, "work");
    internal string Site => Path.Combine(Work, "Site");
    private string Packages => Path.Combine(_root, "packages");
    internal IEnumerable<string> LibraryAssemblyFiles => Directory.EnumerateFiles(
        Path.Combine(Packages, "kiji", _version.ToLowerInvariant(), "lib", "net10.0"), "*.dll");

    private readonly Dictionary<string, string?> _runtimePackages = new(StringComparer.Ordinal);
    private string Template => Path.Combine(_repository, "tests", "Kiji.PackageTests", "Fixture");

    public async ValueTask InitializeAsync()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Kiji.slnx"))) { repository = repository.Parent; }
        Assert.NotNull(repository);
        _repository = repository.FullName;
        _root = Path.Combine(_repository, "artifacts", "package-tests");
        using var configuration = new ConfigurationManager();
        configuration.AddEnvironmentVariables();
        _watchdog = new ScenarioWatchdog(configuration["KIJI_TEST_TRACE"] is { Length: > 0 } trace ? trace : null);
        RunningCommand.Trace = _watchdog.Trace;
        var locations = await RunningCommand.RunAsync(_repository, "nuget", "locals", "global-packages", "--list");
        const string prefix = "global-packages: ";
        Assert.StartsWith(prefix, locations, StringComparison.Ordinal);
        _globalPackages = Path.GetFullPath(locations.Trim()[prefix.Length..]);
        Assert.False(string.Equals(Path.TrimEndingDirectorySeparator(_globalPackages), _root, StringComparison.OrdinalIgnoreCase)
            || _globalPackages.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "The global NuGet cache must be outside the test workspace.");
        Directory.CreateDirectory(_root);
        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (_lease is null)
        {
            try { _lease = new FileStream(Path.Combine(_root, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow < deadline) { await Task.Delay(200); }
        }
        try
        {
            await DeleteGeneratedFilesAsync();
            var feed = Path.Combine(_root, "feed");
            Directory.CreateDirectory(feed);
            string package;
            if (configuration["KIJI_TEST_PACKAGE"] is { Length: > 0 } supplied)
            {
                package = Path.Combine(feed, Path.GetFileName(supplied));
                File.Copy(Path.GetFullPath(supplied), package);
                var version = Path.GetFileNameWithoutExtension(supplied)["Kiji.".Length..];
                foreach (var runtimePackage in Directory.GetFiles(Path.GetDirectoryName(Path.GetFullPath(supplied))!, $"Kiji.Runtime.*.{version}.nupkg"))
                {
                    File.Copy(runtimePackage, Path.Combine(feed, Path.GetFileName(runtimePackage)));
                }
            }
            else
            {
                var buildConfiguration = typeof(PackageFixture).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
                await RunningCommand.RunAsync(_repository, "pack", "src/Kiji", "-c", buildConfiguration, "--no-build", "-o", feed);
                package = Directory.GetFiles(feed, "Kiji.*.nupkg").Single(path => !Path.GetFileName(path).StartsWith("Kiji.Runtime.", StringComparison.Ordinal));
            }
            using var archive = ZipFile.OpenRead(package);
            using var manifest = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).Open();
            var metadata = XDocument.Load(manifest).Root!.Elements().Single(element => element.Name.LocalName == "metadata");
            Assert.Equal("Kiji", metadata.Elements().Single(element => element.Name.LocalName == "id").Value);
            _version = metadata.Elements().Single(element => element.Name.LocalName == "version").Value;
            using var graph = archive.GetEntry("runtime.json")!.Open();
            using var runtimeGraph = JsonDocument.Parse(graph);
            foreach (var runtime in runtimeGraph.RootElement.GetProperty("runtimes").EnumerateObject())
            {
                _runtimePackages[runtime.Name] = runtime.Value.GetProperty("Kiji").EnumerateObject()
                    .Select(dependency => dependency.Name).SingleOrDefault(id => id.StartsWith("Kiji.Runtime.", StringComparison.Ordinal));
            }
            foreach (var runtimePackage in _runtimePackages.Values.OfType<string>())
            {
                Assert.True(File.Exists(Path.Combine(feed, $"{runtimePackage}.{_version}.nupkg")), $"{runtimePackage} was not packed.");
            }
            await PreparePackagesAsync(metadata, runtimeGraph, feed);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    private async Task PreparePackagesAsync(XElement metadata, JsonDocument runtimeGraph, string feed)
    {
        Write("Directory.Build.props", "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        Write("Directory.Build.targets", "<Project />");
        Write("Directory.Packages.props", "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>");
        Write("NuGet.Config", "<configuration><packageSources><clear/><add key=\"nuget\" value=\"https://api.nuget.org/v3/index.json\"/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders></configuration>");
        var group = metadata.Elements().Single(element => element.Name.LocalName == "dependencies")
            .Elements().Single(element => (string?)element.Attribute("targetFramework") == "net10.0");
        var runtimeDependencies = runtimeGraph.RootElement.GetProperty("runtimes").EnumerateObject()
            .SelectMany(runtime => runtime.Value.GetProperty("Kiji").EnumerateObject())
            .Where(dependency => !dependency.Name.StartsWith("Kiji.", StringComparison.Ordinal))
            .Select(dependency => (Id: dependency.Name, Version: dependency.Value.GetString()!))
            .Distinct();
        var references = group.Elements().Select(dependency => (Id: dependency.Attribute("id")!.Value, Version: dependency.Attribute("version")!.Value))
            .Concat(runtimeDependencies)
            .Select(dependency => new XElement("PackageReference", new XAttribute("Include", dependency.Id), new XAttribute("Version", dependency.Version)));
        Write("restore/dependencies/Dependencies.csproj", new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk.Web"), new XElement("ItemGroup", references)).ToString());
        Write("restore/dependencies/Placeholder.razor", "@* Restore only; this project is never built. *@");
        await DotnetAsync("restore", "restore/dependencies/Dependencies.csproj", "--packages", _globalPackages, "--configfile", Path.Combine(Work, "NuGet.Config"));
        Write("restore/kiji/Kiji.csproj", $"""
            <Project Sdk="Microsoft.NET.Sdk"><ItemGroup>
              <PackageDownload Include="Kiji{string.Concat(_runtimePackages.Values.OfType<string>().Select(id => ";" + id))}" Version="[{SecurityElement.Escape(_version)}]" />
            </ItemGroup></Project>
            """);
        await DotnetAsync("restore", "restore/kiji/Kiji.csproj", "--packages", Packages, "--source", feed, "--configfile", Path.Combine(Work, "NuGet.Config"));
    }

    internal async Task ScenarioAsync(Func<Task> action, string sdk = "Microsoft.NET.Sdk.Razor",
        [System.Runtime.CompilerServices.CallerMemberName] string scenario = "")
    {
        _watchdog?.Trace($"scenario {scenario} ({sdk}) starts");
        try
        {
            await DeleteOwnedAsync(Work);
            foreach (var source in Directory.GetFiles(Template, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(Template, source);
                if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")) { continue; }
                var destination = Path.Combine(Work, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination);
            }
            Write("Directory.Build.props", $"""
                <Project><PropertyGroup>
                  <TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
                  <RestorePackagesPath>{SecurityElement.Escape(Packages)}</RestorePackagesPath>
                  <RestoreFallbackFolders>{SecurityElement.Escape(_globalPackages)}</RestoreFallbackFolders>
                  <CreateHardLinksForCopyLocalIfPossible>true</CreateHardLinksForCopyLocalIfPossible>
                  <KijiPackageVersion>{SecurityElement.Escape(_version)}</KijiPackageVersion>
                </PropertyGroup></Project>
                """);
            Write("Directory.Packages.props", "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>");
            Write("Directory.Build.targets", "<Project />");
            Write("NuGet.Config", $"<configuration><packageSources><clear/><add key=\"local\" value=\"{SecurityElement.Escape(Path.Combine(_root, "feed"))}\"/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders></configuration>");
            Replace("Site/PackageSite.csproj", "Microsoft.NET.Sdk.Razor", sdk);
            File.WriteAllBytes(Path.Combine(Site, "content", "source.png"), Convert.FromBase64String(File.ReadAllText(Path.Combine(Site, "content", "source.png.base64"))));
            await action();
            _watchdog?.Trace($"scenario {scenario} ({sdk}) passed; cleaning up");
        }
        catch (Exception exception)
        {
            _watchdog?.Trace($"scenario {scenario} ({sdk}) failed; cleaning up: {exception.Message.Split('\n')[0]}");
            throw;
        }
        finally
        {
            await DeleteOwnedAsync(Work);
            _watchdog?.Trace($"scenario {scenario} ({sdk}) cleaned up");
        }
    }

    internal void Write(string relative, string text)
    {
        var path = Path.Combine(Work, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    internal void Replace(string relative, string before, string after)
    {
        var path = Path.Combine(Work, relative);
        File.WriteAllText(path, File.ReadAllText(path).Replace(before, after, StringComparison.Ordinal));
    }

    internal async Task<(string RuntimeIdentifier, string? Images)> ResolveRuntimeImagesAsync(string project)
    {
        var output = await DotnetAsync("msbuild", project, "-getProperty:RuntimeIdentifier", "-getProperty:BundledRuntimeIdentifierGraphFile");
        using var properties = JsonDocument.Parse(output[output.IndexOf('{', StringComparison.Ordinal)..]);
        var runtimeIdentifier = properties.RootElement.GetProperty("Properties").GetProperty("RuntimeIdentifier").GetString()!;
        var graphFile = properties.RootElement.GetProperty("Properties").GetProperty("BundledRuntimeIdentifierGraphFile").GetString()!;
        using var sdkGraph = JsonDocument.Parse(File.ReadAllText(graphFile));
        var runtimes = sdkGraph.RootElement.GetProperty("runtimes");
        var pending = new Queue<string>([runtimeIdentifier]);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryDequeue(out var runtime))
        {
            if (!visited.Add(runtime)) { continue; }
            if (_runtimePackages.TryGetValue(runtime, out var runtimePackage))
            {
                return (runtimeIdentifier, runtimePackage is null ? null
                    : Path.Combine(Packages, runtimePackage.ToLowerInvariant(), _version.ToLowerInvariant(), "runtimes", runtimePackage["Kiji.Runtime.".Length..], "native", "kiji-r2r"));
            }
            if (runtimes.TryGetProperty(runtime, out var entry) && entry.TryGetProperty("#import", out var imports))
            {
                foreach (var import in imports.EnumerateArray()) { pending.Enqueue(import.GetString()!); }
            }
        }
        return (runtimeIdentifier, null);
    }

    internal Task<string> DotnetAsync(params string[] arguments) => RunningCommand.RunAsync(Work, arguments);
    internal Task<string> PublishAsync(params string[] options) =>
        DotnetAsync(["publish", "Site/PackageSite.csproj", "-c", "Release", "-o", Path.Combine(Site, "dist"), .. options]);

    internal async Task WatchAsync(Func<HttpClient, Task> check)
    {
        await using var command = RunningCommand.Start(Work, "watch", "--project", "Site/PackageSite.csproj", "--non-interactive", "--", "--urls", "http://127.0.0.1:0");
        try
        {
            using var client = new HttpClient
            { BaseAddress = await command.AddressAsync(), Timeout = TimeSpan.FromSeconds(15) };
            await check(client);
        }
        catch (Exception exception)
        {
            await command.StopAsync();
            throw new InvalidOperationException($"Consumer server failed.\n{command.Output}", exception);
        }
    }

    private async Task DeleteOwnedAsync(string path)
    {
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) { throw new InvalidOperationException("Cleanup escaped package workspace."); }
        var elapsed = Stopwatch.StartNew();
        while (Directory.Exists(full))
        {
            try { Directory.Delete(full, recursive: true); return; }
            catch (Exception exception) when (OperatingSystem.IsWindows()
                && exception is IOException or UnauthorizedAccessException
                && (exception.HResult & 0xffff) is 5 or 32 or 33 or 145
                && elapsed.Elapsed < TimeSpan.FromSeconds(10))
            {
                await Task.Delay(100);
            }
        }
    }

    private async Task DeleteGeneratedFilesAsync()
    {
        await DeleteOwnedAsync(Work);
        await DeleteOwnedAsync(Path.Combine(_root, "feed"));
        await DeleteOwnedAsync(Packages);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_lease is null) { return; }
            try { await DeleteGeneratedFilesAsync(); }
            finally { await _lease.DisposeAsync(); _lease = null; }
        }
        finally
        {
            _watchdog?.Trace("fixture disposed");
            _watchdog?.Dispose();
            _watchdog = null;
        }
    }
}
