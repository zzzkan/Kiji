#!/usr/bin/env python3
"""Compares the cold publish process of two Kiji revisions through real package consumers.

Packs the baseline revision and the working tree, publishes the frozen synthetic site against
each package, then times the command KijiGenerateSite executes, alternating the two consumers.
Reports medians per scenario and checks after every round that both produce identical output.

    python3 measure_cold_publish.py --baseline origin/main --work artifacts/cold-publish

Run it from the repository root. The work directory must not exist; it is left in place.
"""
import argparse
import hashlib
import os
import pathlib
import shutil
import statistics
import subprocess
import sys
import time

PROGRAM = """\
using Kiji;
using Kiji.Feeds;
using Kiji.Markdown;
using Kiji.Sitemaps;
using Kiji.SyntheticSite;
using Kiji.SyntheticSite.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = StaticSite.CreateBuilder(args);
builder.UseSiteInfo(new SiteInfo
{
    BaseUrl = new Uri("https://bench.example.com/"),
    Name = "Kiji Bench",
    Description = "Synthetic benchmark site.",
    Language = "en",
    Author = "bench",
});
builder.AddMarkdownContent<PostFrontMatter>(static options => options.Directory = "contents");
builder.UseDefaultLayout<MainLayout>();
builder.UseNotFoundPage<NotFoundPage>();
builder.AddPageParameters<PostPage>(static services => services
    .GetRequiredService<ContentCollection<MarkdownContent<PostFrontMatter>>>()
    .Select(static post => new { Slug = PostSlug.From(post.Path), ContentKey = post.Path }));
builder.AddRssFeed(static services => services
    .GetRequiredService<ContentCollection<MarkdownContent<PostFrontMatter>>>()
    .OrderByDescending(static post => post.FrontMatter.CreatedAt)
    .Select(static post => new FeedItem(
        post.FrontMatter.Title ?? string.Empty,
        post.FrontMatter.Description ?? string.Empty,
        post.FrontMatter.CreatedAt ?? DateTimeOffset.UnixEpoch,
        RelativePath: $"blog/{PostSlug.From(post.Path)}/")));
builder.AddSitemap();
await builder.Build().RunAsync();
"""

PROJECT = """\
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>Kiji.SyntheticSite</AssemblyName>
    <RootNamespace>Kiji.SyntheticSite</RootNamespace>
    <UseCurrentRuntimeIdentifier>true</UseCurrentRuntimeIdentifier>
    <InvariantGlobalization>true</InvariantGlobalization>
    <InvariantTimezone>true</InvariantTimezone>
    <DefaultItemExcludes>$(DefaultItemExcludes);contents/**</DefaultItemExcludes>
    <RestorePackagesPath>{packages}</RestorePackagesPath>
    <RestoreAdditionalProjectSources>{feed}</RestoreAdditionalProjectSources>
  </PropertyGroup>
  <ItemGroup><PackageReference Include="Kiji" Version="{version}" /></ItemGroup>
</Project>
"""

SITE_SOURCES = ["MainLayout.cs", "PageHead.cs", "PostFrontMatter.cs", "PostSlug.cs"]

# A startup hook that reports Kiji's build phases, as wall-clock offsets from process start.
# Both revisions report through the same internal observer, so the hook needs no Kiji reference.
PHASE_HOOK = """\
using System;
using System.Diagnostics;
using System.Reflection;

internal static class StartupHook
{
    public static void Initialize()
    {
        var start = Process.GetCurrentProcess().StartTime;
        double Now() => (DateTime.Now - start).TotalMilliseconds;
        Console.Error.WriteLine($"KIJI_PHASE hook 0 {Now():F1}");
        AppDomain.CurrentDomain.AssemblyLoad += (_, e) =>
        {
            if (e.LoadedAssembly.GetName().Name != "Kiji") { return; }
            var observer = e.LoadedAssembly.GetType("Kiji.Generation.BuildPhaseTimer")?
                .GetProperty("Observer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            observer?.SetValue(null, new Action<string, TimeSpan>((name, elapsed) =>
                Console.Error.WriteLine($"KIJI_PHASE {name} {elapsed.TotalMilliseconds:F1} {Now():F1}")));
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            Console.Error.WriteLine($"KIJI_PHASE exit 0 {Now():F1}");
            // Compilation the process still does, summed over threads: what precompiling could remove.
            Console.Error.WriteLine($"KIJI_STAT jit-ms {System.Runtime.JitInfo.GetCompilationTime().TotalMilliseconds:F1}");
            Console.Error.WriteLine($"KIJI_STAT jit-methods {System.Runtime.JitInfo.GetCompiledMethodCount()}");
        };
    }
}
"""

