# Benchmark run of 2026-11-09

Every benchmark in `PerformanceTest`, 101 cases in one BenchmarkDotNet session of 45 minutes, and the memory report,
run on 2026-11-09 at commit `0a4e35b`. It led to three changes, measured under
[Changes after the first run](../../Architecture.md#changes-after-the-first-run), and to a
[second run](../2026-11-09-after-fixes/README.md) with them, which
[Architecture.md](../../Architecture.md#latest-full-run) summarizes.

| | |
| --- | --- |
| Processor | AMD Ryzen 7 7800X3D, 8 cores and 16 threads at 4.2 GHz, 96 MB L3 cache |
| Operating system | Windows 11 25H2, build 26200 |
| Runtime | .NET 10.0.14, SDK 10.0.403, RyuJIT x86-64-v4 |
| BenchmarkDotNet | 0.15.8 |
| Real tables | CAIDA RouteViews `routeviews-rv2-20250930-2200.pfx2as` and `routeviews-rv6-20250930-0200.pfx2as` |

Each benchmark class has two reports: `-report-github.md`, the summary table, and `-report.csv`, every column of it.
`memory-report.txt` is the output of `--memory`.

To repeat the run:

```sh
Prototypes/GenericTrie/download-routeviews.sh /tmp/routeviews
ROUTEVIEWS_DIR=/tmp/routeviews dotnet run -c Release --project PerformanceTest -- --filter '*'
ROUTEVIEWS_DIR=/tmp/routeviews dotnet run -c Release --project PerformanceTest -- --memory
```
