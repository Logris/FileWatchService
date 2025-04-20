using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Threading;
using System.Threading.Tasks;

namespace FilePolling
{
    public class SqliteCommandManager : IDisposable
    {
        private readonly string _connectionString;
        private readonly BlockingCollection<IDatabaseCommand> _commandQueue;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly Task _processingTask;
        private readonly SQLiteConnection _connection;
        private readonly int _batchSize;
        private readonly TimeSpan _batchTimeout;

        public SqliteCommandManager(string databasePath, int batchSize = 50, TimeSpan? batchTimeout = null)
        {
            _connectionString = $"Data Source={databasePath};Version=3;Journal Mode=WAL;";
            _connection = new SQLiteConnection(_connectionString);
            _connection.Open();

            _batchSize = Math.Max(1, batchSize);
            _batchTimeout = batchTimeout ?? TimeSpan.FromMilliseconds(100);

            CreateDefaultTables();

            _commandQueue = new BlockingCollection<IDatabaseCommand>();
            _cancellationTokenSource = new CancellationTokenSource();
            _processingTask = Task.Run(ProcessQueue);
        }

        private void CreateDefaultTables()
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS LOG (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Time TEXT NOT NULL,
                        Category TEXT NOT NULL,
                        Message TEXT NOT NULL
                    );
                
                    CREATE TABLE IF NOT EXISTS Snapshot (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Path TEXT NOT NULL,
                        Time TEXT NOT NULL,
                        LastChange INTEGER NOT NULL
                    );
        
                    CREATE TABLE IF NOT EXISTS Tasks (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Path TEXT NOT NULL,
                        Status TEXT NOT NULL,
                        StatusCode INTEGER NOT NULL,
                        CreatedAt TEXT NOT NULL,
                        CompletedAt TEXT,
                        Description TEXT
                    );
                
                CREATE INDEX IF NOT EXISTS IX_Tasks_StatusCode ON Tasks(StatusCode);";

                cmd.ExecuteNonQuery();
            }
        }

        public Task<int> ExecuteNonQueryAsync(string sql, params (string name, object value)[] parameters)
        {
            var tcs = new TaskCompletionSource<int>();

            _commandQueue.Add(new NonQueryCommand
            {
                CommandText = sql,
                Parameters = parameters,
                CompletionSource = tcs
            });

            return tcs.Task;
        }

        public int ExecuteNonQuery(string sql, params (string name, object value)[] parameters)
        {
            int affectedRows = 0;

            using (SQLiteConnection connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                using (SQLiteCommand command = new SQLiteCommand(sql, connection))
                {
                    if (parameters != null)
                    {
                        foreach (var (name, value) in parameters)
                        {
                            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
                        }
                    }

                    affectedRows = command.ExecuteNonQuery();
                }
            }
            return affectedRows;
        }

        public Task<object> ExecuteScalarAsync(string sql, params (string name, object value)[] parameters)
        {
            var tcs = new TaskCompletionSource<object>();

            _commandQueue.Add(new ScalarCommand
            {
                CommandText = sql,
                Parameters = parameters,
                CompletionSource = tcs
            });

            return tcs.Task;
        }

        public async Task<System.Data.Common.DbDataReader> ExecuteReaderAsync(string sql, params (string name, object value)[] parameters)
        {
            var readConnection = new SQLiteConnection(_connectionString);
            var command = new SQLiteCommand(sql, readConnection);

            try
            {
                await readConnection.OpenAsync();

                foreach (var (name, value) in parameters)
                {
                    command.Parameters.AddWithValue(name, value ?? DBNull.Value);
                }

                // Явно указываем тип возвращаемого reader'а как DbDataReader
                return await command.ExecuteReaderAsync(CommandBehavior.CloseConnection);
            }
            catch
            {
                readConnection.Dispose();
                command.Dispose();
                throw;
            }
        }
        private void ProcessQueue()
        {
            var batch = new List<IDatabaseCommand>();
            var batchWaitToken = new CancellationTokenSource();

            try
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    if (_commandQueue.TryTake(out var command, Timeout.Infinite, _cancellationTokenSource.Token))
                    {
                        batch.Add(command);

                        try
                        {
                            batchWaitToken.CancelAfter(_batchTimeout);

                            while (batch.Count < _batchSize &&
                                   _commandQueue.TryTake(out command, Timeout.Infinite, batchWaitToken.Token))
                            {
                                batch.Add(command);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            // Таймаут ожидания - выполняем текущий пакет
                        }
                        finally
                        {
                            batchWaitToken.Dispose();
                            batchWaitToken = new CancellationTokenSource();
                        }

                        ExecuteBatch(batch);
                        batch.Clear();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (batch.Count > 0)
                {
                    ExecuteBatch(batch);
                }
            }
            finally
            {
                batchWaitToken.Dispose();
            }
        }

        private void ExecuteBatch(List<IDatabaseCommand> batch)
        {
            try
            {
                using (var transaction = _connection.BeginTransaction())
                {
                    foreach (var cmd in batch)
                    {
                        cmd.Execute(_connection);
                    }
                    transaction.Commit();
                }
            }
            catch (Exception ex)
            {
                foreach (var cmd in batch)
                {
                    cmd.SetException(ex);
                }
            }
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _processingTask.Wait();

            _commandQueue.Dispose();
            _cancellationTokenSource.Dispose();

            _connection.Close();
            _connection.Dispose();
        }

        private interface IDatabaseCommand
        {
            string CommandText { get; set; }
            (string name, object value)[] Parameters { get; set; }
            void Execute(SQLiteConnection connection);
            void SetException(Exception ex);
        }

        private class NonQueryCommand : IDatabaseCommand
        {
            public string CommandText { get; set; }
            public (string name, object value)[] Parameters { get; set; }
            public TaskCompletionSource<int> CompletionSource { get; set; }

            public void Execute(SQLiteConnection connection)
            {
                try
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = CommandText;

                        foreach (var (name, value) in Parameters)
                        {
                            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
                        }

                        var result = command.ExecuteNonQuery();
                        CompletionSource.SetResult(result);
                    }
                }
                catch (Exception ex)
                {
                    CompletionSource.SetException(ex);
                }
            }

            public void SetException(Exception ex)
            {
                CompletionSource.SetException(ex);
            }
        }

        private class ScalarCommand : IDatabaseCommand
        {
            public string CommandText { get; set; }
            public (string name, object value)[] Parameters { get; set; }
            public TaskCompletionSource<object> CompletionSource { get; set; }

            public void Execute(SQLiteConnection connection)
            {
                try
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = CommandText;

                        foreach (var (name, value) in Parameters)
                        {
                            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
                        }

                        var result = command.ExecuteScalar();
                        CompletionSource.SetResult(result);
                    }
                }
                catch (Exception ex)
                {
                    CompletionSource.SetException(ex);
                }
            }

            public void SetException(Exception ex)
            {
                CompletionSource.SetException(ex);
            }
        }
    }
}