HOOK_PROJECT = """\
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>KijiPhaseHook</AssemblyName></PropertyGroup>
</Project>
"""


def run(command, cwd, quiet=True):
    result = subprocess.run(command, cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    if result.returncode != 0:
        sys.exit(f"{' '.join(map(str, command))} failed in {cwd}:\n{result.stdout[-4000:]}")
    return result.stdout


def pack(source, feed):
    run(["dotnet", "pack", "src/Kiji", "-c", "Release", "-o", str(feed)], source)
    # Since runtime packages exist, Kiji.Runtime.<rid> packages sit beside Kiji's.
    package = next(path for path in feed.glob("Kiji.*.nupkg") if not path.name.startswith("Kiji.Runtime."))
    return package.name[len("Kiji."):-len(".nupkg")]


def create_site(site, repository, corpus, feed, version, packages):
    shutil.copytree(corpus / "contents", site / "contents")
    shutil.copytree(corpus / "wwwroot", site / "wwwroot")
    source = repository / "src" / "Kiji.SyntheticSite"
    shutil.copytree(source / "Pages", site / "Pages")
    for name in SITE_SOURCES:
        shutil.copy(source / name, site / name)
    (site / "Program.cs").write_text(PROGRAM, encoding="utf-8")
    (site / "Site.csproj").write_text(
        PROJECT.format(packages=packages, feed=feed, version=version), encoding="utf-8")
    run(["dotnet", "publish", "-c", "Release", "-o", "dist"], site)


def build_directory(site, kind):
    # The site builds for the current runtime, into <kind>/Release/net10.0/<rid>/.
    return next((site / kind / "Release" / "net10.0").glob("*/Kiji.SyntheticSite.dll")).parent


def staging(site):
    return build_directory(site, "obj") / "kiji"


def generate(site, hook=None):
    command = ["dotnet", "exec", "--runtimeconfig", str(staging(site) / "publish.runtimeconfig.json"),
               str(build_directory(site, "bin") / "Kiji.SyntheticSite.dll")]
    environment = None
    if hook is not None:
        environment = dict(os.environ, DOTNET_STARTUP_HOOKS=str(hook))
    start = time.perf_counter()
    result = subprocess.run(command, cwd=site, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, text=True, env=environment)
    elapsed = (time.perf_counter() - start) * 1000
    if result.returncode != 0:
        sys.exit(f"Generation failed in {site}:\n{result.stderr[-4000:]}")
    return elapsed, parse_phases(result.stderr)


def parse_phases(stderr):
    """Top-level phase durations, plus the time before the first and after the last."""
    marks = [line.split()[1:] for line in stderr.splitlines() if line.startswith("KIJI_PHASE ")]
    if not marks:
        return {}
    phases = {}
    for line in stderr.splitlines():
        if line.startswith("KIJI_STAT "):
            _, name, value = line.split()
            phases[name] = float(value)
    ends = [(name, float(duration), float(at)) for name, duration, at in marks]
    timed = [mark for mark in ends if mark[0] not in ("hook", "exit") and "." not in mark[0]]
    if timed:
        first = timed[0]
        phases["start"] = first[2] - first[1]
        for name, duration, _ in timed:
            phases[name] = phases.get(name, 0) + duration
        exits = [at for name, _, at in ends if name == "exit"]
        if exits:
            phases["exit"] = exits[-1] - timed[-1][2]
    return phases


def build_hook(work):
    directory = work / "phase-hook"
    directory.mkdir()
    (directory / "StartupHook.cs").write_text(PHASE_HOOK, encoding="utf-8")
    (directory / "KijiPhaseHook.csproj").write_text(HOOK_PROJECT, encoding="utf-8")
    run(["dotnet", "build", "-c", "Release", "-o", str(directory / "out")], directory)
    return directory / "out" / "KijiPhaseHook.dll"


def remove(path):
    if path.is_dir():
        shutil.rmtree(path)
    elif path.exists():
        path.unlink()


def prepare(site, scenario, round_index):
    post = site / "contents" / "post-00000" / "index.md"
    original = site / "post-00000.original"
    if not original.exists():
        shutil.copy(post, original)
    if scenario in ("fresh", "profiled"):
        # A full generation: nothing to reuse. Fresh also has no JIT profile, as a clean checkout.
        remove(site / ".kiji" / "cache")
        remove(staging(site) / "site")
        if scenario == "fresh":
            remove(site / ".kiji")
            for profile in staging(site).glob("*.jitprofile*"):
                remove(profile)
            remove(staging(site) / "jit-session")
    elif scenario == "one-edited":
        # Equal-length edits, so every round changes exactly one input.
        edit = "A" if round_index % 2 == 0 else "B"
        post.write_text(original.read_text(encoding="utf-8") + f"\n\nEdit {edit}.\n", encoding="utf-8")


def restore(site):
    original = site / "post-00000.original"
    if original.exists():
        shutil.copy(original, site / "contents" / "post-00000" / "index.md")


def output_digest(site):
    digest = hashlib.sha256()
    root = staging(site) / "site"
    for path in sorted(root.rglob("*")):
        if path.is_file():
            digest.update(path.relative_to(root).as_posix().encode())
            digest.update(path.read_bytes())
    return digest.hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--baseline", required=True, help="Git revision to compare the working tree with.")
    parser.add_argument("--work", required=True, help="New directory for packages, consumers and results.")
    parser.add_argument("--pages", type=int, default=1000)
    parser.add_argument("--rounds", type=int, default=12, help="Measured rounds per scenario.")
    parser.add_argument("--phases", action="store_true",
                        help="Also report median build phases, through a startup hook in every run.")
    arguments = parser.parse_args()

    repository = pathlib.Path.cwd()
    work = pathlib.Path(arguments.work).resolve()
    work.mkdir(parents=True)
    # A work directory inside the repository must not inherit its build settings.
    (work / "Directory.Build.props").write_text("<Project />", encoding="utf-8")
    (work / "Directory.Build.targets").write_text("<Project />", encoding="utf-8")
    (work / "Directory.Packages.props").write_text(
        "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>",
        encoding="utf-8")
    baseline_source = work / "baseline-source"
    run(["git", "worktree", "add", "--detach", str(baseline_source), arguments.baseline], repository)

    variants = {}
    for name, source in (("baseline", baseline_source), ("candidate", repository)):
        feed = work / f"feed-{name}"
        variants[name] = (feed, pack(source, feed))
        print(f"{name}: Kiji {variants[name][1]}", flush=True)

    corpus = work / "corpus"
    run(["dotnet", "run", "-c", "Release", "--project", "src/Kiji.SyntheticSite", "--",
         "--pages", str(arguments.pages), "--runs", "1", "--root", str(corpus)], repository)

    sites = {}
    for name, (feed, version) in variants.items():
        sites[name] = work / f"site-{name}"
        sites[name].mkdir()
        create_site(sites[name], repository, corpus, feed, version, work / f"packages-{name}")

    if output_digest(sites["baseline"]) != output_digest(sites["candidate"]):
        sys.exit("The two packages generate different output.")
    print("output: identical", flush=True)
    hook = build_hook(work) if arguments.phases else None

    # Discarded rounds let each state settle: the profiled state records its profile first.
    warmup = {"fresh": 1, "profiled": 3, "no-change": 2, "one-edited": 2}
    print(f"{'scenario':<12}{'baseline':>10}{'candidate':>11}{'change':>9}   (median ms, {arguments.rounds} rounds)")
    for scenario in ("fresh", "profiled", "no-change", "one-edited"):
        samples = {name: [] for name in sites}
        phase_samples = {name: {} for name in sites}
        previous = None
        for round_index in range(warmup[scenario] + arguments.rounds):
            digests = {}
            for name, site in sites.items():
                prepare(site, scenario, round_index)
                elapsed, phases = generate(site, hook)
                # Outside the measured process: a round that skipped generating is no speedup.
                digests[name] = output_digest(site)
                if round_index >= warmup[scenario]:
                    samples[name].append(elapsed)
                    for phase, duration in phases.items():
                        phase_samples[name].setdefault(phase, []).append(duration)
            if digests["baseline"] != digests["candidate"]:
                sys.exit(f"The two packages generate different output ({scenario}, round {round_index + 1}).")
            # Edits alternate, so every round's output differs from the round before.
            if scenario == "one-edited" and digests["baseline"] == previous:
                sys.exit(f"The edit did not change the output ({scenario}, round {round_index + 1}).")
            previous = digests["baseline"]
        for site in sites.values():
            restore(site)
        baseline = statistics.median(samples["baseline"])
        candidate = statistics.median(samples["candidate"])
        print(f"{scenario:<12}{baseline:>10.1f}{candidate:>11.1f}{(candidate / baseline - 1) * 100:>8.1f}%", flush=True)
        for phase in phase_samples["candidate"]:
            if phase in phase_samples["baseline"]:
                before = statistics.median(phase_samples["baseline"][phase])
                after = statistics.median(phase_samples["candidate"][phase])
                print(f"  {phase:<10}{before:>10.1f}{after:>11.1f}", flush=True)


if __name__ == "__main__":
    main()
