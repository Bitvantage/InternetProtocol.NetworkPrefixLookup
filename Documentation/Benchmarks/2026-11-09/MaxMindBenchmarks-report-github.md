```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
AMD Ryzen 7 7800X3D 4.20GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


```
| Method           | Mean         | Error       | StdDev      | Ratio | RatioSD | Gen0      | Gen1   | Allocated   | Alloc Ratio |
|----------------- |-------------:|------------:|------------:|------:|--------:|----------:|-------:|------------:|------------:|
| Reader_Find      | 221,828.9 μs | 4,042.59 μs | 3,583.65 μs | 1.000 |    0.02 | 9333.3333 |      - | 468686544 B |       1.000 |
| Lookup_ReadWrite |   3,440.0 μs |    56.08 μs |    49.72 μs | 0.016 |    0.00 |         - |      - |           - |       0.000 |
| Lookup_Frozen    |   2,172.1 μs |    10.61 μs |     9.41 μs | 0.010 |    0.00 |         - |      - |           - |       0.000 |
| Load             |     261.0 μs |     3.11 μs |     2.91 μs | 0.001 |    0.00 |   18.3105 | 7.0801 |    927528 B |       0.002 |
