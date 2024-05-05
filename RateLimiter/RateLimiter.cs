using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace RateLimiter
{
    public class RateLimiter<TArg>
    {
        private readonly Func<TArg, Task> _action;
        private readonly ConcurrentDictionary<TimeSpan, (int Limit, SemaphoreSlim Semaphore, ConcurrentQueue<DateTime> Queue, TimeSpan Period)> _rateLimits;

        public RateLimiter(Func<TArg, Task> action, params (TimeSpan Period, int Limit)[] rateLimits)
        {
            _action = action;
            _rateLimits = new ConcurrentDictionary<TimeSpan, (int, SemaphoreSlim, ConcurrentQueue<DateTime>, TimeSpan)>();

            foreach (var (period, limit) in rateLimits)
            {
                _rateLimits[period] = (limit, new SemaphoreSlim(limit, limit), new ConcurrentQueue<DateTime>(), period);
            }
        }

        public async Task Perform(TArg argument)
        {
            
            foreach (var rateLimit in _rateLimits.Values)   //Wait for all rate limits to allow a new action
            {
                await WaitUntilLimitAllows(rateLimit.Semaphore, rateLimit.Queue, rateLimit.Limit, rateLimit.Period);
            }

            var now = DateTime.UtcNow;

            foreach (var rateLimit in _rateLimits.Values)             //Acquire the semaphore and record the action time

            {
                rateLimit.Semaphore.Wait();
                rateLimit.Queue.Enqueue(now);

                await CleanupAsync(rateLimit.Semaphore, rateLimit.Queue, now - rateLimit.Period); //Cleanup old entries asynchronously to keep the queue size in check
            }

            try
            {
                await _action(argument);
            }
            finally
            {
                foreach (var rateLimit in _rateLimits.Values)   //Release the semaphore and remove the action time from the queue
                {
                    rateLimit.Queue.TryDequeue(out _);
                    rateLimit.Semaphore.Release();
                }
            }
        }

        private static async Task CleanupAsync(SemaphoreSlim semaphore, ConcurrentQueue<DateTime> queue, DateTime thresholdTime)
        {
            await Task.Run(() => {
                while (queue.TryPeek(out DateTime oldTime) && oldTime < thresholdTime)
                {
                    queue.TryDequeue(out _);
                }
            });
        }


        private static async Task WaitUntilLimitAllows(SemaphoreSlim semaphore, ConcurrentQueue<DateTime> queue, int limit, TimeSpan period)
        {
            while (true)
            {
                DateTime now = DateTime.UtcNow;

                while (queue.TryPeek(out DateTime oldest) && (now - oldest > period))   //Clean up old timestamps before checking conditions.
                {
                    queue.TryDequeue(out _);
                    Console.WriteLine("Old timestamp removed at: " + DateTime.UtcNow.ToString("T"));
                }

                if (queue.Count < limit)
                {
                    Console.WriteLine("Queue has space. Proceeding...");
                    break;  //Enough space under the limit to proceed.
                }

                //Calculate the time until the next possible action can be taken
                DateTime expectedNextPossibleActionTime = queue.OrderBy(x => x).First().Add(period);
                TimeSpan timeToWait = expectedNextPossibleActionTime.Subtract(DateTime.UtcNow);

                if (timeToWait.TotalMilliseconds > 0)   //Ensure we're waiting a reasonable amount of time
                {
                    Console.WriteLine($"Rate limit reached. Queue count: {queue.Count}. Next possible action at: {expectedNextPossibleActionTime:T}. Waiting {timeToWait.TotalSeconds} seconds...");
                    await Task.Delay(timeToWait);
                }
                else
                {
                    await Task.Delay(100); //Check every 100 ms as fallback
                }
            }

            queue.Enqueue(DateTime.UtcNow); //Enqueue the current timestamp when proceeding
        }




    }
}
