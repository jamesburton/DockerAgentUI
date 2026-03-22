var builder = DistributedApplication.CreateBuilder(args);

var service = builder.AddProject<Projects.AgentHub_Service>("agenthub-service")
    .WithHttpEndpoint(port: 5131, name: "http");

builder.AddProject<Projects.AgentHub_Web>("agenthub-web")
    .WithReference(service)
    .WithExternalHttpEndpoints();

builder.Build().Run();
