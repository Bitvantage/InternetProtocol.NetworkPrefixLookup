```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
AMD Ryzen 7 7800X3D 4.20GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


```
| Method  | Mean     | Error    | StdDev   | Ratio | RatioSD |
|-------- |---------:|---------:|---------:|------:|--------:|
| Core    | 26.69 ms | 0.523 ms | 0.750 ms |  1.00 |    0.04 |
| IPLayer | 28.22 ms | 0.462 ms | 0.432 ms |  1.06 |    0.03 |
