using System.Collections.Generic;
using System.Linq;

namespace Reown.Core.Common.Utils
{
    public class DictionaryComparer<TKey, TValue> : IEqualityComparer<Dictionary<TKey, TValue>>
    {
        private readonly IEqualityComparer<TValue> _valueComparer;

        public DictionaryComparer(IEqualityComparer<TValue> valueComparer = null)
        {
            _valueComparer = valueComparer ?? EqualityComparer<TValue>.Default;
        }

        public bool Equals(Dictionary<TKey, TValue> x, Dictionary<TKey, TValue> y)
        {
            if (x.Count != y.Count)
                return false;
            if (x.Keys.Except(y.Keys).Any())
                return false;
            if (y.Keys.Except(x.Keys).Any())
                return false;
            foreach (var pair in x)
                if (!_valueComparer.Equals(pair.Value, y[pair.Key]))
                    return false;
            return true;
        }

        public int GetHashCode(Dictionary<TKey, TValue> obj)
        {
            var hash = 0;
            foreach (var pair in obj)
            {
                hash ^= pair.Key.GetHashCode();
                hash ^= _valueComparer.GetHashCode(pair.Value);
            }

            return hash;
        }
    }
}