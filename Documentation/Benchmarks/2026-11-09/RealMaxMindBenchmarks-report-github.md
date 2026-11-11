```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9711/25H2/2025Update/HudsonValley2)
AMD Ryzen 7 7800X3D 4.20GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4
  Job-BFKSRR : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4


```
| Method           | Job        | IterationCount | Mean       | Error     | StdDev    | Ratio | RatioSD | Gen0       | Gen1       | Allocated    | Alloc Ratio |
|----------------- |----------- |--------------- |-----------:|----------:|----------:|------:|--------:|-----------:|-----------:|-------------:|------------:|
| Reader_Find      | DefaultJob | Default        |  55.149 ms | 0.8447 ms | 0.7488 ms |  1.00 |    0.02 |  1333.3333 |          - |   68800000 B |        1.00 |
| Lookup_ReadWrite | DefaultJob | Default        |   9.936 ms | 0.1914 ms | 0.2351 ms |  0.18 |    0.00 |          - |          - |            - |        0.00 |
| Lookup_Frozen    | DefaultJob | Default        |   5.622 ms | 0.0930 ms | 0.0776 ms |  0.10 |    0.00 |          - |          - |            - |        0.00 |
|                  |            |                |            |           |           |       |         |            |            |              |             |
| Load             | Job-BFKSRR | 10             | 994.976 ms | 9.0759 ms | 4.7469 ms |     ? |       ? | 21000.0000 | 13000.0000 | 1218134432 B |           ? |
