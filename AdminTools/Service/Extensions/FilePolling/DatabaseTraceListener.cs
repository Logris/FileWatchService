using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace FilePolling
{
    public class DatabaseTraceListener : TraceListener
    {
        private readonly string table_name;
        private readonly List<LogEntry> log_buffer = new List<LogEntry>();
        private readonly int buffer_size = 10; // Размер буфера
        private readonly TimeSpan flush_interval = TimeSpan.FromSeconds(10); // Интервал сброса буфера
        private readonly object buffer_lock = new object();
        private readonly CancellationTokenSource cancellation_source;
        private readonly SqliteCommandManager _db_manager;

        public DatabaseTraceListener(SqliteCommandManager manager, string table_name)
        {
            this._db_manager = manager;
            this.table_name = table_name ?? throw new ArgumentNullException(nameof(table_name));

            this.cancellation_source = new CancellationTokenSource();
            Task.Run(() => FlushBufferPeriodically(cancellation_source.Token));
        }

        public override void Write(string message)
        {
            // Игнорируем, так как мы будем использовать WriteLine
        }

        public override void WriteLine(string message)
        {
            WriteLine(message, "INFO");
        }

        public override void WriteLine(string message, string category)
        {
            lock (buffer_lock)
            {
                log_buffer.Add(new LogEntry { Message = message, Category = category, Timestamp = DateTime.UtcNow });

                if (log_buffer.Count >= buffer_size)
                {
                    FlushBufferAsync().ConfigureAwait(false);
                }
            }
        }

        public void Stop()
        {
            cancellation_source.Cancel();
        }

        public new void Dispose()
        {
            cancellation_source?.Cancel();
            cancellation_source?.Dispose();

            base.Dispose();
        }

        private async Task FlushBufferAsync()
        {
            List<LogEntry> buffer_copy;
            lock (buffer_lock)
            {
                buffer_copy = new List<LogEntry>(log_buffer);
                log_buffer.Clear();
            }

            try
            {
                var query = $"INSERT INTO {table_name} (Time, Category, Message) VALUES (@Time, @Category, @Message)";
                foreach (var log in buffer_copy)
                {
                    await _db_manager.ExecuteNonQueryAsync(query,
                        ("@Message", log.Message),
                        ("@Category", log.Category),
                        ("@Time", log.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"))
                    );
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to write logs to database: {ex.Message}");
            }
        }

        private async Task FlushBufferPeriodically(CancellationToken cancellation_token)
        {
            while (!cancellation_token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(flush_interval);
                    await FlushBufferAsync();
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"DataBaseListener: {ex.Message}", "ERROR");
                }
            }

        }

        private sealed class LogEntry
        {
            public string Message { get; set; }
            public string Category { get; set; }
            public DateTime Timestamp { get; set; }
        }
    }
}
