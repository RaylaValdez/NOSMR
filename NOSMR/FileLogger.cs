using System;
using System.Collections.Concurrent;
using System.IO;

namespace NOSMR;

/// <summary>
/// Thread-safe debug file logger. Buffers writes in a concurrent queue
/// and flushes to disk periodically (call Flush) or on dispose.
/// </summary>
public sealed class FileLogger : IDisposable
{
    private readonly string _logPath;
    private readonly StreamWriter _writer;
    private readonly ConcurrentQueue<string> _queue = new();
    private bool _disposed;

    public FileLogger(string pluginDirectory)
    {
        var dir = Path.Combine(pluginDirectory, "debug");
        Directory.CreateDirectory(dir);

        _logPath = Path.Combine(dir, "debug.log");
        _writer = new StreamWriter(_logPath, append: true) { AutoFlush = false };
    }

    public void Info(string message) => Enqueue("INFO", message);
    public void Warn(string message) => Enqueue("WARN", message);
    public void Error(string message) => Enqueue("ERROR", message);
    public void Debug(string message) => Enqueue("DEBUG", message);

    public void Error(string message, Exception ex) =>
        Enqueue("ERROR", $"{message}: {ex.Message}\n{ex.StackTrace}");

    private void Enqueue(string level, string message)
    {
        if (_disposed) return;

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        _queue.Enqueue($"[{timestamp}] [{level}] {message}");
    }

    public void Flush()
    {
        while (_queue.TryDequeue(out var line))
        {
            try
            {
                _writer.WriteLine(line);
            }
            catch
            {
            }
        }
        try { _writer.Flush(); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Flush();
        _writer.Dispose();
    }
}
