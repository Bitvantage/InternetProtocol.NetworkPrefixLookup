```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
AMD Ryzen 7 7800X3D 4.20GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


```
| Method        | Family | Frozen | Mean       | Error     | StdDev    | Ratio | RatioSD | Gen0     | Allocated  | Alloc Ratio |
|-------------- |------- |------- |-----------:|----------:|----------:|------:|--------:|---------:|-----------:|------------:|
| **TryGetMatch**   | **4**      | **False**  |   **3.202 ms** | **0.0294 ms** | **0.0275 ms** |  **1.00** |    **0.01** |        **-** |          **-** |          **NA** |
| Overlaps      | 4      | False  |   2.654 ms | 0.0185 ms | 0.0145 ms |  0.83 |    0.01 |        - |          - |          NA |
| GetMatches    | 4      | False  |   7.266 ms | 0.0506 ms | 0.0473 ms |  2.27 |    0.02 | 304.6875 | 15325472 B |          NA |
| GetSubnetsAny | 4      | False  |   8.959 ms | 0.1771 ms | 0.1657 ms |  2.80 |    0.06 | 359.3750 | 18658640 B |          NA |
| GetSubnetsAll | 4      | False  | 130.704 ms | 1.0417 ms | 0.8133 ms | 40.83 |    0.42 | 250.0000 | 19059320 B |          NA |
|               |        |        |            |           |           |       |         |          |            |             |
| **TryGetMatch**   | **4**      | **True**   |   **2.379 ms** | **0.0460 ms** | **0.0492 ms** |  **1.00** |    **0.03** |        **-** |          **-** |          **NA** |
| Overlaps      | 4      | True   |   1.021 ms | 0.0107 ms | 0.0095 ms |  0.43 |    0.01 |        - |          - |          NA |
| GetMatches    | 4      | True   |   5.086 ms | 0.0809 ms | 0.0757 ms |  2.14 |    0.05 | 234.3750 | 11814872 B |          NA |
| GetSubnetsAny | 4      | True   |   9.049 ms | 0.1436 ms | 0.1273 ms |  3.80 |    0.09 | 171.8750 |  8800000 B |          NA |
| GetSubnetsAll | 4      | True   |  29.507 ms | 0.3161 ms | 0.2957 ms | 12.41 |    0.27 | 156.2500 |  8800000 B |          NA |
|               |        |        |            |           |           |       |         |          |            |             |
| **TryGetMatch**   | **6**      | **False**  |   **6.644 ms** | **0.0493 ms** | **0.0411 ms** |  **1.00** |    **0.01** |        **-** |          **-** |          **NA** |
| Overlaps      | 6      | False  |   4.693 ms | 0.0385 ms | 0.0360 ms |  0.71 |    0.01 |        - |          - |          NA |
| GetMatches    | 6      | False  |  10.230 ms | 0.0715 ms | 0.0669 ms |  1.54 |    0.01 | 296.8750 | 15363176 B |          NA |
| GetSubnetsAny | 6      | False  |  11.819 ms | 0.0854 ms | 0.0714 ms |  1.78 |    0.01 | 421.8750 | 21910240 B |          NA |
| GetSubnetsAll | 6      | False  |  53.079 ms | 1.0031 ms | 0.8892 ms |  7.99 |    0.14 | 400.0000 | 22692504 B |          NA |
|               |        |        |            |           |           |       |         |          |            |             |
| **TryGetMatch**   | **6**      | **True**   |   **4.251 ms** | **0.0458 ms** | **0.0383 ms** |  **1.00** |    **0.01** |        **-** |          **-** |          **NA** |
| Overlaps      | 6      | True   |   3.182 ms | 0.0236 ms | 0.0209 ms |  0.75 |    0.01 |        - |          - |          NA |
| GetMatches    | 6      | True   |   7.439 ms | 0.0500 ms | 0.0417 ms |  1.75 |    0.02 | 250.0000 | 12683240 B |          NA |
| GetSubnetsAny | 6      | True   |   9.383 ms | 0.0790 ms | 0.0739 ms |  2.21 |    0.03 | 250.0000 | 12800000 B |          NA |
| GetSubnetsAll | 6      | True   |  16.748 ms | 0.1549 ms | 0.1293 ms |  3.94 |    0.04 | 250.0000 | 12800000 B |          NA |
