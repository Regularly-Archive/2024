using LLama;
using LLama.Common;
using LLamaSharp.KernelMemory;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Postgres;
using Microsoft.OpenApi.Models;
using Minio;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Confirguration;
using PostgreSQL.Embedding.Common.Converters;
using PostgreSQL.Embedding.Common.Middlewares;
using PostgreSQL.Embedding.Common.Settings;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.Handlers;
using PostgreSQL.Embedding.Hubs;
using PostgreSQL.Embedding.LlmServices;
using PostgreSQL.Embedding.LlmServices.Abstration;
using PostgreSQL.Embedding.LLmServices.Extensions;
using PostgreSQL.Embedding.Services;
using SqlSugar;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using PostgreSQL.Embedding.Utils;

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

    options.Filters.Add<GlobalExceptionFilter>();
})
.AddJsonOptions(cfg =>
{
    cfg.JsonSerializerOptions.Converters.Add(new BigIntJsonConverter());
    cfg.JsonSerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
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
builder.Services.AddSignalR();
builder.Services.AddSingleton<INotificationService, NotificationService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddScoped<IUserInfoService, UserInfoService>();
builder.Services.AddScoped<IKernelService, KernalService>();
builder.Services.AddScoped<IMemoryService, PostgreSQL.Embedding.LlmServices.MemoryService>();
builder.Services.AddScoped<IImportingTaskHandler, FileImportingTaskHandler>();
builder.Services.AddScoped<IImportingTaskHandler, TextImportingTaskHandler>();
builder.Services.AddScoped<IImportingTaskHandler, UrlImportingTaskHandler>();

// Todo: 需要实现按指定模型加载
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
builder.Services.AddScoped<IFullTextSearchService, FullTextSearchService>();
builder.Services.AddScoped<IFileStorageService, MinioFileStorageService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddSingleton<IRerankService, BgeRerankService>();
builder.Services.AddScoped<ILlmPluginService, LlmPluginService>();
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
builder.Services.RegisterLlmPlugins();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseStaticFiles(new StaticFileOptions()
{
    FileProvider = new PhysicalFileProvider(Path.Combine(builder.Environment.ContentRootPath)),
    RequestPath = "/statics"
});

app.UseAuthentication();

app.UseAuthorization();

app.MapHub<NotificationHub>("/hubs/notificationHub");
app.MapControllers().RequireAuthorization();

app.Run();
