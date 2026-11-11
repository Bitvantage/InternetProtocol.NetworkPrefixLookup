```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
AMD Ryzen 7 7800X3D 4.20GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


```
| Method               | Prefixes | Mean     | Error     | StdDev    | Ratio | RatioSD |
|--------------------- |--------- |---------:|----------:|----------:|------:|--------:|
| **LeafPushed_FullRoot**  | **64**       | **1.188 ms** | **0.0235 ms** | **0.0196 ms** |  **1.00** |    **0.02** |
| LeafPushed_SizedRoot | 64       | 1.189 ms | 0.0127 ms | 0.0113 ms |  1.00 |    0.02 |
| Compressed_FullRoot  | 64       | 1.540 ms | 0.0176 ms | 0.0138 ms |  1.30 |    0.02 |
| Compressed_SizedRoot | 64       | 1.647 ms | 0.0176 ms | 0.0165 ms |  1.39 |    0.03 |
|                      |          |          |           |           |       |         |
| **LeafPushed_FullRoot**  | **256**      | **5.389 ms** | **0.0313 ms** | **0.0293 ms** |  **1.00** |    **0.01** |
| LeafPushed_SizedRoot | 256      | 5.563 ms | 0.0291 ms | 0.0272 ms |  1.03 |    0.01 |
| Compressed_FullRoot  | 256      | 5.989 ms | 0.0449 ms | 0.0420 ms |  1.11 |    0.01 |
| Compressed_SizedRoot | 256      | 4.987 ms | 0.0309 ms | 0.0289 ms |  0.93 |    0.01 |
|                      |          |          |           |           |       |         |
| **LeafPushed_FullRoot**  | **1000**     | **4.816 ms** | **0.0363 ms** | **0.0322 ms** |  **1.00** |    **0.01** |
| LeafPushed_SizedRoot | 1000     | 4.792 ms | 0.0359 ms | 0.0318 ms |  1.00 |    0.01 |
| Compressed_FullRoot  | 1000     | 5.568 ms | 0.0380 ms | 0.0355 ms |  1.16 |    0.01 |
| Compressed_SizedRoot | 1000     | 5.095 ms | 0.0626 ms | 0.0586 ms |  1.06 |    0.01 |
|                      |          |          |           |           |       |         |
| **LeafPushed_FullRoot**  | **4000**     | **2.698 ms** | **0.0251 ms** | **0.0235 ms** |  **1.00** |    **0.01** |
| LeafPushed_SizedRoot | 4000     | 2.722 ms | 0.0323 ms | 0.0302 ms |  1.01 |    0.01 |
| Compressed_FullRoot  | 4000     | 2.114 ms | 0.0099 ms | 0.0093 ms |  0.78 |    0.01 |
| Compressed_SizedRoot | 4000     | 2.795 ms | 0.0530 ms | 0.0825 ms |  1.04 |    0.03 |
