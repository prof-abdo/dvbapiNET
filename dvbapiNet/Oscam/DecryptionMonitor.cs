using dvbapiNet.Dvb.Crypto;
using dvbapiNet.Oscam.Packets;
using System;
using System.Collections.Generic;

namespace dvbapiNet.Oscam
{
    /// <summary>
    /// Lightweight thread-safe hub for ECM/CW telemetry. Hook into
    /// DvbApiAdapter.GotControlWord + DvbApiClient.CmdEcmInfo to collect
    /// stats exposed by ConfigDialog (Debug tab) and WebInterface.
    /// </summary>
    public sealed class DecryptionMonitor
    {
        private const int cHistorySize = 100;
        private const int cLatencyWindow = 10;

        private static readonly Lazy<DecryptionMonitor> _Instance =
            new Lazy<DecryptionMonitor>(() => new DecryptionMonitor());

        public static DecryptionMonitor Instance => _Instance.Value;

        private readonly object _Lock = new object();

        // CW counters
        private long _CwTotal;
        private long _CwEven;
        private long _CwOdd;

        // ECM counters
        private long _EcmTotal;
        private int _LastEcmMs;
        private int _MaxEcmMs;
        private readonly int[] _LatencyWindow = new int[cLatencyWindow];
        private int _LatencyCount;
        private int _LatencyCursor;

        // ECM history (circular)
        private readonly Queue<EcmEvent> _History = new Queue<EcmEvent>(cHistorySize);

        // Last CW timestamp
        private DateTime _LastCwAt = DateTime.MinValue;

        // Latency buckets — one per minute, last 60 min
        private readonly int[] _LatencyBuckets = new int[60];
        private DateTime _LastBucketTime = DateTime.UtcNow;

        // Per-channel watch time (sid → seconds)
        private readonly Dictionary<int, long> _ChannelWatchSec = new Dictionary<int, long>();
        private int _CurrentSid = 0;
        private DateTime _CurrentSidStart = DateTime.MinValue;

        // Per-CAID ECM count
        private readonly Dictionary<int, long> _CaidEcm = new Dictionary<int, long>();

        private DecryptionMonitor() { }

        public void Attach(DvbApiAdapter adapter)
        {
            if (adapter == null) return;
            adapter.GotControlWord += OnControlWord;
        }

        private void OnControlWord(byte[] cw, DescramblerParity parity)
        {
            lock (_Lock)
            {
                _CwTotal++;
                if (parity == DescramblerParity.Even) _CwEven++;
                else _CwOdd++;
                _LastCwAt = DateTime.UtcNow;
            }
        }

        internal void RecordEcm(EcmInfo info)
        {
            if (info == null) return;

            var ev = new EcmEvent
            {
                When = DateTime.UtcNow,
                CaId = info.CaId,
                Pid = info.Pid,
                EcmTimeMs = info.EcmTime,
                Reader = info.ReaderName ?? "",
                Protocol = info.ProtocolName ?? "",
                Hops = info.HopsCount
            };

            lock (_Lock)
            {
                _EcmTotal++;
                _LastEcmMs = info.EcmTime;
                if (info.EcmTime > _MaxEcmMs) _MaxEcmMs = info.EcmTime;

                // Per-CAID counter
                if (_CaidEcm.ContainsKey(info.CaId)) _CaidEcm[info.CaId]++;
                else _CaidEcm[info.CaId] = 1;

                // Channel switch tracking (via ServiceId in ECM info)
                if (info.ServiceId != _CurrentSid)
                {
                    if (_CurrentSid > 0 && _CurrentSidStart != DateTime.MinValue)
                    {
                        long elapsed = (long)(DateTime.UtcNow - _CurrentSidStart).TotalSeconds;
                        if (elapsed > 0)
                        {
                            if (_ChannelWatchSec.ContainsKey(_CurrentSid))
                                _ChannelWatchSec[_CurrentSid] += elapsed;
                            else
                                _ChannelWatchSec[_CurrentSid] = elapsed;
                        }
                    }
                    _CurrentSid = info.ServiceId;
                    _CurrentSidStart = DateTime.UtcNow;
                }

                _LatencyWindow[_LatencyCursor] = info.EcmTime;
                _LatencyCursor = (_LatencyCursor + 1) % cLatencyWindow;
                if (_LatencyCount < cLatencyWindow) _LatencyCount++;

                if (_History.Count >= cHistorySize) _History.Dequeue();
                _History.Enqueue(ev);

                // Buckets latence par minute : shift si nouvelle minute
                int minutesElapsed = (int)(DateTime.UtcNow - _LastBucketTime).TotalMinutes;
                if (minutesElapsed > 0)
                {
                    int shift = Math.Min(minutesElapsed, _LatencyBuckets.Length);
                    if (shift < _LatencyBuckets.Length)
                        Array.Copy(_LatencyBuckets, 0, _LatencyBuckets, shift, _LatencyBuckets.Length - shift);
                    for (int i = 0; i < shift; i++) _LatencyBuckets[i] = 0;
                    _LastBucketTime = _LastBucketTime.AddMinutes(minutesElapsed);
                }
                // Mise à jour du bucket courant (max latence sur la minute)
                if (info.EcmTime > _LatencyBuckets[0]) _LatencyBuckets[0] = info.EcmTime;
            }
        }

