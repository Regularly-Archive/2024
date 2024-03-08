using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.LlmServices.Abstration;

namespace PostgreSQL.Embedding.LlmServices
{
    public class KernalService : IKernelService
    {
        private readonly IConfiguration _configuration;
        public KernalService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public Kernel GetKernel(long appId)
        {
            // 从数据库中查询应用信息，查询应用的服务提供商
            var llmServiceProvider = LlmServiceProvider.OpenAI;

            return null;
        }
    }
}
