using System;
using System.Threading;
using System.Threading.Tasks;

namespace FilePolling
{
    public static class DatabaseLock
    {
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public static void ExecuteWithLock(Action action)
        {
            _semaphore.Wait();
            try
            {
                action();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public static async Task ExecuteWithLockAsync(Func<Task> asyncAction)
        {
            await _semaphore.WaitAsync();
            try
            {
                await asyncAction();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public static async Task<T> ExecuteWithLockAsync<T>(Func<Task<T>> asyncAction)
        {
            await _semaphore.WaitAsync();
            try
            {
                return await asyncAction();
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}