```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
AMD Ryzen 7 7800X3D 4.20GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


```
| Method           | Mean         | Error       | StdDev      | Ratio | RatioSD | Gen0      | Gen1   | Allocated   | Alloc Ratio |
|----------------- |-------------:|------------:|------------:|------:|--------:|----------:|-------:|------------:|------------:|
| Reader_Find      | 209,098.4 μs | 3,583.74 μs | 3,176.89 μs | 1.000 |    0.02 | 9333.3333 |      - | 468686544 B |       1.000 |
| Lookup_ReadWrite |   2,747.8 μs |    20.39 μs |    18.08 μs | 0.013 |    0.00 |         - |      - |           - |       0.000 |
| Lookup_Frozen    |   1,460.1 μs |     5.07 μs |     4.23 μs | 0.007 |    0.00 |         - |      - |           - |       0.000 |
| Load             |     240.7 μs |     1.27 μs |     1.13 μs | 0.001 |    0.00 |   18.3105 | 7.0801 |    927528 B |       0.002 |
