using System;
using Newtonsoft.Json;

namespace Reown.AppKit.Unity.Model.BlockchainApi
{
    [Serializable]
    public sealed class GetIdentityResponse
    {
        public string Name { get; }
        public string Avatar { get; }
        
        [JsonConstructor]
        public GetIdentityResponse(string name, string avatar)
        {
            Name = name;
            Avatar = avatar;
        }
    }
}