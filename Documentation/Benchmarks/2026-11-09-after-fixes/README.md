# Benchmark run of 2026-11-09, after the fixes

Every benchmark in `PerformanceTest`, 101 cases in one BenchmarkDotNet session of 43 minutes, and the memory report,
run on 2026-11-09 on commit `0a4e35b` with the three changes that the [first run](../2026-11-09/README.md) led to: the
frozen search through sampled keys, the inlined IPv6 address conversion, and `LayerBenchmarks` reading the trie inside
the lookup. [Architecture.md](../../Architecture.md#latest-full-run) summarizes the results, and
[Changes after the first run](../../Architecture.md#changes-after-the-first-run) measures each change in one session.

The machine and the data are those of the first run:

| | |
| --- | --- |
| Processor | AMD Ryzen 7 7800X3D, 8 cores and 16 threads at 4.2 GHz, 96 MB L3 cache |
| Operating system | Windows 11 25H2, build 26200 |
| Runtime | .NET 10.0.14, SDK 10.0.403, RyuJIT x86-64-v4 |
| BenchmarkDotNet | 0.15.8 |
| Real tables | CAIDA RouteViews `routeviews-rv2-20250930-2200.pfx2as` and `routeviews-rv6-20250930-0200.pfx2as` |

Between the two runs, benchmarks whose code did not change moved by up to 17%, most of them faster. Compare the two
runs only where the difference is far larger than that.

Each benchmark class has two reports: `-report-github.md`, the summary table, and `-report.csv`, every column of it.
`memory-report.txt` is the output of `--memory`.
