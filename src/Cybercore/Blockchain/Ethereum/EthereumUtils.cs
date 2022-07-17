using System;

namespace Cybercore.Blockchain.Ethereum
{
    public class EthereumUtils
    {
        public static void DetectNetworkAndChain(string netVersionResponse, string gethChainResponse,
            out EthereumNetworkType networkType, out GethChainType chainType)
        {
            if (int.TryParse(netVersionResponse, out var netWorkTypeInt))
            {
                networkType = (EthereumNetworkType)netWorkTypeInt;

                if (!Enum.IsDefined(typeof(EthereumNetworkType), networkType))
                    networkType = EthereumNetworkType.Unknown;
            }

            else
                networkType = EthereumNetworkType.Unknown;

            if (!Enum.TryParse(gethChainResponse, true, out chainType))
            {
                chainType = GethChainType.Unknown;
            }

            if (chainType == GethChainType.Ethereum)
                chainType = GethChainType.Ethereum;

            if (chainType == GethChainType.Callisto)
                chainType = GethChainType.Callisto;
        }
    }
}