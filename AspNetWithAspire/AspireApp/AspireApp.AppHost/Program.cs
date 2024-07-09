var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.AspireApp_ApiService>("ApiService");

builder.AddProject<Projects.AspireApp_Web>("WebApp")
    .WithExternalHttpEndpoints()
    .WithReference(apiService);

builder.Build().Run();
