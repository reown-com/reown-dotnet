using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Reown.Core.Common.Utils;
using Reown.Core.Network.Models;

namespace Reown.Sign.Nethereum.Model
{
    [RpcMethod("wallet_grantPermissions")]
    [RpcRequestOptions(Clock.ONE_MINUTE, 99990)]
    public class WalletGrantPermissions : List<object>
    {
        public WalletGrantPermissions(PermissionsRequest permissionsRequest) : base(new object[]
        {
            permissionsRequest
        })
        {
        }

        [Preserve]
        public WalletGrantPermissions()
        {
        }
    }

    public class PermissionsRequest
    {
        [JsonProperty("chainId")]
        public string ChainId { get; set; }

        [JsonProperty("address")]
        public string Address { get; set; }

        [JsonProperty("expiry")]
        public long Expiry { get; set; }

        [JsonProperty("signer")]
        public Signer Signer { get; set; }

        [JsonProperty("permissions")]
        public Permission[] Permissions { get; set; }

        public PermissionsRequest(Signer signer, TimeSpan duration, string chainId, string address, params Permission[] permissions)
        {
            if (permissions.Length == 0)
            {
                throw new ArgumentException("At least one permission is required.", nameof(permissions));
            }

            Signer = signer;
            Permissions = permissions;
            Expiry = DateTimeOffset.UtcNow.Add(duration).ToUnixTimeSeconds();

            ChainId = chainId;
            Address = address;
        }
    }

    public class PermissionsResponse
    {
        [JsonProperty("context")]
        public string Context { get; set; }

        [JsonProperty("chainId")]
        public string ChainId { get; set; }

        [JsonProperty("address")]
        public string Address { get; set; }

        [JsonProperty("expiry")]
        public long Expiry { get; set; }

        [JsonProperty("permissions")]
        public Permission[] Permissions { get; set; }

        [Preserve]
        public PermissionsResponse()
        {
        }
    }

    public class Signer
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("data")]
        public Dictionary<string, object> Data { get; set; }

        public Signer(string key)
        {
            Type = "keys";
            Data = new Dictionary<string, object>
            {
                {
                    "keys", new List<Dictionary<string, string>>
                    {
                        new()
                        {
                            {
                                "type", "secp256k1"
                            },
                            {
                                "publicKey", key
                            }
                        }
                    }
                }
            };
        }
    }

    public class Permission
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("data")]
        public Dictionary<string, object> Data { get; set; }
    }

    public class Function
    {
        [JsonProperty("functionName")]
        public string FunctionName { get; set; }
    }
}