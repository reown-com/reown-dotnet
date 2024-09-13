using System;
using Newtonsoft.Json;

namespace Reown.WalletKit.Models
{
    public class BaseEventArgs<T> : EventArgs
    {
        [JsonProperty("id")]
        public long Id;
    
        [JsonProperty("topic")]
        public string Topic { get; set; }
    
        [JsonProperty("params")]
        public T Parameters { get; set; }
    }
}
