using System;
using System.Collections.Generic;
using System.Threading;

namespace RiotSharp
{
    public class Cache : ICache
    {
        private IDictionary<object, CacheItem> cache = new Dictionary<object, CacheItem>();

        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();

        #region ICache interface
        /// <summary>
        /// Add a (key, value) pair to the cache with a relative expiry time (e.g. 2 mins).
        /// </summary>
        /// <typeparam name="K">Type of the key.</typeparam>
        /// <typeparam name="V">Type of the value which has to be a reference type.</typeparam>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <param name="slidingExpiry">The sliding time at the end of which the (key, value) pair should expire and
        /// be purged from the cache.</param>
        public void Add<K, V>(K key, V value, TimeSpan slidingExpiry) where V : class
        {
            Add(key, value, slidingExpiry, true);
        }

        /// <summary>
        /// Add a (key, value) pair to the cache with an absolute expiry date (e.g. 23:33:00 03/04/2030)
        /// </summary>
        /// <typeparam name="K">Type of the key.</typeparam>
        /// <typeparam name="V">Type of the value which has to be a reference type.</typeparam>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <param name="absoluteExpiry">The absolute expiry date when the (key, value) pair should expire and
        /// be purged from the cache.</param>
        public void Add<K, V>(K key, V value, DateTime absoluteExpiry) where V : class
        {
            var diff = absoluteExpiry - DateTime.Now;
            if (diff > TimeSpan.Zero)
            {
                Add(key, value, diff, false);
            }
        }

        /// <summary>
        /// Get a value from the cache.
        /// </summary>
        /// <typeparam name="K">Type of the key.</typeparam>
        /// <typeparam name="V">Type of the value which has to be a reference type.</typeparam>
        /// <param name="key">The key</param>
        /// <returns>The value if the key exists in the cache, null otherwise.</returns>
        public V Get<K, V>(K key) where V : class
        {
            CacheItem item = null;

            _lock.EnterReadLock();
            try
            {
                if (!cache.TryGetValue(key, out item))
                {
                    return null;
                }
            }
            finally
            {
                _lock.ExitReadLock();
            }

            item.Hit();
            return (V)item.Value;
        }

        /// <summary>
        /// Remove the value associated with the specified key from the cache.
        /// </summary>
        /// <typeparam name="K">Type of the key.</typeparam>
        /// <param name="key">The key.</param>
        public void Remove<K>(K key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            _lock.EnterWriteLock();
            try
            {
                if (cache.TryGetValue(key, out CacheItem item))
                {
                    RemoveUnderLock(new KeyValuePair<object, CacheItem>(key, item));
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Clear the cache.
        /// </summary>
        public void Clear()
        {
            _lock.EnterWriteLock();
            try
            {
                foreach (var pair in cache)
                {
                    RemoveUnderLock(pair);
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
        #endregion

        private void Add<K, V>(K key, V value, TimeSpan timeSpan, bool isSliding) where V : class
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            _lock.EnterWriteLock();
            try
            {
                if (cache.TryGetValue(key, out CacheItem item))
                {
                    item.Value = value;
                }
                else
                {
                    item = new CacheItem(this, key);
                    item.Value = value;
                    cache.Add(key, item);
                }

                item.StartTimer(DateTime.Now + timeSpan, isSliding ? (TimeSpan?)timeSpan : null);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        private void RemoveUnderLock(KeyValuePair<object, CacheItem> pair)
        {
            pair.Value.Dispose();
            cache.Remove(pair);
        }

        private class CacheItem
        {
            private const int MaxTimer = int.MaxValue - 1;

            private object _key;
            private Cache _cache;
            private Timer _timer;

            public CacheItem(Cache cache, object key)
            {
                _key = key;
                _cache = cache;
                _timer = new Timer(TimerFired, null, Timeout.Infinite, Timeout.Infinite);
            }

            public object Value { get; set; }
            public DateTime? Expiry;
            public TimeSpan? SlidingExpiry { get; private set; }

            public void StartTimer(DateTime expiry, TimeSpan? slidingExpiry)
            {
                lock (_timer)
                {
                    Expiry = expiry;
                    SlidingExpiry = slidingExpiry;
                    RestartTimer();
                }
            }

            public void Dispose()
            {
                lock (_timer)
                {
                    if (Expiry == null)
                    {
                        return;
                    }

                    Expiry = null;
                    SlidingExpiry = null;
                    _timer.Dispose();
                }
            }

            public void Hit()
            {
                lock (_timer)
                {
                    if (SlidingExpiry != null)
                    {
                        Expiry = DateTime.Now + SlidingExpiry.Value;
                    }
                }
            }

            private void RestartTimer()
            {
                long remainingMs = (long)(Expiry.Value - DateTime.Now).TotalMilliseconds;

                if (remainingMs < 0)
                {
                    remainingMs = 0;
                }
                else if (remainingMs > MaxTimer)
                {
                    remainingMs = MaxTimer;
                }

                _timer.Change((int)remainingMs, Timeout.Infinite);
            }

            private void TimerFired(object state)
            {
                lock (_timer)
                {
                    if (Expiry == null)
                    {
                        return;
                    }

                    if (Expiry <= DateTime.Now)
                    {
                        _cache.Remove(new KeyValuePair<object, CacheItem>(_key, this));
                    }
                    else
                    {
                        RestartTimer();
                    }
                }
            }
        }
    }
}
