```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
AMD Ryzen 7 7800X3D 4.20GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


```
| Method                    | Categories | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|-------------------------- |----------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| IPv4_ReadWrite            | IPv4       | 24.325 ms | 0.4697 ms | 0.4613 ms |  1.00 |    0.03 |         - |          NA |
| IPv4_LeafPushed           | IPv4       |  8.889 ms | 0.1712 ms | 0.3900 ms |  0.37 |    0.02 |         - |          NA |
| IPv4_Compressed           | IPv4       | 11.791 ms | 0.1988 ms | 0.1859 ms |  0.48 |    0.01 |         - |          NA |
| IPv4_LeafPushed_ValueOnly | IPv4       |  6.453 ms | 0.0824 ms | 0.0771 ms |  0.27 |    0.01 |         - |          NA |
| IPv4_ReadWrite_IPAddress  | IPv4       | 28.847 ms | 0.5102 ms | 0.4772 ms |  1.19 |    0.03 |         - |          NA |
|                           |            |           |           |           |       |         |           |             |
| IPv6_ReadWrite            | IPv6       | 56.502 ms | 0.5910 ms | 0.5239 ms |  1.00 |    0.01 |         - |          NA |
| IPv6_LeafPushed           | IPv6       | 23.713 ms | 0.3409 ms | 0.3189 ms |  0.42 |    0.01 |         - |          NA |
| IPv6_Compressed           | IPv6       | 30.890 ms | 0.3367 ms | 0.2811 ms |  0.55 |    0.01 |         - |          NA |
| IPv6_LeafPushed_ValueOnly | IPv6       | 22.906 ms | 0.2691 ms | 0.2247 ms |  0.41 |    0.01 |         - |          NA |
| IPv6_ReadWrite_IPAddress  | IPv6       | 93.320 ms | 1.7805 ms | 1.5784 ms |  1.65 |    0.03 |         - |          NA |
