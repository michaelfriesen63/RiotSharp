using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("RiotSharp.Test")]
namespace RiotSharp.Http
{
    internal class RateLimiter
    {
        /// <summary>
        /// A delegate that is rate-limited.
        /// </summary>
        public delegate Task<T> RateLimitRoutineAsync<T>();

        /// <summary>
        /// A delegate that is rate-limited.
        /// </summary>
        public delegate T RateLimitRoutine<T>();

        private bool _timerRunning;

        private uint _expiryTick;

        private Timer _timer;

        private readonly RateLimit _retryLimit;

        private readonly List<RateLimit> _limits;

        private readonly LinkedList<TaskCompletionSource<uint>> _queue;

        public RateLimiter(IDictionary<TimeSpan, int> rateLimits)
        {
            _limits = new List<RateLimit>(rateLimits.Count);
            foreach (var pair in rateLimits)
            {
                double intervalMs = Math.Ceiling(pair.Key.TotalMilliseconds);

                if (intervalMs <= 0 || intervalMs > uint.MaxValue)
                {
                    throw new ArgumentOutOfRangeException(nameof(rateLimits));

                }

                if (pair.Value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(rateLimits));
                }

                _limits.Add(new RateLimit((uint)intervalMs, (uint)pair.Value));
            }

            _retryLimit = new RetryRateLimit();
            //_limits.Add(_retryLimit);

            _queue = new LinkedList<TaskCompletionSource<uint>>();

            _timerRunning = false;
            _timer = new Timer(TimerExpiredCallback);
        }

        /// <summary>Blocks until a request can be made without violating rate limit rules. Release must be called
        /// after the request completes.</summary>
        /// <param name="routine">The routine to rate-limit.</param>
        public async Task<T> HandleRateLimitAsync<T>(RateLimitRoutineAsync<T> routine)
        {
            bool retry = false;

            while (true)
            {
                try
                {
                    return await HandleRateLimitAsyncInternal(routine, retry);
                }
                catch (RateLimitException e)
                {
                    Trace.TraceError($"Caught RateLimitException with RetryAfter={e.RetryAfter}");
                    retry = true;
                }
            }
        }

        private async Task<T> HandleRateLimitAsyncInternal<T>(RateLimitRoutineAsync<T> routine, bool retry)
        {
            TaskCompletionSource<uint> task = new TaskCompletionSource<uint>();
            Exception exception = null;
            T result = default(T);

            lock (_limits)
            {
                Enqueue(task, retry);
            }

            await task.Task;

            try
            {
                result = await routine();
            }
            catch (RateLimitException e)
            {
                // TODO set the retry rate limit.
                exception = e;
            }
            catch (Exception e)
            {
                exception = e;
            }
            finally
            {
                lock (_limits)
                {
                    Complete();
                }
            }

            if (exception != null)
            {
                throw exception;
            }

            return result;
        }

        /// <summary>Creates a task that blocks until a request can be made without violating rate limit rules. Release
        /// must be called after the task completes.</summary>
        public T HandleRateLimit<T>(RateLimitRoutine<T> routine)
        {
            return HandleRateLimitAsync(() => { return Task.FromResult(routine()); }).GetAwaiter().GetResult();
        }

        private void Enqueue(TaskCompletionSource<uint> task, bool front = false)
        {
            if (front)
            {
                _queue.AddFirst(task);
            }
            else
            {
                _queue.AddLast(task);
            }

            if (CanStart())
            {
                Start();
            }
        }

        private bool CanStart()
        {
            if (_queue.Count == 0)
            {
                return false;
            }

            foreach (RateLimit limit in _limits)
            {
                if (!limit.CanStart())
                {
                    return false;
                }
            }

            return true;
        }

        private void Start() {
            uint currentTick = GetTickCount();

            foreach (RateLimit limit in _limits)
            {
                limit.Start(currentTick);
            }

            TaskCompletionSource<uint> task = _queue.First.Value;
            _queue.RemoveFirst();
            task.SetResult(currentTick);
        }

        private void TimerExpiredCallback(object stateObject)
        {
            lock (_limits)
            {
                uint currentTick = GetTickCount();

                if (Tick.GreaterThan(_expiryTick, currentTick))
                {
                    // The timer fired before the expiry tick.
                    Trace.TraceInformation($"{this}: timer rescheduled for {_expiryTick} at {currentTick}");
                    _timer.Change(_expiryTick - currentTick, Timeout.Infinite);
                    return;
                }

                ProcessExpiry(currentTick);
            }
        }

