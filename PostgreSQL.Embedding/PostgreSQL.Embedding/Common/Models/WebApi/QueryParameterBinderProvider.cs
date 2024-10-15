using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace PostgreSQL.Embedding.Common.Models.WebApi
{
    public class QueryParameterBinderProvider : IModelBinderProvider
    {
        public IModelBinder GetBinder(ModelBinderProviderContext context)
        {
            if (context.Metadata.ModelType.IsGenericType &&
                context.Metadata.ModelType.GetGenericTypeDefinition() == typeof(QueryParameter<,>))
            {
                var entityType = context.Metadata.ModelType.GetGenericArguments()[0];
                var filterType = context.Metadata.ModelType.GetGenericArguments()[1];
                var binderType = typeof(QueryParameterBinder<,>).MakeGenericType(entityType, filterType);
                return (IModelBinder)Activator.CreateInstance(binderType);
            }

            return null;
        }
    }
}
