namespace AgentHub.Tests;

/// <summary>
/// Minimal IServiceProvider for tests that don't exercise DI-resolved services.
/// </summary>
public sealed class NullServiceProvider : IServiceProvider
{
    public object? GetService(Type serviceType) => null;
}
