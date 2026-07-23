using System.Management;
using System.Diagnostics;

namespace NativeScreenDimmer_WinUI3.Services;

internal sealed class ProcessRuleWatcher : IDisposable
{
    private readonly Lock _gate = new();
    private ManagementEventWatcher? _processStartWatcher;
    private ManagementEventWatcher? _processStopWatcher;
    private HashSet<string> _watchedProcessNames = [];
    private HashSet<string> _runningProcessNames = [];
    private bool _disposed;

    public event EventHandler? ProcessStateChanged;

    public void UpdateWatchedProcessNames(IEnumerable<string> processNames)
    {
        HashSet<string> normalizedProcessNames = new(
            processNames.Where(processName => !string.IsNullOrWhiteSpace(processName)),
            StringComparer.OrdinalIgnoreCase);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_watchedProcessNames.SetEquals(normalizedProcessNames))
            {
                return;
            }

            _watchedProcessNames = normalizedProcessNames;
            if (normalizedProcessNames.Count == 0)
            {
                _runningProcessNames = [];
                StopWatchers();
                return;
            }

            EnsureWatchersStarted();
            SeedRunningProcessNames();
        }
    }

    public IReadOnlySet<string> GetRunningProcessNamesSnapshot()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return Volatile.Read(ref _runningProcessNames);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopWatchers();
        }
    }

    private void EnsureWatchersStarted()
    {
        if (_processStartWatcher is null)
        {
            _processStartWatcher = CreateWatcher("SELECT * FROM Win32_ProcessStartTrace");
            _processStartWatcher.EventArrived += ProcessEventArrived;
            TryStartWatcher(_processStartWatcher, "process start");
        }

        if (_processStopWatcher is null)
        {
            _processStopWatcher = CreateWatcher("SELECT * FROM Win32_ProcessStopTrace");
            _processStopWatcher.EventArrived += ProcessEventArrived;
            TryStartWatcher(_processStopWatcher, "process stop");
        }
    }

    private void SeedRunningProcessNames()
    {
        Process[] runningProcesses = Process.GetProcesses();
        try
        {
            HashSet<string> runningProcessNames = new(StringComparer.OrdinalIgnoreCase);
            foreach (Process process in runningProcesses)
            {
                string? processName = process.ProcessName;
                if (!string.IsNullOrWhiteSpace(processName))
                {
                    runningProcessNames.Add(processName);
                }
            }

            _runningProcessNames = runningProcessNames;
        }
        finally
        {
            foreach (Process process in runningProcesses)
            {
                process.Dispose();
            }
        }
    }

    private static ManagementEventWatcher CreateWatcher(string queryText)
    {
        return new ManagementEventWatcher(new WqlEventQuery(queryText));
    }

    private void TryStartWatcher(ManagementEventWatcher watcher, string watcherName)
    {
        try
        {
            watcher.Start();
        }
        catch (UnauthorizedAccessException exception)
        {
            AppLogger.LogException($"Unable to start {watcherName} watcher", exception);
            StopWatchers();
        }
        catch (ManagementException exception)
        {
            AppLogger.LogException($"Unable to start {watcherName} watcher", exception);
            StopWatchers();
        }
    }

    private void StopWatchers()
    {
        StopWatcher(ref _processStartWatcher);
        StopWatcher(ref _processStopWatcher);
    }

    private void StopWatcher(ref ManagementEventWatcher? watcher)
    {
        if (watcher is null)
        {
            return;
        }

        watcher.EventArrived -= ProcessEventArrived;
        try
        {
            watcher.Stop();
        }
        catch (InvalidOperationException exception)
        {
            AppLogger.LogException("Unable to stop process watcher", exception);
        }
        catch (ManagementException exception)
        {
            AppLogger.LogException("Unable to stop process watcher", exception);
        }
        finally
        {
            watcher.Dispose();
            watcher = null;
        }
    }

    private void ProcessEventArrived(object sender, EventArrivedEventArgs e)
    {
        bool stateChanged = false;
        string? processName = e.NewEvent?.Properties["ProcessName"]?.Value as string;
        if (string.IsNullOrWhiteSpace(processName))
        {
            return;
        }

        string normalizedProcessName = System.IO.Path.GetFileNameWithoutExtension(processName.Trim());
        if (string.IsNullOrWhiteSpace(normalizedProcessName))
        {
            return;
        }

        HashSet<string> watchedProcessNames = Volatile.Read(ref _watchedProcessNames);
        if (!watchedProcessNames.Contains(normalizedProcessName))
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            HashSet<string> updatedRunningProcessNames = new(_runningProcessNames, StringComparer.OrdinalIgnoreCase);
            string? eventClassName = e.NewEvent?.ClassPath?.ClassName;
            if (string.Equals(eventClassName, "Win32_ProcessStartTrace", StringComparison.OrdinalIgnoreCase))
            {
                stateChanged = updatedRunningProcessNames.Add(normalizedProcessName);
            }
            else if (string.Equals(eventClassName, "Win32_ProcessStopTrace", StringComparison.OrdinalIgnoreCase))
            {
                stateChanged = updatedRunningProcessNames.Remove(normalizedProcessName);
            }

            if (stateChanged)
            {
                _runningProcessNames = updatedRunningProcessNames;
            }
        }

        if (!stateChanged)
        {
            return;
        }

        ProcessStateChanged?.Invoke(this, EventArgs.Empty);
    }
}
