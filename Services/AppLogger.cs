using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace NativeScreenDimmer_WinUI3.Services;

internal static class AppLogger
{
    private static readonly string LogFilePath = BuildLogFilePath();
    private static readonly BlockingCollection<string> PendingLines = new(new ConcurrentQueue<string>());
    private static readonly Task WriterTask = Task.Run(WriterLoop);

    public static void LogInfo(string message)
    {
        Write("INFO", message);
    }

    public static void LogWarning(string message)
    {
        Write("WARN", message);
    }

    public static void LogError(string message)
    {
        Write("ERROR", message);
    }

    public static void LogException(string context, Exception exception)
    {
        Write("ERROR", $"{context}: {exception.GetType().Name}: {exception.Message}");
    }

    public static IDisposable BeginOperation(string name)
    {
        return new TimedOperation(name);
    }

    private static void Write(string level, string message)
    {
        string line = $"{DateTime.UtcNow:O} [{level}] {message}";
        PendingLines.Add(line);
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

            try
            {
                string? directoryPath = System.IO.Path.GetDirectoryName(LogFilePath);
                if (string.IsNullOrWhiteSpace(directoryPath))
                {
                    bufferedLines.Clear();
                    continue;
                }

                System.IO.Directory.CreateDirectory(directoryPath);
                await System.IO.File.AppendAllLinesAsync(LogFilePath, bufferedLines);
            }
            catch (IOException)
            {
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
        return System.IO.Path.Combine(appDataPath, "NativeScreenDimmer", "logs", "app.log");
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

