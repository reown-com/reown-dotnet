using System;
using System.Numerics;

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
        public string ContractAddress { get; set; }
        public string ContractAbi { get; set; }
        public string MethodName { get; set; }
        public object[] Arguments { get; set; }
    }

    public class WriteContractParams
    {
        public string ContractAddress { get; set; }
        public string ContractAbi { get; set; }
        public string MethodName { get; set; }
        public BigInteger Value { get; set; }
        public BigInteger Gas { get; set; }
        public object[] Arguments { get; set; }
    }


    // -- Transaction Models --------------------------------------------

    public class SendTransactionParams
    {
        public string AddressTo { get; set; }
        public BigInteger Value { get; set; }
        public string Data { get; set; }
    }
}