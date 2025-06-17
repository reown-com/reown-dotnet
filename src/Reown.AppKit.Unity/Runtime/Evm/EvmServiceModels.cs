using System.Numerics;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Newtonsoft.Json;

namespace Reown.AppKit.Unity
{
    // -- Verification Models ---------------------------------------------

    public class VerifyMessageSignatureParams
    {
        public string Address { get; set; }
        public string Message { get; set; }
        public string Signature { get; set; }
    }

    public class VerifyTypedDataSignatureParams
    {
        public string Address { get; set; }
        public string Data { get; set; }
        public string Signature { get; set; }
    }


    // -- Contract Interaction Models ------------------------------------

    public class ReadContractParams
    {
        /// <summary>
        ///     Address of the contract to interact with
        /// </summary>
        public string ContractAddress { get; set; }

        /// <summary>
        ///     Contract ABI in JSON or human-readable format
        /// </summary>
        public string ContractAbi { get; set; }

        /// <summary>
        ///     Name of the method to call on the contract
        /// </summary>
        public string MethodName { get; set; }

        /// <summary>
        ///     Arguments to pass to the contract method
        /// </summary>
        public object[] Arguments { get; set; }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }
    }

    public class WriteContractParams
    {
        /// <summary>
        ///     Address of the contract to interact with
        /// </summary>
        public string ContractAddress { get; set; }

        /// <summary>
        ///     Contract ABI in JSON or human-readable format
        /// </summary>
        public string ContractAbi { get; set; }

        /// <summary>
        ///     Name of the method to call on the contract
        /// </summary>
        public string MethodName { get; set; }

        /// <summary>
        ///     Value in wei to send with the transaction
        /// </summary>
        public BigInteger Value { get; set; }

        /// <summary>
        ///     Gas amount for the transaction
        /// </summary>
        public BigInteger Gas { get; set; }

        /// <summary>
        ///     Arguments to pass to the contract method
        /// </summary>
        public object[] Arguments { get; set; }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }
    }


    // -- Transaction Models --------------------------------------------

    public class SendTransactionParams
    {
        /// <summary>
        ///     Recipient address of the transaction
        /// </summary>
        public string AddressTo { get; set; }

        /// <summary>
        ///     Value in wei to be transferred
        /// </summary>
        public BigInteger Value { get; set; }

        /// <summary>
        ///     The compiled code of a contract OR the hash of the invoked method signature and encoded parameters
        /// </summary>
        public string Data { get; set; }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }
    }

    public class TransactionReceipt
    {
        /// <summary>
        ///     Hash of this transaction
        /// </summary>
        [JsonProperty("transactionHash")]
        public string TransactionHash { get; set; }

        /// <summary>
        ///     Index of this transaction in the block
        /// </summary>
        [JsonProperty("transactionIndex")]
        public HexBigInteger TransactionIndex { get; set; }

        /// <summary>
        ///     Hash of block containing this transaction
        /// </summary>
        [JsonProperty("blockHash")]
        public string BlockHash { get; set; }

        /// <summary>
        ///     Number of block containing this transaction
        /// </summary>
        [JsonProperty("blockNumber")]
        public HexBigInteger BlockNumber { get; set; }

        /// <summary>
        ///     Transaction sender
        /// </summary>
        [JsonProperty("from")]
        public string From { get; set; }

        /// <summary>
        ///     Transaction recipient or null if deploying a contract
        /// </summary>
        [JsonProperty("to")]
        public string To { get; set; }

        /// <summary>
        ///     Gas used by this and all preceding transactions in this block
        /// </summary>
        [JsonProperty("cumulativeGasUsed")]
        public HexBigInteger CumulativeGasUsed { get; set; }

        /// <summary>
        ///     Gas used by this transaction
        /// </summary>
        [JsonProperty("gasUsed")]
        public HexBigInteger GasUsed { get; set; }

        /// <summary>
        ///     Pre-London, it is equal to the transaction's gasPrice. Post-London, it is equal to the actual gas price paid for inclusion.
        /// </summary>
        [JsonProperty("effectiveGasPrice")]
        public HexBigInteger EffectiveGasPrice { get; set; }

        /// <summary>
        ///     Address of new contract or null if no contract was created
        /// </summary>
        [JsonProperty("contractAddress")]
        public string ContractAddress { get; set; }

        /// <summary>
        ///     True if this transaction was successful or false if it failed
        /// </summary>
        [JsonProperty("statusSuccessful")]
        public bool StatusSuccessful { get; set; }

        /// <summary>
        ///     List of log objects generated by this transaction
        /// </summary>
        [JsonProperty("logs")]
        public FilterLog[] Logs { get; set; }

        /// <summary>
        ///     Transaction type
        /// </summary>
        [JsonProperty("type")]
        public HexBigInteger Type { get; set; }

        /// <summary>
        ///     Logs bloom filter
        /// </summary>
        [JsonProperty("logsBloom")]
        public string LogsBloom { get; set; }

        /// <summary>
        ///     The post-transaction state root. Only specified for transactions included before the Byzantium upgrade.
        /// </summary>
        [JsonProperty("root")]
        public string Root { get; set; }

        public TransactionReceipt()
        {
        }

        public TransactionReceipt(Nethereum.RPC.Eth.DTOs.TransactionReceipt receipt)
        {
            TransactionHash = receipt.TransactionHash;
            TransactionIndex = receipt.TransactionIndex;
            BlockHash = receipt.BlockHash;
            BlockNumber = receipt.BlockNumber;
            From = receipt.From;
            To = receipt.To;
            CumulativeGasUsed = receipt.CumulativeGasUsed;
            GasUsed = receipt.GasUsed;
            EffectiveGasPrice = receipt.EffectiveGasPrice;
            StatusSuccessful = receipt.Status != new HexBigInteger(0);
            Logs = receipt.Logs;
            Type = receipt.Type;
            LogsBloom = receipt.LogsBloom;
            Root = receipt.Root;
        }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }
    }
}