namespace EVBlocker.App.Services;

/// <summary>
/// Keeps one interactive EVBlocker per sign-in session, and lets a second launch bring the first
/// one's window back instead of starting a copy.
/// </summary>
/// <remarks>
/// Needed once closing the window stops meaning exit: the app lives on in the tray, so opening
/// the shortcut again would otherwise start a second process with a second tray icon, both
/// polling connections and both able to write the firewall. The Local\ namespace scopes both
/// kernel objects to the session, so another user signed in on the same machine keeps their own.
/// </remarks>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\EVBlocker.Instance";

    private const string ShowEventName = @"Local\EVBlocker.Show";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showRequest;
    private readonly RegisteredWaitHandle _registration;

    private SingleInstance(Mutex mutex, EventWaitHandle showRequest, Action onShowRequested)
    {
        _mutex = mutex;
        _showRequest = showRequest;

        // A thread-pool wait rather than a dedicated thread; the callback runs off the UI thread,
        // so the caller marshals it.
        _registration = ThreadPool.RegisterWaitForSingleObject(
            showRequest, (_, _) => onShowRequested(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>
    /// Claims the session for this process, or hands over to the instance already running.
    /// </summary>
    /// <param name="wait">
    /// How long to wait for a running instance to exit. Zero for an ordinary launch; longer for
    /// the elevated copy started by "run as administrator", which is launched while the
    /// unelevated one is still shutting down.
    /// </param>
    /// <param name="onShowRequested">Called, off the UI thread, when a later launch asks for the window.</param>
    /// <param name="handedOver">
    /// True when a running instance was asked to show itself. False when one exists but could not
    /// be reached: an elevated instance's objects carry a high integrity label, which an
    /// unelevated process is not allowed to signal.
    /// </param>
    /// <returns>The claim, or null when another instance holds the session.</returns>
    public static SingleInstance? TryClaim(TimeSpan wait, Action onShowRequested, out bool handedOver)
    {
        handedOver = false;

        Mutex mutex;
        try
        {
            mutex = new Mutex(false, MutexName);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        bool owned;
        try
        {
            owned = mutex.WaitOne(wait);
        }
        catch (AbandonedMutexException)
        {
            // The previous owner died without releasing it - a crash or a kill. Ownership has
            // passed to this thread, and there is no one left to hand over to.
            owned = true;
        }

        if (!owned)
        {
            mutex.Dispose();
            handedOver = TrySignalRunningInstance();
            return null;
        }

        var showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        return new SingleInstance(mutex, showRequest, onShowRequested);
    }

    public void Dispose()
    {
        _registration.Unregister(null);
        _showRequest.Dispose();

        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned by the calling thread. Closing the handle below still frees it for the
            // next launch, which sees it as abandoned.
        }

        _mutex.Dispose();
    }

    private static bool TrySignalRunningInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out EventWaitHandle? showRequest))
            {
                using (showRequest)
                {
                    return showRequest.Set();
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
        }

        return false;
    }
}
