using System.Collections.Concurrent;

namespace Kiji.Generation;

/// <summary>
/// Persists rendered pages on threads of its own, so rendering keeps the processors busy while
/// the file system works. Creating, comparing and closing a file waits on the file system, and
/// on Windows on the scan that follows each new file; on the rendering threads that wait left
/// processors idle for as long as the rendering took.
/// </summary>
internal sealed class PageOutputWriter : IDisposable
{
    private readonly BlockingCollection<(Func<bool> Persist, TaskCompletionSource<bool> Result)> _queue = [];
    private readonly ConcurrentDictionary<string, byte> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _failed;
    private readonly Thread[] _threads;

    /// <param name="cancellationToken">Stops writing pages that have not started.</param>
    internal PageOutputWriter(CancellationToken cancellationToken)
    {
        _failed = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Writers mostly wait, so one per processor overlaps them with rendering on all of them.
        _threads = new Thread[Math.Max(2, Environment.ProcessorCount)];
        for (var i = 0; i < _threads.Length; i++)
        {
            _threads[i] = new Thread(Run) { IsBackground = true, Name = "Kiji page writer" };
            _threads[i].Start();
        }
    }

    /// <summary>Canceled when the build is canceled or a page fails to persist.</summary>
    internal CancellationToken Stopping => _failed.Token;

    /// <summary>Queues a page; the task reports whether its bytes were written.</summary>
    internal Task<bool> Enqueue(string fullPath, byte[] html, Func<bool> mustWrite)
    {
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add((() =>
        {
            if (!mustWrite()) { return false; }
            var directory = Path.GetDirectoryName(fullPath)!;
            // Recorded only once it exists, so a page racing for the same directory creates it too.
            if (!_directories.ContainsKey(directory))
            {
                Directory.CreateDirectory(directory);
                _directories.TryAdd(directory, 0);
            }
            using var handle = File.OpenHandle(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
            RandomAccess.Write(handle, html, fileOffset: 0);
            return true;
        }, result));
        return result.Task;
    }

    /// <summary>Lets queued pages finish and stops the writers.</summary>
    public void Dispose()
    {
        _queue.CompleteAdding();
        foreach (var thread in _threads) { thread.Join(); }
        _queue.Dispose();
        _failed.Dispose();
    }

    private void Run()
    {
        foreach (var (persist, result) in _queue.GetConsumingEnumerable())
        {
            if (_failed.IsCancellationRequested)
            {
                result.SetCanceled(_failed.Token);
                continue;
            }
            try
            {
                result.SetResult(persist());
            }
#pragma warning disable CA1031 // Reported through the page's task, which the build awaits.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                result.SetException(exception);
                // Rendering further pages cannot make this build succeed.
                _failed.Cancel();
            }
        }
    }
}
