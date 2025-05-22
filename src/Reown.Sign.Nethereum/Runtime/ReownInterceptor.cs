using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nethereum.JsonRpc.Client;
using Nethereum.RPC;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.RPC.HostWallet;
using Reown.Sign.Nethereum.Model;

namespace Reown.Sign.Nethereum
{
    public class ReownInterceptor : RequestInterceptor
    {
        private readonly ReownSignService _reownSignService;

        public readonly HashSet<string> SignMethods = new()
        {
            nameof(ApiMethods.eth_sendTransaction),
            nameof(ApiMethods.personal_sign),
            nameof(ApiMethods.eth_signTypedData_v4),
            nameof(ApiMethods.wallet_switchEthereumChain),
            nameof(ApiMethods.wallet_addEthereumChain),
            nameof(ApiMethods.wallet_grantPermissions),
            nameof(ApiMethods.wallet_revokePermissions)
        };

        public ReownInterceptor(ReownSignService reownSignService)
        {
            _reownSignService = reownSignService;
        }

        public override async Task<object> InterceptSendRequestAsync<T>(
            Func<RpcRequest, string, Task<T>> interceptedSendRequestAsync,
            RpcRequest request,
            string route = null)
        {
            if (!SignMethods.Contains(request.Method))
            {
                return await base
                    .InterceptSendRequestAsync(interceptedSendRequestAsync, request, route)
                    .ConfigureAwait(false);
            }

            if (!_reownSignService.IsWalletConnected)
                throw new InvalidOperationException("[ReownInterceptor] Wallet is not connected");

            if (_reownSignService.IsMethodSupported(request.Method))
            {
                if (request.Method == nameof(ApiMethods.eth_sendTransaction))
                {
                    return await _reownSignService.SendTransactionAsync((TransactionInput)request.RawParameters[0]);
                }

                if (request.Method == nameof(ApiMethods.personal_sign))
                {
                    if (request.RawParameters.Length == 1)
                        return await _reownSignService.PersonalSignAsync((string)request.RawParameters[0]);

                    return await _reownSignService.PersonalSignAsync((string)request.RawParameters[0], (string)request.RawParameters[1]);
                }

                if (request.Method == nameof(ApiMethods.eth_signTypedData_v4))
                {
                    if (request.RawParameters.Length == 1)
                        return await _reownSignService.EthSignTypedDataV4Async((string)request.RawParameters[0]);

                    return await _reownSignService.EthSignTypedDataV4Async((string)request.RawParameters[1], (string)request.RawParameters[0]);
                }

                if (request.Method == nameof(ApiMethods.wallet_switchEthereumChain))
                {
                    return await _reownSignService.WalletSwitchEthereumChainAsync((SwitchEthereumChainParameter)request.RawParameters[0]);
                }

                if (request.Method == nameof(ApiMethods.wallet_addEthereumChain))
                {
                    return await _reownSignService.WalletAddEthereumChainAsync((AddEthereumChainParameter)request.RawParameters[0]);
                }

                throw new NotImplementedException();
            }

            return await base
                .InterceptSendRequestAsync(interceptedSendRequestAsync, request, route)
                .ConfigureAwait(false);
        }

        public override async Task<object> InterceptSendRequestAsync<T>(
            Func<string, string, object[], Task<T>> interceptedSendRequestAsync,
            string method,
            string route = null,
            params object[] paramList)
        {
            if (!SignMethods.Contains(method))
            {
                return await base
                    .InterceptSendRequestAsync(interceptedSendRequestAsync, method, route, paramList)
                    .ConfigureAwait(false);
            }

            if (!_reownSignService.IsWalletConnected)
                throw new InvalidOperationException("[ReownInterceptor] Wallet is not connected");

            if (_reownSignService.IsMethodSupported(method))
            {
                if (method == nameof(ApiMethods.eth_sendTransaction))
                {
                    return await _reownSignService.SendTransactionAsync((TransactionInput)paramList[0]);
                }

                if (method == nameof(ApiMethods.personal_sign))
                {
                    if (paramList.Length == 1)
                        return await _reownSignService.PersonalSignAsync((string)paramList[0]);

                    return await _reownSignService.PersonalSignAsync((string)paramList[0], (string)paramList[1]);
                }

                if (method == nameof(ApiMethods.eth_signTypedData_v4))
                {
                    if (paramList.Length == 1)
                        return await _reownSignService.EthSignTypedDataV4Async((string)paramList[0]);

                    return await _reownSignService.EthSignTypedDataV4Async((string)paramList[1], (string)paramList[0]);
                }

                if (method == nameof(ApiMethods.wallet_switchEthereumChain))
                {
                    try
                    {
                        return await _reownSignService.WalletSwitchEthereumChainAsync((SwitchEthereumChain)paramList[0]);
                    }
                    catch (InvalidCastException)
                    {
                        return await _reownSignService.WalletSwitchEthereumChainAsync((SwitchEthereumChainParameter)paramList[0]);
                    }
                }

                if (method == nameof(ApiMethods.wallet_addEthereumChain))
                {
                    try
                    {
                        return await _reownSignService.WalletAddEthereumChainAsync((EthereumChain)paramList[0]);
                    }
                    catch (InvalidCastException)
                    {
                        return await _reownSignService.WalletAddEthereumChainAsync((AddEthereumChainParameter)paramList[0]);
                    }
                }

                if (method == nameof(ApiMethods.wallet_grantPermissions))
                {
                    return await _reownSignService.WalletRequestPermissionsAsync((PermissionsRequest)paramList[0]);
                }

                throw new NotImplementedException();
            }

            return await base
                .InterceptSendRequestAsync(interceptedSendRequestAsync, method, route, paramList)
                .ConfigureAwait(false);
        }
    }
}