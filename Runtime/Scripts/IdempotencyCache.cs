using System;
using System.Collections.Generic;
using VoyageForge.Rpc.Transport;

namespace VoyageForge.Rpc.Executor
{
    /// <summary>
    /// 幂等缓存：按 RequestId 去重，默认 5 分钟 TTL。
    /// <list type="bullet">
    /// <item>默认开启、不提供关闭开关；</item>
    /// <item>只缓存成功响应，失败响应允许用新 RequestId 重试；</item>
    /// <item>RequestId 为 0 的本地调用不参与缓存。</item>
    /// </list>
    /// </summary>
    public sealed class IdempotencyCache
    {
        /// <summary>默认缓存时长：5 分钟。</summary>
        public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

        /// <summary>默认容量上限，避免长期运行无限增长。</summary>
        public const int DefaultCapacity = 1024;

        private readonly Dictionary<uint, Entry> _entries = new Dictionary<uint, Entry>();
        private readonly object _sync = new object();
        private readonly TimeSpan _ttl;
        private readonly int _capacity;

        /// <summary>创建幂等缓存。</summary>
        /// <param name="ttl">缓存时长；默认 5 分钟。</param>
        /// <param name="capacity">容量上限；默认 1024。</param>
        public IdempotencyCache(TimeSpan? ttl = null, int capacity = DefaultCapacity)
        {
            if (ttl.HasValue && ttl.Value <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ttl));
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));

            _ttl = ttl ?? DefaultTtl;
            _capacity = capacity;
        }

        /// <summary>当前缓存时长。</summary>
        public TimeSpan Ttl { get { return _ttl; } }

        /// <summary>当前缓存项数量（诊断用）。</summary>
        public int Count
        {
            get
            {
                lock (_sync)
                {
                    PruneLocked();
                    return _entries.Count;
                }
            }
        }

        /// <summary>查找 RequestId 对应的成功响应。</summary>
        public bool TryGet(uint requestId, out RpcResponse response)
        {
            response = null;
            if (requestId == 0) return false;

            lock (_sync)
            {
                PruneLocked();
                if (!_entries.TryGetValue(requestId, out Entry entry)) return false;

                response = entry.Response;
                return true;
            }
        }

        /// <summary>写入成功响应；失败响应或 RequestId 为 0 时忽略。</summary>
        public void Set(uint requestId, RpcResponse response)
        {
            if (requestId == 0 || response == null || !response.Success) return;

            lock (_sync)
            {
                PruneLocked();
                if (_entries.Count >= _capacity)
                {
                    // 容量满时按最早插入项淘汰；Dictionary 不保证顺序，这里简单清空一半。
                    List<uint> keys = new List<uint>(_entries.Keys);
                    int removeCount = Math.Max(1, keys.Count / 2);
                    for (int index = 0; index < removeCount && index < keys.Count; index++)
                    {
                        _entries.Remove(keys[index]);
                    }
                }

                _entries[requestId] = new Entry(response, DateTime.UtcNow);
            }
        }

        /// <summary>清空缓存。</summary>
        public void Clear()
        {
            lock (_sync)
            {
                _entries.Clear();
            }
        }

        /// <summary>清理过期项（调用方需持有锁）。</summary>
        private void PruneLocked()
        {
            if (_entries.Count == 0) return;

            DateTime now = DateTime.UtcNow;
            List<uint> expired = null;
            foreach (KeyValuePair<uint, Entry> pair in _entries)
            {
                if (now - pair.Value.CreatedAt > _ttl)
                {
                    if (expired == null) expired = new List<uint>();
                    expired.Add(pair.Key);
                }
            }

            if (expired == null) return;
            for (int index = 0; index < expired.Count; index++)
            {
                _entries.Remove(expired[index]);
            }
        }

        private readonly struct Entry
        {
            public Entry(RpcResponse response, DateTime createdAt)
            {
                Response = response;
                CreatedAt = createdAt;
            }

            public RpcResponse Response { get; }

            public DateTime CreatedAt { get; }
        }
    }
}
