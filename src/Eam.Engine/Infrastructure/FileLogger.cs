using System.Collections.Concurrent;
using System.Text;

namespace Eam.Engine.Infrastructure;

/// <summary>Writes one log file per day in the data folder and keeps the last 30 days.</summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _dir;
    private readonly BlockingCollection<string> _queue = new(new ConcurrentQueue<string>(), 10_000);
    private readonly Thread _writer;

    public FileLoggerProvider(string dir)
    {
        _dir = dir;
        Directory.CreateDirectory(dir);
        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "eam-log-writer" };
        _writer.Start();
        Cleanup();
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    internal void Enqueue(string line) => _queue.TryAdd(line);

    private void WriteLoop()
    {
        foreach (var line in _queue.GetConsumingEnumerable())
        {
            try
            {
                var file = Path.Combine(_dir, $"eam-{DateTime.Now:yyyy-MM-dd}.log");
                var sb = new StringBuilder(line).AppendLine();
                while (_queue.TryTake(out var more)) sb.AppendLine(more);
                File.AppendAllText(file, sb.ToString());
            }
            catch { /* logging must never crash the engine */ }
        }
    }

    private void Cleanup()
    {
        try
        {
            foreach (var f in Directory.GetFiles(_dir, "eam-*.log"))
                if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-30)) File.Delete(f);
        }
        catch { }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _writer.Join(TimeSpan.FromSeconds(2));
    }

    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var level = logLevel switch
            {
                LogLevel.Information => "INFO ",
                LogLevel.Warning => "WARN ",
                LogLevel.Error => "ERROR",
                LogLevel.Critical => "CRIT ",
                _ => logLevel.ToString().ToUpperInvariant(),
            };
            var shortCat = category[(category.LastIndexOf('.') + 1)..];
            var msg = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} [{shortCat}] {formatter(state, exception)}";
            if (exception is not null) msg += Environment.NewLine + exception;
            owner.Enqueue(msg);
        }
    }
}
