var builder = DistributedApplication.CreateBuilder(args);

var service = builder.AddProject<Projects.AgentHub_Service>("agenthub-service");

builder.AddProject<Projects.AgentHub_Web>("agenthub-web")
    .WithReference(service)
    .WithExternalHttpEndpoints();

builder.Build().Run();
