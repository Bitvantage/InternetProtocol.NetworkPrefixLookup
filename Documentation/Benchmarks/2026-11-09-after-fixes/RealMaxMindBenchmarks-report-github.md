```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
AMD Ryzen 7 7800X3D 4.20GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  Job-BFKSRR : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


```
| Method           | Job        | IterationCount | Mean       | Error      | StdDev    | Ratio | RatioSD | Gen0       | Gen1       | Allocated    | Alloc Ratio |
|----------------- |----------- |--------------- |-----------:|-----------:|----------:|------:|--------:|-----------:|-----------:|-------------:|------------:|
| Reader_Find      | DefaultJob | Default        |  50.734 ms |  0.3553 ms | 0.2967 ms |  1.00 |    0.01 |  1300.0000 |          - |   68800000 B |        1.00 |
| Lookup_ReadWrite | DefaultJob | Default        |   7.629 ms |  0.0519 ms | 0.0486 ms |  0.15 |    0.00 |          - |          - |            - |        0.00 |
| Lookup_Frozen    | DefaultJob | Default        |   5.351 ms |  0.0192 ms | 0.0161 ms |  0.11 |    0.00 |          - |          - |            - |        0.00 |
|                  |            |                |            |            |           |       |         |            |            |              |             |
| Load             | Job-BFKSRR | 10             | 872.446 ms | 11.0002 ms | 7.2760 ms |     ? |       ? | 21000.0000 | 13000.0000 | 1218134432 B |           ? |
