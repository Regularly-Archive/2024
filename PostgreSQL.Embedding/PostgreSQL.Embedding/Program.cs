using LLama;
using LLama.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Minio;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Confirguration;
using PostgreSQL.Embedding.Common.Converters;
using PostgreSQL.Embedding.Common.Extensions;
using PostgreSQL.Embedding.Common.Middlewares;
using PostgreSQL.Embedding.Common.Settings;
using PostgreSQL.Embedding.Domain.Models.WebApi;
using PostgreSQL.Embedding.Handlers;
using PostgreSQL.Embedding.Hubs;
using PostgreSQL.Embedding.Infrastructure;
using PostgreSQL.Embedding.Infrastructure.DataAccess;
using PostgreSQL.Embedding.Infrastructure.FileStorage;
using PostgreSQL.Embedding.Infrastructure.Messaging;
using PostgreSQL.Embedding.Llm.Abstractions;
using PostgreSQL.Embedding.Llm.Core;
using PostgreSQL.Embedding.Llm.Services;
using PostgreSQL.Embedding.Llm.Services.Rerank;
using PostgreSQL.Embedding.Llm.Services.Retrieval;
using PostgreSQL.Embedding.Utils;
using SqlSugar;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.SaveToken = true;
                options.RequireHttpsMetadata = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["JwtSetting:Secret"])),
                    ValidateIssuer = false,
                    ValidateAudience = false
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) &&
                            (path.StartsWithSegments("/hubs/notificationHub")))
                        {
                            context.Token = accessToken;
                        }
                        return Task.CompletedTask;
                    }
                };
            });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Bearer", new AuthorizationPolicyBuilder()
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build()
   );
});

builder.Services.AddControllers(options =>
{
    options.ModelBinderProviders.Insert(0, new QueryParameterBinderProvider());
    options.Filters.Add<GlobalExceptionFilter>();
})
.AddJsonOptions(cfg =>
{
    cfg.JsonSerializerOptions.Converters.Add(new BigIntJsonConverter());
    cfg.JsonSerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
    cfg.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
          new OpenApiSecurityScheme
          {
            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
          },
          new string[] {}
        }
    });
});
builder.Services.AddSignalR(options => {
    options.KeepAliveInterval = TimeSpan.FromSeconds(30);
});
builder.Services.AddSingleton<INotificationService, NotificationService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient()
    .ConfigureHttpClientDefaults(httpClientBuilder =>
    {
        httpClientBuilder.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler() { 
            ServerCertificateCustomValidationCallback = (a, b, c, d) => true 
        });
    });
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddScoped<IUserInfoService, UserInfoService>();
builder.Services.AddScoped<IKernelService, KernalService>();
builder.Services.AddScoped<IMemoryService, MemoryService>();
builder.Services.AddScoped<IImportingTaskHandler, FileImportingTaskHandler>();
builder.Services.AddScoped<IImportingTaskHandler, TextImportingTaskHandler>();
builder.Services.AddScoped<IImportingTaskHandler, UrlImportingTaskHandler>();

// Todo: 
builder.Services.AddSingleton<LLamaEmbedder>(sp =>
{
    var modelPath = Path.Combine(builder.Environment.ContentRootPath, builder.Configuration["LLamaConfig:ModelPath"]!);
    var @params = new ModelParams(modelPath) { ContextSize = builder.Configuration.GetValue<uint>("LLamaConfig:ContextSize") };
    using var weights = LLamaWeights.LoadFromFile(@params);
    var embedder = new LLamaEmbedder(weights, @params);
    return embedder;
});
builder.Services.AddScoped<ISqlSugarClient, SqlSugarClient>(sp =>
{
    var sqlSugarClient = new SqlSugarClient(new ConnectionConfig()
    {
        DbType = DbType.PostgreSQL,
        InitKeyType = InitKeyType.Attribute,
        IsAutoCloseConnection = true,
        ConnectionString = builder.Configuration["ConnectionStrings:Default"]
    });

    return sqlSugarClient;
});
builder.Services.AddScoped(typeof(SimpleClient<>));
builder.Services.AddScoped(typeof(CrudBaseService<>));
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
builder.Services.AddScoped<IChatHistoriesService, ChatHistoriesService>();
builder.Services.AddLLama().AddHuggingFace().AddOllama();
builder.Services.Configure<LlmConfig>(builder.Configuration.GetSection(nameof(LlmConfig)));
builder.Services.Configure<JwtSetting>(builder.Configuration.GetSection(nameof(JwtSetting)));
builder.Services.Configure<PythonConfig>(builder.Configuration.GetSection(nameof(PythonConfig)));
builder.Services.Configure<CodeInterpreterConfig>(builder.Configuration.GetSection(nameof(CodeInterpreterConfig)));
builder.Services.AddSingleton<ILlmServiceFactory, LlmServiceFactory>();
builder.Services.AddScoped<IKnowledgeBaseService, KnowledgeBaseService>();
builder.Services.AddScoped<IKnowledgeBaseTaskQueueService, KnowledgeBaseTaskQueueService>();
builder.Services.AddScoped<PromptTemplateService>();
builder.Services.AddMinio(minioClient =>
{
    var minioConfig = builder.Configuration.GetSection("MinioConfig");
    minioClient
        .WithEndpoint(new Uri(minioConfig["Url"]))
        .WithCredentials(minioConfig["AccessKey"], minioConfig["SecretKey"])
        .WithSSL(false);
});
builder.Services.AddSingleton<KnowledgeBaseBackgroundService>();
builder.Services.AddHostedService<KnowledgeBaseBackgroundService>();
builder.Services.AddSingleton<EnumValuesConverter>();
builder.Services.AddScoped<IFileStorageService, MinioFileStorageService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddKeyedSingleton<IRerankService, BgeRerankService>(nameof(RerankerType.BGE));
builder.Services.AddKeyedSingleton<IRerankService, BM25RerankerService>(nameof(RerankerType.BM25));
builder.Services.AddKeyedSingleton<IRerankService, FlashRerankService>(nameof(RerankerType.FlashRank));
builder.Services.AddScoped<ILlmPluginService, LlmPluginService>();
builder.Services.AddScoped<IKnowledgeRetrievalService, VectorsRetrievalService>();
builder.Services.AddScoped<IKnowledgeRetrievalService, FullTextRetrievalService>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(builder =>
    {
        builder.WithOrigins("http://localhost:2800", "http://192.168.1.196:2800", "http://192.168.1.116:2800")
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
    });
});
builder.Services.AddScoped<CacheableMcpClientFactory>();
builder.Services.AddPythonRuntime(builder.Configuration);
builder.Services.RegisterLlmPlugins();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

// 补充自定义 MIME 类型映射（内置已包含常见类型）
var contentTypeProvider = new FileExtensionContentTypeProvider
{
    Mappings =
    {
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".yml"] = "application/x-yaml",
        [".yaml"] = "application/x-yaml",
    }
};

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(builder.Environment.ContentRootPath)),
    RequestPath = "/api/statics",
    ServeUnknownFileTypes = true,
    ContentTypeProvider = contentTypeProvider
});

app.UseMiddleware<DisableCompressionMiddleware>();

app.UseAuthentication();

app.UseAuthorization();

app.MapHub<NotificationHub>("/hubs/notificationHub");
app.MapControllers().RequireAuthorization();

app.Run();
