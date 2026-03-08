var builder = DistributedApplication.CreateBuilder(args);

// Minimal Aspire starter.
// Real next step:
// - register AgentHub.Service as a project resource
// - add Redis/NATS when durable eventing is introduced
// - add blob emulator / Azurite if you want local shared storage testing

builder.Build().Run();
