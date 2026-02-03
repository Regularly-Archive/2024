using Castle.DynamicProxy;

namespace PostgreSQL.Embedding.Infrastructure.DataAccess
{
    /// <summary>
    /// Repository 代理工厂，用于创建带有数据隔离功能的 Repository 代理
    /// </summary>
    public interface IRepositoryProxyFactory
    {
        /// <summary>
        /// 为 Repository 创建代理实例
        /// </summary>
        IRepository<T> CreateProxy<T>(IRepository<T> implementation) where T : Domain.Entities.BaseEntity, new();
    }

    public class RepositoryProxyFactory : IRepositoryProxyFactory
    {
        private readonly ProxyGenerator _proxyGenerator;
        private readonly IDataIsolationService _dataIsolationService;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public RepositoryProxyFactory(
            IDataIsolationService dataIsolationService,
            IHttpContextAccessor httpContextAccessor)
        {
            _proxyGenerator = new ProxyGenerator();
            _dataIsolationService = dataIsolationService;
            _httpContextAccessor = httpContextAccessor;
        }

        public IRepository<T> CreateProxy<T>(IRepository<T> implementation) where T : Domain.Entities.BaseEntity, new()
        {
            var interceptor = new RepositoryInterceptor(_dataIsolationService, _httpContextAccessor);

            // 为接口创建代理
            var proxy = _proxyGenerator.CreateInterfaceProxyWithTarget(
                implementation,
                interceptor);

            return proxy;
        }
    }
}
