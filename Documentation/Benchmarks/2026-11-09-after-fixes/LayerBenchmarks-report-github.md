```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
AMD Ryzen 7 7800X3D 4.20GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


```
| Method  | Mean     | Error    | StdDev   | Ratio |
|-------- |---------:|---------:|---------:|------:|
| Core    | 23.47 ms | 0.157 ms | 0.139 ms |  1.00 |
| IPLayer | 23.78 ms | 0.293 ms | 0.274 ms |  1.01 |
