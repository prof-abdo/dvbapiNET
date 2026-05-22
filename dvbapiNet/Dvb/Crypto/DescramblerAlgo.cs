namespace dvbapiNet.Dvb.Crypto
{
    /// <summary>
    /// Auflistung Verfügbarer Descrambler-Algorithmen
    /// </summary>
    public enum DescramblerAlgo : int
    {
        DvbCsa = 0,
        Des = 1,
        Aes128 = 2,
        DvbAltCsa = 3,    // softcsa - iCAM/VideoGuard 64-bit CW (CA_ALGO_DVBCSA_SOFTCSA)
    }
}
