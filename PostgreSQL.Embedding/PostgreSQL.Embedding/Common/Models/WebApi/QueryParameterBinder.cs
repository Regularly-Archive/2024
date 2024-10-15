using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace PostgreSQL.Embedding.Common.Models.WebApi
{
    public class QueryParameterBinder<TEntity, TFilter> : IModelBinder where TFilter : class, IQueryableFilter<TEntity>, new()
    {
        private static readonly HashSet<string> ReservedParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pageindex", "pagesize", "sortby", "isdescending"
        };

        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            var queryParameter = new QueryParameter<TEntity, TFilter>();
            var values = bindingContext.ValueProvider;

            queryParameter.PageIndex = GetIntValue(values, "pageindex", QueryParameter<TEntity, TFilter>.DefaultPageIndex);
            queryParameter.PageSize = GetIntValue(values, "pagesize", QueryParameter<TEntity, TFilter>.DefaultPageSize);
            queryParameter.SortBy = GetStringValue(values, "sortby", QueryParameter<TEntity, TFilter>.DefaultSortBy);
            queryParameter.IsDescending = GetBoolValue(values, "isdescending", QueryParameter<TEntity, TFilter>.DefaultIsDescending);
            queryParameter.Filter = BindFilter(bindingContext);

            bindingContext.Result = ModelBindingResult.Success(queryParameter);
            return Task.CompletedTask;
        }

        private TFilter BindFilter(ModelBindingContext bindingContext)
        {
            var filter = new TFilter();
            var filterType = typeof(TFilter);
            var properties = filterType.GetProperties();

            foreach (var property in properties)
            {
                var key = property.Name.ToLower();
                if (!ReservedParameters.Contains(key))
                {
                    // 同时兼容平铺型以嵌套型参数
                    // ?pageIndex=1&pageSize=10&name=xxx&age=xxx
                    // ?pageIndex=1&pageSize=10&filter.name=xxx&filter.age=xxx
                    var value = bindingContext.ValueProvider.GetValue(key).FirstValue;
                    if (value == null)
                    {
                        value = bindingContext.ValueProvider.GetValue($"filter.{key}").FirstValue;
                    }
                    if (value != null)
                    {
                        object convertedValue = null;
                        var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

                        if (targetType.IsEnum)
                        {
                            convertedValue = Enum.Parse(targetType, value, true);
                        }
                        else if (targetType == typeof(bool) && bool.TryParse(value, out var _))
                        {
                            convertedValue = bool.Parse(value);
                        }
                        else if (targetType == typeof(DateTime) && DateTime.TryParse(value, out var _))
                        {
                            convertedValue = DateTime.Parse(value);
                        }
                        else
                        {
                            if (!string.IsNullOrEmpty(value.ToString())) 
                                convertedValue = Convert.ChangeType(value, targetType);
                        }

                        property.SetValue(filter, convertedValue);
                    }
                }
            }

            return filter;
        }

        private int GetIntValue(IValueProvider values, string key, int defaultValue)
        {
            return int.TryParse(values.GetValue(key).FirstValue, out int value) ? value : defaultValue;
        }

        private string GetStringValue(IValueProvider values, string key, string deafultValue)
        {
            return values.GetValue(key).FirstValue ?? deafultValue;
        }

        private bool GetBoolValue(IValueProvider values, string key, bool defaultValue)
        {
            if (bool.TryParse(values.GetValue(key).FirstValue, out bool value)) return value;
            return defaultValue;
        }
    }
}
