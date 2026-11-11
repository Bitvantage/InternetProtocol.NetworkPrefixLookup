```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
AMD Ryzen 7 7800X3D 4.20GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


```
| Method                    | Categories | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|-------------------------- |----------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| IPv4_ReadWrite            | IPv4       | 22.655 ms | 0.2761 ms | 0.2711 ms |  1.00 |    0.02 |         - |          NA |
| IPv4_LeafPushed           | IPv4       |  7.970 ms | 0.1262 ms | 0.1119 ms |  0.35 |    0.01 |         - |          NA |
| IPv4_Compressed           | IPv4       | 11.106 ms | 0.1891 ms | 0.1676 ms |  0.49 |    0.01 |         - |          NA |
| IPv4_LeafPushed_ValueOnly | IPv4       |  6.309 ms | 0.0500 ms | 0.0443 ms |  0.28 |    0.00 |         - |          NA |
| IPv4_ReadWrite_IPAddress  | IPv4       | 26.346 ms | 0.5238 ms | 0.5379 ms |  1.16 |    0.03 |         - |          NA |
|                           |            |           |           |           |       |         |           |             |
| IPv6_ReadWrite            | IPv6       | 54.230 ms | 0.7411 ms | 0.6188 ms |  1.00 |    0.02 |         - |          NA |
| IPv6_LeafPushed           | IPv6       | 23.710 ms | 0.1813 ms | 0.1696 ms |  0.44 |    0.01 |         - |          NA |
| IPv6_Compressed           | IPv6       | 29.601 ms | 0.2402 ms | 0.2247 ms |  0.55 |    0.01 |         - |          NA |
| IPv6_LeafPushed_ValueOnly | IPv6       | 21.901 ms | 0.2519 ms | 0.2233 ms |  0.40 |    0.01 |         - |          NA |
| IPv6_ReadWrite_IPAddress  | IPv6       | 59.373 ms | 0.9439 ms | 0.8829 ms |  1.09 |    0.02 |         - |          NA |
