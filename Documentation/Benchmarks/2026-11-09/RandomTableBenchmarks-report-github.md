```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
AMD Ryzen 7 7800X3D 4.20GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  Job-OWGQXZ : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4

IterationCount=30  

```
| Method                               | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0      | Gen1      | Gen2     | Allocated   | Alloc Ratio |
|------------------------------------- |---------:|---------:|---------:|------:|--------:|----------:|----------:|---------:|------------:|------------:|
| Lookup_IPAddress_1M                  | 26.02 ms | 0.299 ms | 0.419 ms |  1.00 |    0.02 |         - |         - |        - |           - |          NA |
| Lookup_IPAddress_1M_Frozen           | 20.23 ms | 0.685 ms | 1.003 ms |  0.78 |    0.04 |         - |         - |        - |           - |          NA |
| Lookup_IPAddress_1M_FrozenCompressed | 10.56 ms | 0.389 ms | 0.570 ms |  0.41 |    0.02 |         - |         - |        - |           - |          NA |
| Lookup_UInt32_1M                     | 23.01 ms | 0.259 ms | 0.380 ms |  0.88 |    0.02 |         - |         - |        - |           - |          NA |
| Lookup_UInt32_1M_Frozen              | 10.00 ms | 0.500 ms | 0.685 ms |  0.38 |    0.03 |         - |         - |        - |           - |          NA |
| Add_100K                             | 94.95 ms | 1.269 ms | 1.899 ms |  3.65 |    0.09 | 5333.3333 | 2166.6667 | 166.6667 | 261201109 B |          NA |
| Add_100K_Batch                       | 15.25 ms | 0.172 ms | 0.246 ms |  0.59 |    0.01 |  390.6250 |  234.3750 |        - |  19983056 B |          NA |
| Freeze_100K                          | 15.51 ms | 0.323 ms | 0.484 ms |  0.60 |    0.02 |  281.2500 |  281.2500 | 281.2500 | 111292710 B |          NA |
