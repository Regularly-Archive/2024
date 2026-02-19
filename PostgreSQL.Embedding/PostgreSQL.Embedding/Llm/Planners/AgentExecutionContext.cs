using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading.Channels;
using PostgreSQL.Embedding.Common.Streaming;

namespace PostgreSQL.Embedding.Llm.Planners
{
    public class AgentExecutionContext
    {
        private readonly AsyncLocal<ImmutableDictionary<string, object>> _asyncLocalContext = new AsyncLocal<ImmutableDictionary<string, object>>();
        private readonly ConcurrentDictionary<string, object> _globalContext = new ConcurrentDictionary<string, object>();

        /// <summary>
        /// EventWriter stored in AsyncLocal to ensure isolation per async flow
        /// </summary>
        private readonly AsyncLocal<ChannelWriter<ISseEvent>?> _eventWriter = new AsyncLocal<ChannelWriter<ISseEvent>?>();

        /// <summary>
        /// Initialize the EventBus with a ChannelWriter
        /// </summary>
        public void InitializeEventBus(ChannelWriter<ISseEvent> writer)
        {
            _eventWriter.Value = writer;
        }

        /// <summary>
        /// Publish an event through the EventBus
        /// </summary>
        public async Task PublishEventAsync(ISseEvent evt, CancellationToken ct = default)
        {
            var writer = _eventWriter.Value;
            if (writer != null)
            {
                await writer.WriteAsync(evt, ct);
            }
        }

        /// <summary>
        /// Check if EventBus is available
        /// </summary>
        public bool HasEventBus => _eventWriter.Value != null;

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

        public T GetGlobalData<T>(string key)
        {
            if (_globalContext.TryGetValue(key, out object value))
                return (T)value;

            return default(T);
        }
    }
}
