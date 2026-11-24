using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Kiji.Benchmarks.FrontMatterParserBenchmarks).Assembly).Run(args);
