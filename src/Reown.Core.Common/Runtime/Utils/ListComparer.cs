using System.Collections.Generic;

namespace Reown.Core.Common.Utils
{
    public class ListComparer<T> : IEqualityComparer<List<T>>
    {
        private readonly IEqualityComparer<T> _valueComparer;

        public ListComparer(IEqualityComparer<T> valueComparer = null)
        {
            _valueComparer = valueComparer ?? EqualityComparer<T>.Default;
        }

        public bool Equals(List<T> x, List<T> y)
        {
            return x.SetEquals(y, _valueComparer);
        }

        public int GetHashCode(List<T> obj)
        {
            var hash = 0;
            foreach (var item in obj)
            {
                hash ^= _valueComparer.GetHashCode(item);
            }

            return hash;
        }
    }
}