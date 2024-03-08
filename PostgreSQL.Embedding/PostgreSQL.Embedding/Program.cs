using Microsoft.EntityFrameworkCore;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Postgres;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.Handlers;
using LLamaSharp.SemanticKernel.TextEmbedding;
using LLamaSharp.KernelMemory;
using LLama.Common;
using LLama;
using PostgreSQL.Embedding.Services.Training;
using PostgreSQL.Embedding.LLmServices.Extensions;
using PostgreSQL.Embedding.LlmServices.Abstration;
using PostgreSQL.Embedding.LlmServices;
using DocumentFormat.OpenXml.Office2016.Drawing.ChartDrawing;
using Microsoft.VisualBasic.FileIO;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<IEmbeddingService, SKEmbeddingService>();
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
builder.Services.AddLLama();
builder.Services.Configure<LlmConfig>(builder.Configuration.GetSection(nameof(LlmConfig)));
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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
