using dvbapiNet.Log;
using dvbapiNet.Log.Locale;
using System;
using System.Runtime.InteropServices;

namespace dvbapiNet.Dvb.Crypto.Algo
{
    /// <summary>
    /// Implementiert DVB CSA für iCAM/VideoGuard 64-bit CWs (softcsa).
    /// Verwendet set_even/odd_control_word_alt aus FFDecsa.dll, die keine Paritätskorrektur vornehmen.
    /// </summary>
    public sealed class DvbAltCsa : IDescramblerAlgo
    {
        private const string cLogSection = "descr dvbaltcsa";

        private IntPtr[] _Cluster;

        private int _CurrentBatchIndex;

        private byte[] _CwEven;

        private byte[] _CwOdd;

        private int _MaxPacketsAtOnce;

        private IntPtr _PtrKeySet;

        public DvbAltCsa()
        {
            _MaxPacketsAtOnce = 256;
            _CwEven = new byte[8];
            _CwOdd = new byte[8];
            _PtrKeySet = GetKeySet();
            _CurrentBatchIndex = 0;

            _Cluster = new IntPtr[(_MaxPacketsAtOnce << 1) + 2];
        }

        public void AddToBatch(IntPtr tsPacket)
        {
            int pos = _CurrentBatchIndex << 1;
            _Cluster[pos] = tsPacket;
            _Cluster[pos + 1] = tsPacket + 188;
            _CurrentBatchIndex++;

            if (_CurrentBatchIndex >= _MaxPacketsAtOnce)
                DescrambleBatch();
        }

        public void DescrambleBatch()
        {
            if (_CurrentBatchIndex == 0)
                return;

            int pos = _CurrentBatchIndex << 1;

            _Cluster[pos] = IntPtr.Zero;
            _Cluster[pos + 1] = IntPtr.Zero;

            while (Decrypt(_PtrKeySet, _Cluster) > 0) ;

            _CurrentBatchIndex = 0;
        }

        public void DescrambleSingle(IntPtr tsPacket)
        {
            IntPtr[] cl = new IntPtr[3];

            cl[0] = tsPacket;
            cl[1] = tsPacket + 188;
            cl[2] = IntPtr.Zero;

            Decrypt(_PtrKeySet, cl);
        }

        public void Dispose()
        {
            FreeKeySet(_PtrKeySet);
        }

        public void SetDescramblerData(DescramblerParity parity, DescramblerDataType type, byte[] data)
        {
            if (type == DescramblerDataType.Key)
            {
                switch (parity)
                {
                    case DescramblerParity.Even:
                        Array.Copy(data, _CwEven, _CwEven.Length);
                        SetEvenControlWord(_PtrKeySet, _CwEven);
                        break;

                    case DescramblerParity.Odd:
                        Array.Copy(data, _CwOdd, _CwOdd.Length);
                        SetOddControlWord(_PtrKeySet, _CwOdd);
                        break;

                    default:
                        LogProvider.Add(DebugLevel.Warning, cLogSection, Message.CsaUnknownParity, parity);
                        return;
                }
            }
            else
            {
                LogProvider.Add(DebugLevel.Warning, cLogSection, Message.CsaInvalidDescrDataType, type);
            }
        }

        public void SetDescramblerMode(DescramblerMode mode)
        {
            // nicht unterstützt, ignorieren.
        }

        [DllImport("FFDecsa.dll", EntryPoint = "decrypt_packets", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Decrypt(IntPtr keySet, IntPtr[] cluster);

        [DllImport("FFDecsa.dll", EntryPoint = "free_key_struct", CallingConvention = CallingConvention.Cdecl)]
        private static extern void FreeKeySet(IntPtr keySet);

        [DllImport("FFDecsa.dll", EntryPoint = "get_key_struct", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetKeySet();

        /// <summary>
        /// Setzt even CW ohne Paritätskorrektur (für softcsa 64-bit CWs).
        /// </summary>
        [DllImport("FFDecsa.dll", EntryPoint = "set_even_control_word_alt", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SetEvenControlWord(IntPtr keySet, byte[] cw);

        /// <summary>
        /// Setzt odd CW ohne Paritätskorrektur (für softcsa 64-bit CWs).
        /// </summary>
        [DllImport("FFDecsa.dll", EntryPoint = "set_odd_control_word_alt", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SetOddControlWord(IntPtr keySet, byte[] cw);
    }
}
