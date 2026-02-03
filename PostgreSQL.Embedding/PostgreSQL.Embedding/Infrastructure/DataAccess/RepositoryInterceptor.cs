using Castle.DynamicProxy;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Domain.Entities;
using System.Linq.Expressions;
using System.Reflection;

namespace PostgreSQL.Embedding.Infrastructure.DataAccess
{
    /// <summary>
    /// Repository 拦截器，用于自动注入数据隔离条件
    /// </summary>
    public class RepositoryInterceptor : IInterceptor
    {
        private readonly IDataIsolationService _dataIsolationService;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public RepositoryInterceptor(IDataIsolationService dataIsolationService, IHttpContextAccessor httpContextAccessor)
        {
            _dataIsolationService = dataIsolationService;
            _httpContextAccessor = httpContextAccessor;
        }

        public void Intercept(IInvocation invocation)
        {
            var methodName = invocation.Method.Name;
            var targetType = invocation.TargetType;

            // 检查是否需要数据隔离
            var entityType = GetEntityType(targetType);
            if (entityType == null || !_dataIsolationService.ShouldIsolate(entityType))
            {
                invocation.Proceed();
                return;
            }

            // 获取当前用户
            var currentUserId = _dataIsolationService.GetCurrentUserId();
            if (string.IsNullOrEmpty(currentUserId))
            {
                // 如果没有用户上下文，记录警告并继续（可能是系统调用）
                invocation.Proceed();
                return;
            }

            // 根据方法名处理
            switch (methodName)
            {
                case nameof(IRepository<BaseEntity>.FindListAsync):
                    // 只有 Expression<Func<...>> 参数的重载才拦截
                    if (invocation.Arguments.Length > 0 &&
                        invocation.Arguments[0] is LambdaExpression)
                    {
                        InterceptFindList(invocation, currentUserId);
                    }
                    else
                    {
                        invocation.Proceed();
                    }
                    break;

                case nameof(IRepository<BaseEntity>.FindAsync):
                    InterceptFindAsync(invocation, currentUserId);
                    break;

                case nameof(IRepository<BaseEntity>.CountAsync):
                    InterceptCount(invocation, currentUserId);
                    break;

                case nameof(IRepository<BaseEntity>.ExistsAsync):
                    InterceptExists(invocation, currentUserId);
                    break;

                case nameof(IRepository<BaseEntity>.PaginateAsync):
                    // 只有 Expression<Func<...>, int, int> 参数的重载才拦截
                    if (invocation.Arguments.Length >= 2 &&
                        invocation.Arguments[0] is LambdaExpression)
                    {
                        InterceptPaginate(invocation, currentUserId);
                    }
                    else
                    {
                        invocation.Proceed();
                    }
                    break;

                case nameof(IRepository<BaseEntity>.GetAllAsync):
                    // GetAllAsync() 需要转换为带过滤的调用
                    InterceptGetAll(invocation, currentUserId);
                    break;

                default:
                    invocation.Proceed();
                    break;
            }
        }

        private Type? GetEntityType(Type repositoryType)
        {
            // IRepository<T> 中的 T 就是实体类型
            var interfaceType = repositoryType.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRepository<>));

            return interfaceType?.GetGenericArguments().FirstOrDefault();
        }

        private void InterceptFindList(IInvocation invocation, string currentUserId)
        {
            var predicateArg = invocation.Arguments[0] as LambdaExpression;

            if (predicateArg == null)
            {
                // 没有 predicate，创建一个
                var param = Expression.Parameter(invocation.TargetType.GetGenericArguments()[0], "x");
                predicateArg = Expression.Lambda(Expression.Constant(true), param);
            }

            var newPredicate = CombinePredicates(predicateArg, currentUserId);
            invocation.Arguments[0] = newPredicate;
            invocation.Proceed();
        }

        private void InterceptFindAsync(IInvocation invocation, string currentUserId)
        {
            var predicateArg = invocation.Arguments[0] as LambdaExpression;
            var newPredicate = CombinePredicates(predicateArg!, currentUserId);
            invocation.Arguments[0] = newPredicate;
            invocation.Proceed();
        }

        private void InterceptCount(IInvocation invocation, string currentUserId)
        {
            var predicateArg = invocation.Arguments[0] as LambdaExpression;
            if (predicateArg == null)
            {
                var param = Expression.Parameter(invocation.TargetType.GetGenericArguments()[0], "x");
                predicateArg = Expression.Lambda(Expression.Constant(true), param);
            }
            var newPredicate = CombinePredicates(predicateArg, currentUserId);
            invocation.Arguments[0] = newPredicate;
            invocation.Proceed();
        }

        private void InterceptExists(IInvocation invocation, string currentUserId)
        {
            var predicateArg = invocation.Arguments[0] as LambdaExpression;
            var newPredicate = CombinePredicates(predicateArg!, currentUserId);
            invocation.Arguments[0] = newPredicate;
            invocation.Proceed();
        }

        private void InterceptPaginate(IInvocation invocation, string currentUserId)
        {
            var predicateArg = invocation.Arguments[0] as LambdaExpression;
            if (predicateArg == null)
            {
                var param = Expression.Parameter(invocation.TargetType.GetGenericArguments()[0], "x");
                predicateArg = Expression.Lambda(Expression.Constant(true), param);
            }
            var newPredicate = CombinePredicates(predicateArg, currentUserId);
            invocation.Arguments[0] = newPredicate;
            invocation.Proceed();
        }

        private void InterceptGetAll(IInvocation invocation, string currentUserId)
        {
            // GetAllAsync() -> 创建一个带过滤的调用
            var entityType = invocation.TargetType.GetGenericArguments()[0];
            var param = Expression.Parameter(entityType, "x");
            var ownerProperty = Expression.Property(param, "CreatedBy");
            var userConstant = Expression.Constant(currentUserId);
            var equalsExpression = Expression.Equal(ownerProperty, userConstant);
            var lambda = Expression.Lambda(equalsExpression, param);

            // 调用 FindListAsync
            var findListMethod = typeof(IRepository<>).MakeGenericType(entityType)
                .GetMethods()
                .First(m => m.Name == nameof(IRepository<BaseEntity>.FindListAsync)
                    && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(Expression<>));

            invocation.ReturnValue = findListMethod.Invoke(invocation.InvocationTarget, new[] { lambda });
        }

        private LambdaExpression CombinePredicates(LambdaExpression existingPredicate, string currentUserId)
        {
            var entityType = existingPredicate.Parameters[0].Type;
            var param = existingPredicate.Parameters[0];

            // 获取 DataIsolationAttribute 配置的字段名
            var attr = entityType.GetCustomAttribute<DataIsolationAttribute>();
            var ownerField = attr?.OwnerField ?? "CreatedBy";

            // 创建 CreatedBy == currentUserId 的表达式
            var ownerProperty = Expression.Property(param, ownerField);
            var userConstant = Expression.Constant(currentUserId);
            var equalsExpression = Expression.Equal(ownerProperty, userConstant);

            // 合并：existingPredicate AND (CreatedBy == currentUserId)
            var andAlsoExpression = Expression.AndAlso(existingPredicate.Body, equalsExpression);

            return Expression.Lambda(andAlsoExpression, param);
        }
    }
}