// namespace FilePolling
// {
//         private void CreateDefaultTables()
//         {
//             using (var cmd = _connection.CreateCommand())
//             {
//                 cmd.CommandText = @"
//                     CREATE TABLE IF NOT EXISTS LOG (
//                         Id INTEGER PRIMARY KEY AUTOINCREMENT,
//                         Time TEXT NOT NULL,
//                         Category TEXT NOT NULL,
//                         Message TEXT NOT NULL
//                     );
//                 
//                     CREATE TABLE IF NOT EXISTS Snapshot (
//                         Id INTEGER PRIMARY KEY AUTOINCREMENT,
//                         Path TEXT NOT NULL,
//                         Time TEXT NOT NULL,
//                         LastChange INTEGER NOT NULL
//                     );
// 
//                     CREATE TABLE IF NOT EXISTS Tasks (
//                         Id INTEGER PRIMARY KEY AUTOINCREMENT,
//                         Path TEXT NOT NULL,
//                         Status TEXT NOT NULL,
//                         StatusCode INTEGER NOT NULL,
//                         CreatedAt TEXT NOT NULL,
//                         CompletedAt TEXT,
//                         Description TEXT
//                     );
//                 
//                 CREATE INDEX IF NOT EXISTS IX_Tasks_StatusCode ON Tasks(StatusCode);";
// 
//                 cmd.ExecuteNonQuery();
//             }
//         }
// 
//         public async Task<IReadOnlyList<IDataRecord>> ExecuteReaderAsyncAsList(string sql, params (string name, object value)[] parameters)
//         {
//             // Для чтения используем отдельное соединение
//             using (var readConnection = new SQLiteConnection(_connectionString))
//             {
//                 using (var command = new SQLiteCommand(sql, readConnection))
//                 {
//                     await readConnection.OpenAsync();
// 
//                     foreach (var (name, value) in parameters)
//                     {
//                         command.Parameters.AddWithValue(name, value ?? DBNull.Value);
//                     }
// 
//                     var results = new List<IDataRecord>();
//                     using (var reader = await command.ExecuteReaderAsync())
//                     {
//                         while (await reader.ReadAsync())
//                         {
//                             results.Add(reader);
//                         }
//                     }
// 
//                     return results.AsReadOnly();
//                 }
//             }
//         }
// 
//         public async Task<System.Data.Common.DbDataReader> ExecuteReaderAsync(string sql, params (string name, object value)[] parameters)
//         {
//             var readConnection = new SQLiteConnection(_connectionString);
//             var command = new SQLiteCommand(sql, readConnection);
// 
//             try
//             {
//                 await readConnection.OpenAsync();
// 
//                 foreach (var (name, value) in parameters)
//                 {
//                     command.Parameters.AddWithValue(name, value ?? DBNull.Value);
//                 }
// 
//                 // Явно указываем тип возвращаемого reader'а как DbDataReader
//                 return await command.ExecuteReaderAsync(CommandBehavior.CloseConnection);
//             }
//             catch
//             {
//                 readConnection.Dispose();
//                 command.Dispose();
//                 throw;
//             }
//         }
// }


