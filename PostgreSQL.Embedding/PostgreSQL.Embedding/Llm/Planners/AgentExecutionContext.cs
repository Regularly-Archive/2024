using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace PostgreSQL.Embedding.Llm.Planners
{
    public class AgentExecutionContext
    {
        private readonly AsyncLocal<ImmutableDictionary<string, object>> _asyncLocalContext = new AsyncLocal<ImmutableDictionary<string, object>>();
        private readonly ConcurrentDictionary<string, object> _globalContext = new ConcurrentDictionary<string, object>();

        public void SetData(string key, object value)
        {
            var current = _asyncLocalContext.Value ?? ImmutableDictionary<string, object>.Empty;
            _asyncLocalContext.Value = current.SetItem(key, value);
        }

        public T GetData<T>(string key)
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

        public void SetGlobalData(string key, object value)
        {
            _globalContext.AddOrUpdate(key, value, (k, oldValue) => value);
        }
    }
}
