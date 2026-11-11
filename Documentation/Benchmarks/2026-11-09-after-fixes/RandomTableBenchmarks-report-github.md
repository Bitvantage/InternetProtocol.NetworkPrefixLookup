```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
AMD Ryzen 7 7800X3D 4.20GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  Job-OWGQXZ : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4

IterationCount=30  

```
| Method                               | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0      | Gen1      | Gen2     | Allocated   | Alloc Ratio |
|------------------------------------- |----------:|----------:|----------:|------:|--------:|----------:|----------:|---------:|------------:|------------:|
| Lookup_IPAddress_1M                  | 24.786 ms | 0.1630 ms | 0.2390 ms |  1.00 |    0.01 |         - |         - |        - |           - |          NA |
| Lookup_IPAddress_1M_Frozen           | 18.443 ms | 0.3657 ms | 0.5360 ms |  0.74 |    0.02 |         - |         - |        - |           - |          NA |
| Lookup_IPAddress_1M_FrozenCompressed | 11.518 ms | 0.1087 ms | 0.1593 ms |  0.46 |    0.01 |         - |         - |        - |           - |          NA |
| Lookup_UInt32_1M                     | 22.124 ms | 0.1142 ms | 0.1675 ms |  0.89 |    0.01 |         - |         - |        - |           - |          NA |
| Lookup_UInt32_1M_Frozen              |  8.339 ms | 0.0980 ms | 0.1437 ms |  0.34 |    0.01 |         - |         - |        - |           - |          NA |
| Add_100K                             | 80.959 ms | 0.9337 ms | 1.3090 ms |  3.27 |    0.06 | 5285.7143 | 2142.8571 | 142.8571 | 261200909 B |          NA |
| Add_100K_Batch                       | 14.095 ms | 0.0864 ms | 0.1266 ms |  0.57 |    0.01 |  390.6250 |  234.3750 |        - |  19983056 B |          NA |
| Freeze_100K                          | 15.293 ms | 0.0699 ms | 0.1002 ms |  0.62 |    0.01 |  312.5000 |  312.5000 | 312.5000 | 111318784 B |          NA |
