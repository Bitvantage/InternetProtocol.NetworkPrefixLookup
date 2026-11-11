/*
   Bitvantage.InternetProtocol.NetworkPrefixLookup
   Copyright (C) Michael Crino

   Licensed under the MIT License. See LICENSE.md in the repository root.
*/

using System.Collections.Concurrent;

namespace Test.Core;

/// <summary>Runs work one item at a time on a thread of its own, so that a test can keep a batch on the thread that started it.</summary>
public sealed class DedicatedThread : IDisposable
{
    private readonly BlockingCollection<Action> _work = new();
    private readonly Thread _thread;

    public DedicatedThread()
    {
        _thread = new Thread(() =>
        {
            foreach (var item in _work.GetConsumingEnumerable())
                item();
        }) { IsBackground = true };
        _thread.Start();
    }

    /// <summary>Runs a function on the thread and returns its result, or rethrows its exception.</summary>
    public T Run<T>(Func<T> function)
    {
        var result = new TaskCompletionSource<T>();
        _work.Add(() =>
        {
            try
            {
                result.SetResult(function());
            }
            catch (Exception exception)
            {
                result.SetException(exception);
            }
        });
        return result.Task.GetAwaiter().GetResult();
    }

    /// <summary>Runs an action on the thread, or rethrows its exception.</summary>
    public void Run(Action action) => Run(() =>
    {
        action();
        return 0;
    });

    public void Dispose()
    {
        _work.CompleteAdding();
        _thread.Join();
    }
}
