using LLama;
using LLama.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Confirguration;
using PostgreSQL.Embedding.Common.Converters;
using PostgreSQL.Embedding.Common.Extensions;
using PostgreSQL.Embedding.Common.Middlewares;
using PostgreSQL.Embedding.Common.Settings;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Domain.Models.WebApi;
using PostgreSQL.Embedding.Hubs;
using PostgreSQL.Embedding.Infrastructure;
using PostgreSQL.Embedding.Infrastructure.DataAccess;
using PostgreSQL.Embedding.Infrastructure.FileStorage;
using PostgreSQL.Embedding.Infrastructure.Messaging;
using PostgreSQL.Embedding.Infrastructure.Sandbox;
using PostgreSQL.Embedding.Infrastructure.Text2DB;
using PostgreSQL.Embedding.Llm.Abstractions;
using PostgreSQL.Embedding.Llm.Core;
using PostgreSQL.Embedding.Llm.Planners;
using PostgreSQL.Embedding.Llm.Services;
using PostgreSQL.Embedding.Plugins;
using PostgreSQL.Embedding.Plugins.BuiltIn;
using PostgreSQL.Embedding.Plugins.Custom;
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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                builder.Configuration["JwtSetting:Secret"]
                ?? throw new InvalidOperationException("JwtSetting:Secret is not configured."))),
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
                    path.StartsWithSegments("/hubs/notificationHub"))
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
        .Build());
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
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
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
            Array.Empty<string>()
        }
    });
});

builder.Services.AddSignalR(options =>
{
    options.KeepAliveInterval = TimeSpan.FromSeconds(30);
});


builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient()
    .ConfigureHttpClientDefaults(httpClientBuilder =>
    {
        httpClientBuilder.ConfigurePrimaryHttpMessageHandler(() =>
            new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            });
    });

builder.Services.Configure<LlmConfig>(builder.Configuration.GetSection(nameof(LlmConfig)));
builder.Services.Configure<JwtSetting>(builder.Configuration.GetSection(nameof(JwtSetting)));
builder.Services.Configure<PythonConfig>(builder.Configuration.GetSection(nameof(PythonConfig)));
builder.Services.Configure<CodeInterpreterConfig>(builder.Configuration.GetSection(nameof(CodeInterpreterConfig)));

builder.Services
    .AddDataAccess(builder.Configuration)
    .AddFileStorage(builder.Configuration)
    .AddMessaging()
    .AddDockerSandbox(builder.Configuration)
    .AddText2DB();

builder.Services
    .AddLlmCore(builder.Configuration)
    .AddLLamaEmbedder(builder.Configuration)
    .AddLlmServices()
    .AddLLama()
    .AddHuggingFace()
    .AddOllama()
    .AddUserIdentityServices();


builder.Services.AddPlugins(builder.Configuration);

// 持久化所有插件元数据到数据库（启动时执行）
builder.Services.PersistAllPluginsAsync().Wait();

builder.Services.AddSingleton<EnumValuesConverter>();
builder.Services.AddScoped<ILlmPluginService, LlmPluginService>();

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
    ?? new[] { "http://localhost:2800" };

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policyBuilder =>
    {
        policyBuilder.WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

 var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

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

var profileFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
var insightaFolder = Path.Combine(profileFolder, ".insighta");
if (!Directory.Exists(insightaFolder)) Directory.CreateDirectory(insightaFolder);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(insightaFolder),
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
