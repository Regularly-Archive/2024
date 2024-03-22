using LLama;
using LLama.Common;
using LLamaSharp.KernelMemory;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Azure;
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
using PostgreSQL.Embedding.Services.Training;
using SqlSugar;
using System.Text;

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
builder.Services.AddScoped<IEmbeddingService, SKEmbeddingService>();
builder.Services.AddScoped<IKernelService, KernalService>();
builder.Services.AddScoped<IMemoryService, PostgreSQL.Embedding.LlmServices.MemoryService>();
builder.Services.AddDbContext<VectorsDbContext>(opt =>
{
    opt.UseNpgsql(builder.Configuration["ConnectionStrings:Default"], x => x.UseVector());
});

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
builder.Services.AddScoped<PgVectorService>();
builder.Services.AddSingleton<MemoryServerless>(serviceProvider =>
{
    var httpClient = new HttpClient(new OpenAIProxyHandler(builder.Configuration));

    var postgresConfig = new PostgresConfig()
    {
        ConnectionString = builder.Configuration["ConnectionStrings:Default"]!,
        TableNamePrefix = "sk_"
    };


    var memoryBuilder = new KernelMemoryBuilder();
    memoryBuilder.WithPostgresMemoryDb(postgresConfig);

    var llmConfig = new LlmConfig();
    builder.Configuration.Bind(nameof(LlmConfig), llmConfig);

    if (llmConfig.Provider == LlmServiceProvider.OpenAI)
    {
        var openAIConfig = new OpenAIConfig();
        builder.Configuration.BindSection(nameof(OpenAIConfig), openAIConfig);

        memoryBuilder
            .WithOpenAITextGeneration(openAIConfig)
            .WithOpenAITextEmbeddingGeneration(openAIConfig);
    }
    else
    {
        var modelPath = Path.Combine(
            builder.Environment.ContentRootPath,
            builder.Configuration["LLamaConfig:ModelPath"]!
        );

        var llamaConfig = new LLamaSharpConfig(modelPath) { ContextSize = builder.Configuration.GetValue<uint>("LlamaConfig:ContextSize") };
        var embedder = serviceProvider.GetRequiredService<LLamaEmbedder>();
        memoryBuilder
            .WithCustomTextGenerator(new LlamaSharpTextGenerator(llamaConfig))
            .WithCustomEmbeddingGenerator(new LLamaSharpTextEmbeddingGenerator(embedder));
    }


    return memoryBuilder.Build<MemoryServerless>();
});
builder.Services.AddSingleton<KnowledgeImportingQueueService>();
builder.Services.AddHostedService<KnowledgeImportingQueueService>();
builder.Services.AddSingleton<EnumValuesConverter>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(builder =>
    {
        builder.WithOrigins("http://localhost:2800")
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
