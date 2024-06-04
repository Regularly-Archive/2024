using HtmlAgilityPack;
using System.Collections.Generic;
using System.Threading;

namespace PostgreSQL.Embedding.Common.Models
{
    public class AsyncEnumerable<T> : IAsyncEnumerable<T> where T : class
    {
        private readonly IEnumerable<T> _items;
        public AsyncEnumerable(IEnumerable<T> items)
        {
            _items = items;
        }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new AsyncEnumerator<T>(_items.ToArray(), cancellationToken);
        }
    }

    public class AsyncEnumerator<T> : IAsyncEnumerator<T> where T : class
    {
        public T Current => _array[_index];

        private readonly T[] _array;
        private readonly CancellationToken _cancellationToken;
        private int _index = -1;
        public AsyncEnumerator(T[] array, CancellationToken cancellationToken)
        {
            _array = array;
            _cancellationToken = cancellationToken;
        }

        public ValueTask DisposeAsync()
        {
            return default;
        }

        public ValueTask<bool> MoveNextAsync()
        {
            _cancellationToken.ThrowIfCancellationRequested();
            _index++;
            return ValueTask.FromResult(_index < _array.Length);
        }
    }
}
