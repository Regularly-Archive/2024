using LLama;
using LLama.Common;
using LLamaSharp.KernelMemory;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Postgres;
using Microsoft.OpenApi.Models;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Converters;
using PostgreSQL.Embedding.Common.Middlewares;
using PostgreSQL.Embedding.Common.Settings;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.Handlers;
using PostgreSQL.Embedding.LlmServices;
using PostgreSQL.Embedding.LlmServices.Abstration;
using PostgreSQL.Embedding.LLmServices.Extensions;
using PostgreSQL.Embedding.Services;
using SqlSugar;
using System.Text;
using System.Text.Encodings.Web;
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
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddScoped<IUserInfoService, UserInfoService>();
builder.Services.AddScoped<IKernelService, KernalService>();
builder.Services.AddScoped<IMemoryService, PostgreSQL.Embedding.LlmServices.MemoryService>();

// Todo: 需要实现按指定模型加载
builder.Services.AddSingleton<LLamaEmbedder>(sp =>
{
    var modelPath = Path.Combine(builder.Environment.ContentRootPath, builder.Configuration["LLamaConfig:ModelPath"]!);
    var @params = new ModelParams(modelPath) { EmbeddingMode = true, ContextSize = builder.Configuration.GetValue<uint>("LLamaConfig:ContextSize") };
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
builder.Services.AddScoped<IChatHistoryService, ChatHistoryService>();
builder.Services.AddLLama().AddHuggingFace();
builder.Services.Configure<LlmConfig>(builder.Configuration.GetSection(nameof(LlmConfig)));
builder.Services.Configure<JwtSetting>(builder.Configuration.GetSection(nameof(JwtSetting)));
builder.Services.AddSingleton<ILlmServiceFactory, LlmServiceFactory>();
builder.Services.AddScoped<IKnowledgeBaseService, KnowledgeBaseService>();
builder.Services.AddScoped<PromptTemplateService>();

builder.Services.AddSingleton<KnowledgeImportingQueueService>();
builder.Services.AddHostedService<KnowledgeImportingQueueService>();
builder.Services.AddSingleton<EnumValuesConverter>();
builder.Services.AddScoped<IFullTextSearchService, FullTextSearchService>();
builder.Services.AddScoped<IFileStorageService, PhysicalFileStorageService>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(builder =>
    {
        builder.WithOrigins("http://localhost:2800", "http://192.168.1.196:2800")
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
    });
});
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers().RequireAuthorization();

app.Run();
