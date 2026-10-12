using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace ScriptedScreensVector;

internal sealed class MainThreadWork
{
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private readonly ConcurrentQueue<(Func<string> Work, TaskCompletionSource<string> Result)> _pending = new();

    internal bool IsMainThread => Environment.CurrentManagedThreadId == _threadId;

    internal string Invoke(Func<string> work, int timeoutMilliseconds = 3000)
    {
        if (IsMainThread)
            return work();

        var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending.Enqueue((work, result));
        if (Task.WaitAny(new Task[] { result.Task }, timeoutMilliseconds) >= 0)
            return result.Task.GetAwaiter().GetResult();

        result.TrySetCanceled();
        return "vector_stats: game busy, try again";
    }

    internal void Pump()
    {
        while (_pending.TryDequeue(out var request))
        {
            if (request.Result.Task.IsCompleted)
                continue;
            try
            {
                request.Result.TrySetResult(request.Work());
            }
            catch (Exception ex)
            {
                request.Result.TrySetException(ex);
            }
        }
    }
}
