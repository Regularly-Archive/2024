using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Moq;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Extensions;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Infrastructure.DataAccess;
using PostgreSQL.Embedding.Llm.Planners;
using PostgreSQL.Embedding.Llm.Services;
using PostgreSQL.Embedding.Plugins.BuiltIn;
using PostgreSQL.Embedding.Plugins.Abstration;
using Shouldly;
using System.Security.Claims;
using Xunit;

namespace Wikit.Tests.Mcp
{
    /// <summary>
    /// Tests for UseMCPPlugin
    /// </summary>
    public class UseMCPPlugin_Tests
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly Mock<ILoggerFactory> _mockLoggerFactory;
        private readonly Mock<ILogger<UseMCPPlugin>> _mockLogger;
        private readonly Mock<IRepository<MCPServer>> _mockMcpServerRepository;
        private readonly Mock<McpConnectionFactory> _mockCacheableMcpClientFactory;
        private readonly Mock<PromptTemplateService> _mockPromptTemplateService;

        public UseMCPPlugin_Tests()
        {
            _mockLoggerFactory = new Mock<ILoggerFactory>(MockBehavior.Loose);
            _mockLogger = new Mock<ILogger<UseMCPPlugin>>(MockBehavior.Loose);
            _mockMcpServerRepository = new Mock<IRepository<MCPServer>>(MockBehavior.Loose);
            _mockCacheableMcpClientFactory = new Mock<McpConnectionFactory>(MockBehavior.Loose, Mock.Of<IServiceProvider>());
            _mockPromptTemplateService = new Mock<PromptTemplateService>(MockBehavior.Loose);

            _mockLoggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>()))
                .Returns(_mockLogger.Object);

            var services = new ServiceCollection();
            services.AddSingleton(_mockLoggerFactory.Object);
            services.AddSingleton(_mockMcpServerRepository.Object);
            services.AddSingleton(_mockPromptTemplateService.Object);

            // BasePlugin requires IHttpContextAccessor
            var httpContext = new DefaultHttpContext();
            var httpContextAccessor = new Mock<IHttpContextAccessor>(MockBehavior.Loose);
            httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
            services.AddSingleton(httpContextAccessor.Object);

            // Create a real CacheableMcpClientFactory with mock logger factory
            var realLoggerFactory = new Mock<ILoggerFactory>(MockBehavior.Loose);
            realLoggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>()))
                .Returns(Mock.Of<ILogger>());
            var realServiceProvider = new ServiceCollection()
                .AddSingleton(realLoggerFactory.Object)
                .BuildServiceProvider();
            var realFactory = new McpConnectionFactory(realServiceProvider);
            services.AddSingleton(realFactory);

            _serviceProvider = services.BuildServiceProvider();
        }

        [Fact]
        public void Plugin_Should_Initialize_Successfully_With_Factory()
        {
            // Arrange & Act
            var plugin = new UseMCPPlugin(_serviceProvider);

            // Assert
            plugin.ShouldNotBeNull();
        }

        [Fact]
        public void GetConnectionStats_Should_Return_Stats_When_Factory_Registered()
        {
            // Arrange
            var plugin = new UseMCPPlugin(_serviceProvider);

            // Act
            var result = plugin.GetConnectionStats();

            // Assert - Just verify it returns some stats format (may not be 0,0 since it's the real factory)
             result.ShouldContain("Connections:");
            result.ShouldContain("ToolCache:");
        }

        [Fact]
        public async Task ListServersAsync_Should_Return_Empty_When_No_Servers()
        {
            // Arrange
            var plugin = new UseMCPPlugin(_serviceProvider);
            var kernel = CreateKernelWithContext(appId: 1);

            _mockMcpServerRepository.Setup(x => x.FindListAsync(It.IsAny<System.Linq.Expressions.Expression<Func<MCPServer, bool>>>()))
                .ReturnsAsync(new List<MCPServer>());

            // Act
            var result = await plugin.ListServersAsync(kernel);

            // Assert
            result.ShouldBe("[]");
        }

        [Fact]
        public async Task ListServersAsync_Should_Return_Server_List()
        {
            // Arrange
            var plugin = new UseMCPPlugin(_serviceProvider);
            var kernel = CreateKernelWithContext(appId: 1);

            var servers = new List<MCPServer>
            {
                new MCPServer { Name = "server1", Intro = "Test server 1" },
                new MCPServer { Name = "server2", Intro = "Test server 2" }
            };

            _mockMcpServerRepository.Setup(x => x.FindListAsync(It.IsAny<System.Linq.Expressions.Expression<Func<MCPServer, bool>>>()))
                .ReturnsAsync(servers);

            // Act
            var result = await plugin.ListServersAsync(kernel);

            // Assert
            result.ShouldContain("server1");
            result.ShouldContain("server2");
        }

        [Fact]
        public async Task ListToolsAsync_Should_Return_Error_For_Unknown_Server()
        {
            // Arrange
            var plugin = new UseMCPPlugin(_serviceProvider);
            var kernel = CreateKernelWithContext(appId: 1);

            _mockMcpServerRepository.Setup(x => x.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<MCPServer, bool>>>()))
                .ReturnsAsync((MCPServer?)null);

            // Act
            var result = await plugin.ListToolsAsync("unknown-server", kernel);

            // Assert
            result.ShouldContain("Unable to find");
        }

        [Fact]
        public async Task RefreshToolsAsync_Should_Return_Error_For_Unknown_Server()
        {
            // Arrange
            var plugin = new UseMCPPlugin(_serviceProvider);
            var kernel = CreateKernelWithContext(appId: 1);

            _mockMcpServerRepository.Setup(x => x.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<MCPServer, bool>>>()))
                .ReturnsAsync((MCPServer?)null);

            // Act
            var result = await plugin.RefreshToolsAsync("unknown-server", kernel);

            // Assert
            result.ShouldContain("Unable to find");
        }

        [Fact]
        public async Task ListResourcesAsync_Should_Return_Error_For_Unknown_Server()
        {
            // Arrange
            var plugin = new UseMCPPlugin(_serviceProvider);
            var kernel = CreateKernelWithContext(appId: 1);

            _mockMcpServerRepository.Setup(x => x.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<MCPServer, bool>>>()))
                .ReturnsAsync((MCPServer?)null);

            // Act
            var result = await plugin.ListResourcesAsync("unknown-server", kernel);

            // Assert
            result.ShouldContain("Unable to find");
        }

        [Fact]
        public async Task ListPromptsAsync_Should_Return_Error_For_Unknown_Server()
        {
            // Arrange
            var plugin = new UseMCPPlugin(_serviceProvider);
            var kernel = CreateKernelWithContext(appId: 1);

            _mockMcpServerRepository.Setup(x => x.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<MCPServer, bool>>>()))
                .ReturnsAsync((MCPServer?)null);

            // Act
            var result = await plugin.ListPromptsAsync("unknown-server", kernel);

            // Assert
            result.ShouldContain("Unable to find");
        }

        [Fact]
        public async Task CallToolAsync_Should_Return_Error_For_Unknown_Server()
        {
            // Arrange
            var plugin = new UseMCPPlugin(_serviceProvider);
            var kernel = CreateKernelWithContext(appId: 1);

            _mockMcpServerRepository.Setup(x => x.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<MCPServer, bool>>>()))
                .ReturnsAsync((MCPServer?)null);

            // Act
            var result = await plugin.CallToolAsync("unknown-server", "test-tool", new Dictionary<string, object>(), kernel);

            // Assert
            result.ShouldContain("Unable to find");
        }

        [Fact]
        public async Task DisconnectAsync_Should_Return_Error_For_Unknown_Server()
        {
            // Arrange
            var plugin = new UseMCPPlugin(_serviceProvider);
            var kernel = CreateKernelWithContext(appId: 1);

            _mockMcpServerRepository.Setup(x => x.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<MCPServer, bool>>>()))
                .ReturnsAsync((MCPServer?)null);

            // Act
            var result = await plugin.DisconnectAsync("unknown-server", kernel);

            // Assert
            result.ShouldContain("Unable to find");
        }

        [Fact]
        public void GetConnectionStats_Should_Throw_When_Factory_Not_Registered()
        {
            // Arrange
            var servicesWithoutFactory = new ServiceCollection();
            servicesWithoutFactory.AddSingleton(_mockLoggerFactory.Object);
            servicesWithoutFactory.AddSingleton(_mockMcpServerRepository.Object);
            servicesWithoutFactory.AddSingleton(_mockPromptTemplateService.Object);

            // BasePlugin requires IHttpContextAccessor
            var httpContext = new DefaultHttpContext();
            var httpContextAccessor = new Mock<IHttpContextAccessor>(MockBehavior.Loose);
            httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
            servicesWithoutFactory.AddSingleton(httpContextAccessor.Object);

            // Note: CacheableMcpClientFactory is NOT registered

            var serviceProviderWithoutFactory = servicesWithoutFactory.BuildServiceProvider();
            var plugin = new UseMCPPlugin(serviceProviderWithoutFactory);

            // Act & Assert
            Should.Throw<InvalidOperationException>(() => plugin.GetConnectionStats());
        }

        private Kernel CreateKernelWithContext(long appId = 1)
        {
            var builder = Kernel.CreateBuilder();
            builder.Services.AddLogging();

            var mockHttpContext = new DefaultHttpContext();
            mockHttpContext.Request.Method = "POST";
            mockHttpContext.Request.Scheme = "http";
            mockHttpContext.Request.Host = new HostString("localhost");
            mockHttpContext.Request.Path = "/test";

            var claimsPrincipal = new ClaimsPrincipal(new[]
            {
                new ClaimsIdentity(new[]
                {
                    new Claim("app_id", appId.ToString())
                })
            });
            mockHttpContext.User = claimsPrincipal;

            var mockHttpContextAccessor = new Mock<IHttpContextAccessor>(MockBehavior.Loose);
            mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(mockHttpContext);

            builder.Services.AddSingleton(mockHttpContextAccessor.Object);
            builder.Services.AddSingleton<AgentExecutionContext>();

            return builder.Build();
        }
    }
}
