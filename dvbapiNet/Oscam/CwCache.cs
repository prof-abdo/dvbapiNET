using dvbapiNet.Dvb.Crypto;
using System;
using System.Collections.Generic;

namespace dvbapiNet.Oscam
{
    /// <summary>
    /// Per-SID Control Word cache. Stores the most recent CWs received from Oscam for each
    /// service, so a retune to a recently watched channel can apply a cached CW immediately
    /// (seed) before the next ECM round-trip completes. The fresh CW from Oscam overwrites
    /// the cached one as soon as it arrives, so this never produces stale decryption.
    ///
    /// Thread-safe singleton. Disabled by default via `cache.cw = 0` in config (opt-in).
    /// </summary>
    public sealed class CwCache
    {
        private const int cTtlSeconds = 12; // typical DVB CW validity window

        public sealed class CwEntry
        {
            public DescramblerParity Parity;
            public byte[] Cw;
            public DateTime At;
        }

        private static readonly Lazy<CwCache> _Instance = new Lazy<CwCache>(() => new CwCache());
        public static CwCache Instance => _Instance.Value;

        private readonly object _Lock = new object();
        private readonly Dictionary<int, List<CwEntry>> _Cache = new Dictionary<int, List<CwEntry>>();
        private bool _Enabled;
        private long _Hits;
        private long _Misses;
        private long _Stores;

        public bool Enabled
        {
            get { lock (_Lock) return _Enabled; }
            set { lock (_Lock) _Enabled = value; }
        }

        public long Hits { get { lock (_Lock) return _Hits; } }
        public long Misses { get { lock (_Lock) return _Misses; } }
        public long Stores { get { lock (_Lock) return _Stores; } }

        private CwCache()
        {
            try { Globals.Config.Get("cache", "cw", ref _Enabled); } catch { }
        }

        public void Store(int sid, DescramblerParity parity, byte[] cw)
        {
            if (cw == null || cw.Length == 0 || sid <= 0) return;
            lock (_Lock)
            {
                if (!_Enabled) return;
                if (!_Cache.TryGetValue(sid, out var list))
                {
                    list = new List<CwEntry>();
                    _Cache[sid] = list;
                }
                list.RemoveAll(e => e.Parity == parity);
                list.Add(new CwEntry
                {
                    Parity = parity,
                    Cw = (byte[])cw.Clone(),
                    At = DateTime.UtcNow
                });
                _Stores++;
            }
        }

        /// <summary>
        /// Returns fresh cached entries for a SID, or null if none / disabled / expired.
        /// </summary>
        public CwEntry[] Take(int sid)
        {
            if (sid <= 0) return null;
            lock (_Lock)
            {
                if (!_Enabled) return null;
                if (!_Cache.TryGetValue(sid, out var list))
                {
                    _Misses++;
                    return null;
                }
                DateTime cutoff = DateTime.UtcNow.AddSeconds(-cTtlSeconds);
                list.RemoveAll(e => e.At < cutoff);
                if (list.Count == 0)
                {
                    _Misses++;
                    return null;
                }
                _Hits++;
                return list.ToArray();
            }
        }

        public void Clear()
        {
            lock (_Lock)
            {
                _Cache.Clear();
                _Hits = _Misses = _Stores = 0;
            }
        }

        public int Size
        {
            get
            {
                lock (_Lock)
                {
                    int n = 0;
                    foreach (var l in _Cache.Values) n += l.Count;
                    return n;
                }
            }
        }
    }
}
