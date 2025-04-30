using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace PostgreSQL.Embedding.Planners
{
    public class AgentExecutionContext
    {
        private static readonly AsyncLocal<ImmutableDictionary<string, object>> _asyncLocalContext = new AsyncLocal<ImmutableDictionary<string, object>>();
        private static readonly ConcurrentDictionary<string, object> _globalContext = new ConcurrentDictionary<string, object>();

        public static void SetData(string key, object value)
        {
            var current = _asyncLocalContext.Value ?? ImmutableDictionary<string, object>.Empty;
            _asyncLocalContext.Value = current.SetItem(key, value);
        }

        public static T GetData<T>(string key)
        {
            if (_asyncLocalContext.Value?.TryGetValue(key, out object asyncValue) ?? false)
            {
                return (T)asyncValue;
            }

            if (_globalContext.TryGetValue(key, out object globalValue))
            {
                return (T)globalValue;
            }

            return default;
        }

        public static void SetGlobalData(string key, object value)
        {
            _globalContext.AddOrUpdate(key, value, (k, oldValue) => value);
        }

        public static IDisposable CreateScope()
        {
            var parentContext = _asyncLocalContext.Value;
            return new ContextScope(parentContext);
        }

        private class ContextScope : IDisposable
        {
            private readonly ImmutableDictionary<string, object> _originalContext;

            public ContextScope(ImmutableDictionary<string, object> originalContext)
            {
                _originalContext = originalContext;
                _asyncLocalContext.Value = originalContext;
            }

            public void Dispose()
            {
                _asyncLocalContext.Value = _originalContext;
            }
        }
    }
}