        public int[] GetLatencyBuckets()
        {
            lock (_Lock)
            {
                var copy = new int[_LatencyBuckets.Length];
                Array.Copy(_LatencyBuckets, copy, copy.Length);
                return copy;
            }
        }

        public KeyValuePair<int, long>[] GetTopChannels(int max = 10)
        {
            lock (_Lock)
            {
                // Add live elapsed for current channel
                var snap = new Dictionary<int, long>(_ChannelWatchSec);
                if (_CurrentSid > 0 && _CurrentSidStart != DateTime.MinValue)
                {
                    long live = (long)(DateTime.UtcNow - _CurrentSidStart).TotalSeconds;
                    if (live > 0)
                    {
                        if (snap.ContainsKey(_CurrentSid)) snap[_CurrentSid] += live;
                        else snap[_CurrentSid] = live;
                    }
                }
                var sorted = new List<KeyValuePair<int, long>>(snap);
                sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
                if (sorted.Count > max) sorted.RemoveRange(max, sorted.Count - max);
                return sorted.ToArray();
            }
        }

        public KeyValuePair<int, long>[] GetCaidEcm()
        {
            lock (_Lock)
            {
                var sorted = new List<KeyValuePair<int, long>>(_CaidEcm);
                sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
                return sorted.ToArray();
            }
        }

        public Snapshot GetSnapshot()
        {
            lock (_Lock)
            {
                int avg = 0;
                if (_LatencyCount > 0)
                {
                    long sum = 0;
                    for (int i = 0; i < _LatencyCount; i++) sum += _LatencyWindow[i];
                    avg = (int)(sum / _LatencyCount);
                }

                return new Snapshot
                {
                    CwTotal = _CwTotal,
                    CwEven = _CwEven,
                    CwOdd = _CwOdd,
                    EcmTotal = _EcmTotal,
                    LastEcmMs = _LastEcmMs,
                    AvgEcmMs = avg,
                    MaxEcmMs = _MaxEcmMs,
                    LastCwAt = _LastCwAt,
                    RecentEcm = _History.ToArray()
                };
            }
        }

        public void Reset()
        {
            lock (_Lock)
            {
                _CwTotal = _CwEven = _CwOdd = 0;
                _EcmTotal = 0;
                _LastEcmMs = _MaxEcmMs = 0;
                _LatencyCount = _LatencyCursor = 0;
                Array.Clear(_LatencyWindow, 0, _LatencyWindow.Length);
                _History.Clear();
                _LastCwAt = DateTime.MinValue;
            }
        }

        public struct EcmEvent
        {
            public DateTime When;
            public int CaId;
            public int Pid;
            public int EcmTimeMs;
            public string Reader;
            public string Protocol;
            public int Hops;
        }

        public sealed class Snapshot
        {
            public long CwTotal;
            public long CwEven;
            public long CwOdd;
            public long EcmTotal;
            public int LastEcmMs;
            public int AvgEcmMs;
            public int MaxEcmMs;
            public DateTime LastCwAt;
            public EcmEvent[] RecentEcm;
        }
    }
}
