namespace COptimizer.Windows;

/// <summary>Runs work on a dedicated single-threaded-apartment thread (required by shell COM objects).</summary>
internal static class StaThread
{
    /// <summary>
    /// Runs <paramref name="work"/> on a new STA thread. If <paramref name="cancellationToken"/> is cancelled, the task
    /// completes as cancelled immediately; the work itself is expected to observe the token and stop on its own.
    /// </summary>
    public static Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

        var thread = new Thread(() =>
        {
            try
            {
                tcs.TrySetResult(work());
            }
            catch (Exception e)
            {
                tcs.TrySetException(e);
            }
            finally
            {
                registration.Dispose();
            }
        })
        {
            IsBackground = true,
            Name = "C_Optimizer STA worker",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}