        private void ProcessExpiry(uint currentTick)
        {
            Trace.TraceInformation($"{this}: timer for {_expiryTick} expired at {currentTick}");

            _timerRunning = false;

            foreach (RateLimit limit in _limits)
            {
                limit.ProcessExpiry(currentTick);
            }

            while (CanStart())
            {
                Start();
            }

            CheckTimer(GetTickCount());
        }

        private void CheckTimer(uint currentTick)
        {
            uint expiryInterval;
            uint hardLimit = uint.MaxValue;
            uint softLimit = 0;

            foreach (RateLimit limit in _limits)
            {
                if (!limit.CanStart())
                {
                    hardLimit = Math.Min(hardLimit, limit.NextExpiry(currentTick));
                }
                else
                {
                    softLimit = Math.Max(softLimit, limit.NextExpiry(currentTick));
                }
            }

            expiryInterval = Math.Min(hardLimit, softLimit);
            Trace.TraceInformation($"CheckTimer running={_timerRunning} currentTick={currentTick} _expiryTick={_expiryTick} expiryInterval={expiryInterval} hardLimit={hardLimit} softLimit={softLimit}");

            if (expiryInterval < uint.MaxValue)
            {
                if (!_timerRunning || _expiryTick != currentTick + expiryInterval)
                {
                    _expiryTick = currentTick + expiryInterval;
                    Trace.TraceInformation($"{this}: timer scheduled for {_expiryTick} at {currentTick}+{expiryInterval}");
                    _timer.Change(expiryInterval, Timeout.Infinite);
                    _timerRunning = true;
                }
            }
        }

        private void Complete()
        {
            uint currentTick = GetTickCount();

            foreach (RateLimit limit in _limits)
            {
                limit.Complete(currentTick);
            }

            CheckTimer(currentTick);
        }

        private static uint GetTickCount()
        {
            return (uint)Environment.TickCount;
        }

        private class Tick
        {
            public static bool GreaterThan(uint leftSide, uint rightSide)
            {
                return (int)(leftSide - rightSide) > 0;
            }
        }

        private class RateLimit
        {
            private uint _limitCount;
            private uint _intervalTicks;

            private bool _needStartTick;
            private uint _startTick;
            private uint _availableCount;
            private uint _outstandingCount;

            public RateLimit(uint intervalMs, uint count)
            {
                _intervalTicks = intervalMs;
                _limitCount = count;
                _availableCount = count;
                _outstandingCount = 0;
                _needStartTick = true;
            }

            public bool CanStart()
            {
                return _availableCount > 0;
            }

            public void Start(uint currentTick)
            {
                if (!CanStart())
                {
                    throw new InvalidOperationException("Rate limit exceeded");
                }

                _availableCount--;
                _outstandingCount++;
            }

            public uint NextExpiry(uint currentTick)
            {
                Trace.TraceInformation($"NextExpiry({this}): currentTick={currentTick} _startTick={_startTick} _needStartTick={_needStartTick} _availableCount={_availableCount} _limitCount={_limitCount} _outstandingCount={_outstandingCount} ");

                if (_availableCount == _limitCount)
                {
                    return uint.MaxValue;
                }
                else if (Tick.GreaterThan(_startTick + _intervalTicks, currentTick))
                {
                    return _startTick + _intervalTicks - currentTick;
                }
                else
                {
                    return 0;
                }
            }

            public void Complete(uint currentTick)
            {
                if (_outstandingCount == 0)
                {
                    throw new InvalidOperationException("Completions > Starts");
                }

                _outstandingCount--;

                if (_needStartTick)
                {
                    _startTick = currentTick;
                    _needStartTick = false;
                }
            }

            public void ProcessExpiry(uint currentTick)
            {
                if (!Tick.GreaterThan(_startTick + _intervalTicks, currentTick))
                {
                    Trace.TraceInformation($"{this} expired at {currentTick}");

                    _availableCount = _limitCount - _outstandingCount;
                    _needStartTick = true;
                }
            }

            public override string ToString()
            {
                return string.Format($"RateLimit-{_intervalTicks}");
            }
        }

        private class RetryRateLimit : RateLimit
        {
            public RetryRateLimit() : base(0, 0)
            {

            }
        }
    }
}
