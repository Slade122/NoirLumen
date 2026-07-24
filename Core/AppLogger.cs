using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace NativeScreenDimmer_WinUI3.Services;

public static class AppLogger
{
    private const int PendingLineCapacity = 4096;
    private static readonly string LogFilePath = BuildLogFilePath();
    private static readonly BlockingCollection<string> PendingLines = new(new ConcurrentQueue<string>(), PendingLineCapacity);
    private static readonly Task WriterTask = Task.Run(WriterLoop);
    private static int _droppedLineCount;

    public static void LogInfo(string message) => Write("INFO", message);

    public static void LogWarning(string message) => Write("WARN", message);

    public static void LogError(string message) => Write("ERROR", message);

    public static void LogException(string context, Exception exception)
    {
        Write("ERROR", $"{context}: {exception.GetType().Name}: {exception.Message}");
    }

    public static IDisposable BeginOperation(string name) => new TimedOperation(name);

    private static void Write(string level, string message)
    {
        string line = $"{DateTime.UtcNow:O} [{level}] {message}";
        if (PendingLines.TryAdd(line))
        {
            return;
        }

        int droppedLineCount = Interlocked.Increment(ref _droppedLineCount);
        if (droppedLineCount == 1 || droppedLineCount % 100 == 0)
        {
            Trace.WriteLine($"NoirLumen logger queue is full. droppedLines={droppedLineCount}.");
        }
    }

    private static async Task WriterLoop()
    {
        List<string> bufferedLines = [];
        while (true)
        {
            string nextLine = PendingLines.Take();
            bufferedLines.Add(nextLine);

            while (PendingLines.TryTake(out string? queuedLine))
            {
                bufferedLines.Add(queuedLine);
                if (bufferedLines.Count >= 128)
                {
                    break;
                }
            }

            int droppedLineCount = Interlocked.Exchange(ref _droppedLineCount, 0);
            if (droppedLineCount > 0)
            {
                bufferedLines.Insert(0, $"{DateTime.UtcNow:O} [WARN] Logger dropped {droppedLineCount} lines due to backpressure.");
            }

            try
            {
                string? directoryPath = Path.GetDirectoryName(LogFilePath);
                if (string.IsNullOrWhiteSpace(directoryPath))
                {
                    Trace.WriteLine("NoirLumen logger path is invalid; dropping buffered log lines.");
                    continue;
                }

                Directory.CreateDirectory(directoryPath);
                await File.AppendAllLinesAsync(LogFilePath, bufferedLines);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Trace.WriteLine($"NoirLumen logger write failed: {exception.Message}");
            }
            finally
            {
                bufferedLines.Clear();
            }
        }
    }

    private static string BuildLogFilePath()
    {
        string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appDataPath, "NativeScreenDimmer", "logs", "app.log");
    }

    private sealed class TimedOperation : IDisposable
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly string _name;
        private bool _disposed;

        public TimedOperation(string name)
        {
            _name = name;
            LogInfo($"START {_name}");
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stopwatch.Stop();
            LogInfo($"END {_name} durationMs={_stopwatch.ElapsedMilliseconds}");
        }
    }
}
