using System;
using System.IO;
using System.Threading;

namespace NOSMR;

/// <summary>
/// Thread-safe debug file logger. Writes to NOSMR/debug.log alongside the plugin.
/// </summary>
public sealed class FileLogger : IDisposable
{
    private readonly string _logPath;
    private readonly StreamWriter _writer;
    private readonly ReaderWriterLockSlim _lock = new();
    private bool _disposed;

    public FileLogger(string pluginDirectory)
    {
        var dir = Path.Combine(pluginDirectory, "debug");
        Directory.CreateDirectory(dir);

        _logPath = Path.Combine(dir, "debug.log");
        _writer = new StreamWriter(_logPath, append: true) { AutoFlush = true };
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);
    public void Debug(string message) => Write("DEBUG", message);

    public void Error(string message, Exception ex) =>
        Write("ERROR", $"{message}: {ex.Message}\n{ex.StackTrace}");

    private void Write(string level, string message)
    {
        if (_disposed) return;

        _lock.EnterWriteLock();
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            _writer.WriteLine($"[{timestamp}] [{level}] {message}");
        }
        catch
        {
            // Swallow logging errors - never crash the plugin over logging
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lock.EnterWriteLock();
        try
        {
            _writer.Flush();
            _writer.Dispose();
        }
        finally
        {
            _lock.ExitWriteLock();
            _lock.Dispose();
        }
    }
}
