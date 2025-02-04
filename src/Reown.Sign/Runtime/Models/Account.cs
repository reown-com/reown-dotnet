using System;

namespace Reown.Sign.Models
{
    // https://chainagnostic.org/CAIPs/caip-10
    [Serializable]
    public readonly struct Account : IEquatable<Account>
    {
        public string Address { get; }
        public string ChainId { get; }

        public string AccountId
        {
            get => $"{ChainId}:{Address}";
        }

        public Account(string address, string chainId)
        {
            Address = address;
            ChainId = chainId;
        }

        public Account(string accountId)
        {
            var (chainId, address) = Core.Utils.DeconstructAccountId(accountId);
            Address = address;
            ChainId = chainId;
        }

        public override string ToString()
        {
            return AccountId;
        }

        public bool Equals(Account other)
        {
            return AccountId == other.AccountId;
        }

        public override bool Equals(object obj)
        {
            return obj is Account other && Equals(other);
        }

        public override int GetHashCode()
        {
            return AccountId.GetHashCode();
        }

        public static bool operator ==(Account left, Account right)
        {
            return string.Equals(left.AccountId, right.AccountId, StringComparison.InvariantCultureIgnoreCase);
        }

        public static bool operator !=(Account left, Account right)
        {
            return !(left == right);
        }
    }
}