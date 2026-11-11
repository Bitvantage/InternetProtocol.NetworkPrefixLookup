```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
AMD Ryzen 7 7800X3D 4.20GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


```
| Method               | Prefixes | Mean     | Error     | StdDev    | Ratio | RatioSD |
|--------------------- |--------- |---------:|----------:|----------:|------:|--------:|
| **LeafPushed_FullRoot**  | **64**       | **1.126 ms** | **0.0064 ms** | **0.0053 ms** |  **1.00** |    **0.01** |
| LeafPushed_SizedRoot | 64       | 1.123 ms | 0.0106 ms | 0.0088 ms |  1.00 |    0.01 |
| Compressed_FullRoot  | 64       | 1.492 ms | 0.0179 ms | 0.0167 ms |  1.33 |    0.02 |
| Compressed_SizedRoot | 64       | 1.570 ms | 0.0120 ms | 0.0112 ms |  1.40 |    0.01 |
|                      |          |          |           |           |       |         |
| **LeafPushed_FullRoot**  | **256**      | **5.326 ms** | **0.0211 ms** | **0.0197 ms** |  **1.00** |    **0.01** |
| LeafPushed_SizedRoot | 256      | 5.350 ms | 0.0261 ms | 0.0244 ms |  1.00 |    0.01 |
| Compressed_FullRoot  | 256      | 6.051 ms | 0.1015 ms | 0.0950 ms |  1.14 |    0.02 |
| Compressed_SizedRoot | 256      | 5.005 ms | 0.0224 ms | 0.0209 ms |  0.94 |    0.01 |
|                      |          |          |           |           |       |         |
| **LeafPushed_FullRoot**  | **1000**     | **4.666 ms** | **0.0475 ms** | **0.0421 ms** |  **1.00** |    **0.01** |
| LeafPushed_SizedRoot | 1000     | 4.660 ms | 0.0213 ms | 0.0167 ms |  1.00 |    0.01 |
| Compressed_FullRoot  | 1000     | 5.227 ms | 0.0374 ms | 0.0331 ms |  1.12 |    0.01 |
| Compressed_SizedRoot | 1000     | 5.042 ms | 0.0255 ms | 0.0213 ms |  1.08 |    0.01 |
|                      |          |          |           |           |       |         |
| **LeafPushed_FullRoot**  | **4000**     | **2.608 ms** | **0.0161 ms** | **0.0142 ms** |  **1.00** |    **0.01** |
| LeafPushed_SizedRoot | 4000     | 2.621 ms | 0.0263 ms | 0.0246 ms |  1.00 |    0.01 |
| Compressed_FullRoot  | 4000     | 2.196 ms | 0.0148 ms | 0.0123 ms |  0.84 |    0.01 |
| Compressed_SizedRoot | 4000     | 2.673 ms | 0.0330 ms | 0.0292 ms |  1.02 |    0.01 |
