```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
AMD Ryzen 7 7800X3D 4.20GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


```
| Method       | Mean       | Error     | StdDev    | Gen0    | Gen1    | Allocated  |
|------------- |-----------:|----------:|----------:|--------:|--------:|-----------:|
| SmallBatch   |   9.650 μs | 0.1581 μs | 0.1479 μs |  2.0605 |  0.4297 |  101.38 KB |
| SingleWrites | 459.583 μs | 8.1285 μs | 7.2057 μs | 76.1719 | 38.0859 | 3774.32 KB |
