using System.IO.Compression;
using System.Reflection;
using System.Security;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Kiji.PackageTests;

public sealed class PackageFixture : IAsyncLifetime
{
    private FileStream? _lease;
    private string _repository = "";
    private string _root = "";
    private string _package = "";
    private string _version = "";
    private string _globalPackages = "";
    internal string Work => Path.Combine(_root, "work");
    internal string Site => Path.Combine(Work, "Site");
    private string Packages => Path.Combine(_root, "packages");
    private string Template => Path.Combine(_repository, "src", "Kiji.PackageTests", "Fixture");

    public async ValueTask InitializeAsync()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Kiji.slnx"))) { repository = repository.Parent; }
        Assert.NotNull(repository);
        _repository = repository.FullName;
        _root = Path.Combine(_repository, "artifacts", "package-tests");
        var locations = await RunningCommand.RunAsync(_repository, "dotnet", "nuget", "locals", "global-packages", "--list");
        const string prefix = "global-packages: ";
        Assert.StartsWith(prefix, locations.Output, StringComparison.Ordinal);
        _globalPackages = Path.GetFullPath(locations.Output.Trim()[prefix.Length..]);
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
            DeleteGeneratedFiles(); // Also recover an interrupted previous run.
            var feed = Path.Combine(_root, "feed");
            Directory.CreateDirectory(feed);
            if (Environment.GetEnvironmentVariable("KIJI_TEST_PACKAGE") is { Length: > 0 } supplied)
            {
                // CI supplies the exact package it will release; never select an old glob match.
                _package = Path.Combine(feed, Path.GetFileName(supplied));
                File.Copy(Path.GetFullPath(supplied), _package);
            }
            else
            {
                var configuration = typeof(PackageFixture).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
                await RunningCommand.RunAsync(_repository, "dotnet", "pack", "src/Kiji", "-c", configuration, "--no-build", "-o", feed);
                _package = Directory.GetFiles(feed, "*.nupkg").Single();
            }
            using var archive = ZipFile.OpenRead(_package);
            using var manifest = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).Open();
            var metadata = XDocument.Load(manifest).Root!.Elements().Single(element => element.Name.LocalName == "metadata");
            Assert.Equal("Kiji", metadata.Elements().Single(element => element.Name.LocalName == "id").Value);
            _version = metadata.Elements().Single(element => element.Name.LocalName == "version").Value;
            await PreparePackagesAsync(metadata, feed);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    private async Task PreparePackagesAsync(XElement metadata, string feed)
    {
        // Restore-only projects: resolve the tested package's dependencies into the normal
        // global cache, then install just Kiji privately before enabling the fallback cache.
        Write("Directory.Build.props", "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        Write("Directory.Build.targets", "<Project />");
        Write("Directory.Packages.props", "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>");
        Write("NuGet.Config", "<configuration><packageSources><clear/><add key=\"nuget\" value=\"https://api.nuget.org/v3/index.json\"/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders></configuration>");
        var group = metadata.Elements().Single(element => element.Name.LocalName == "dependencies")
            .Elements().Single(element => (string?)element.Attribute("targetFramework") == "net10.0");
        var references = group.Elements().Select(dependency => new XElement("PackageReference",
            new XAttribute("Include", dependency.Attribute("id")!.Value), new XAttribute("Version", dependency.Attribute("version")!.Value)));
        // The Web SDK also restores implicit static-web-asset packages used by both SDKs.
        Write("restore/dependencies/Dependencies.csproj", new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk.Web"), new XElement("ItemGroup", references)).ToString());
        // The SDK requests its internal web-assets pack only when Razor content exists.
        Write("restore/dependencies/Placeholder.razor", "@* Restore only; this project is never built. *@");
        await DotnetAsync("restore", "restore/dependencies/Dependencies.csproj", "--packages", _globalPackages, "--configfile", Path.Combine(Work, "NuGet.Config"));
        Write("restore/kiji/Kiji.csproj", $"""
            <Project Sdk="Microsoft.NET.Sdk"><ItemGroup>
              <PackageDownload Include="Kiji" Version="[{SecurityElement.Escape(_version)}]" />
            </ItemGroup></Project>
            """);
        await DotnetAsync("restore", "restore/kiji/Kiji.csproj", "--packages", Packages, "--source", feed, "--configfile", Path.Combine(Work, "NuGet.Config"));
        AssertPrivatePackage();
    }

    internal async Task ScenarioAsync(string name, Func<Task> action, string sdk = "Microsoft.NET.Sdk.Razor")
    {
        try
        {
            DeleteOwned(Work);
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
            // External dependencies must already exist in the global fallback cache.
            // A local-only source prevents accidental private restores of SkiaSharp.
            Write("NuGet.Config", $"<configuration><packageSources><clear/><add key=\"local\" value=\"{SecurityElement.Escape(Path.Combine(_root, "feed"))}\"/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders></configuration>");
            Replace("Site/PackageSite.csproj", "Microsoft.NET.Sdk.Razor", sdk);
            // Tiny valid PNG, kept textual in the fixture source tree.
            File.WriteAllBytes(Path.Combine(Site, "content", "source.png"), Convert.FromBase64String(File.ReadAllText(Path.Combine(Site, "content", "source.png.base64"))));
            await action();
            AssertPrivatePackage();
        }
        catch (Exception exception) { throw new InvalidOperationException($"Package scenario: {name}", exception); }
        finally { DeleteOwned(Work); }
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

    internal void Remove(string relative) => DeleteOwned(Path.Combine(Work, relative));

    internal Task<CommandResult> DotnetAsync(params string[] arguments) => RunningCommand.RunAsync(Work, "dotnet", arguments);
    internal Task<CommandResult> PublishAsync(params string[] options) => DotnetAsync(["publish", "Site/PackageSite.csproj", "-c", "Release", "-o", Path.Combine(Site, "dist"), .. options]);

    internal async Task<string> WithServerAsync(bool watch, Func<HttpClient, Task> check, string assembly = "Site/bin/Release/net10.0/PackageSite.dll")
    {
        await using var command = RunningCommand.Start(Work, watch
            ? ["watch", "--project", "Site/PackageSite.csproj", "--non-interactive", "--", "--urls", "http://127.0.0.1:0"]
            : ["exec", assembly, "--urls", "http://127.0.0.1:0"]);
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = System.Net.DecompressionMethods.None })
            { BaseAddress = await command.AddressAsync(), Timeout = TimeSpan.FromSeconds(15) };
            await check(client);
        }
        catch (Exception exception)
        {
            await command.StopAsync();
            throw new InvalidOperationException($"Consumer server failed.\n{command.Output}", exception);
        }
        await command.StopAsync();
        return command.Output;
    }

    private void AssertPrivatePackage()
    {
        Assert.Equal("kiji", Path.GetFileName(Assert.Single(Directory.GetDirectories(Packages))));
        var installed = Directory.GetFiles(Path.Combine(Packages, "kiji"), "*.nupkg", SearchOption.AllDirectories).Single();
        Assert.Equal(File.ReadAllBytes(_package), File.ReadAllBytes(installed));
    }

    internal async Task AssertPackageAndLinksAsync()
    {
        var expectedKiji = File.ReadAllBytes(Directory.GetFiles(Path.Combine(Packages, "kiji"), "Kiji.dll", SearchOption.AllDirectories).Single());
        var consumerAssemblies = Directory.GetFiles(Work, "Kiji.dll", SearchOption.AllDirectories);
        Assert.NotEmpty(consumerAssemblies);
        foreach (var assembly in consumerAssemblies) { Assert.Equal(expectedKiji, File.ReadAllBytes(assembly)); }
        using var assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(Site, "obj/project.assets.json")));
        var nativeIdentities = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var library in assets.RootElement.GetProperty("libraries").EnumerateObject())
        {
            if (library.Value.GetProperty("type").GetString() != "package" || library.Name.StartsWith("Kiji/", StringComparison.Ordinal)) { continue; }
            var source = Path.Combine(_globalPackages, library.Value.GetProperty("path").GetString()!);
            Assert.True(Directory.Exists(source), $"Dependency is not in the global cache: {library.Name}");
            foreach (var file in library.Value.GetProperty("files").EnumerateArray())
            {
                var relative = file.GetString()!;
                if (Path.GetFileName(relative).StartsWith("libSkiaSharp.", StringComparison.Ordinal))
                { nativeIdentities.Add(relative, await IdentityAsync(Path.Combine(source, relative))); }
            }
        }
        var native = Directory.GetFiles(Work, "libSkiaSharp.*", SearchOption.AllDirectories);
        Assert.NotEmpty(native);
        foreach (var target in native)
        {
            var relative = target[target.LastIndexOf("runtimes" + Path.DirectorySeparatorChar, StringComparison.Ordinal)..].Replace(Path.DirectorySeparatorChar, '/');
            Assert.Equal(nativeIdentities[relative], await IdentityAsync(target));
        }
    }

    private async Task<string> IdentityAsync(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var result = await RunningCommand.RunAsync(Work, "fsutil", "file", "queryfileid", path);
            var id = Regex.Match(result.Output, "0x[0-9a-fA-F]+").Value;
            Assert.NotEmpty(id);
            return id;
        }
        // Linux CI: device + inode, not summed file lengths (which count links repeatedly).
        return (await RunningCommand.RunAsync(Work, "stat", "-c", "%d:%i", path)).Output.Trim();
    }

    private void DeleteOwned(string path)
    {
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) { throw new InvalidOperationException("Cleanup escaped package workspace."); }
        if (Directory.Exists(full)) { Directory.Delete(full, recursive: true); }
    }

    private void DeleteGeneratedFiles()
    {
        DeleteOwned(Work);
        DeleteOwned(Path.Combine(_root, "feed"));
        DeleteOwned(Packages);
        // Remove metadata from the former persistent dependency cache, too.
        File.Delete(Path.Combine(_root, "cache-key"));
    }

    public async ValueTask DisposeAsync()
    {
        if (_lease is null) { return; }
        try { DeleteGeneratedFiles(); }
        finally { await _lease.DisposeAsync(); _lease = null; }
    }
}
