namespace AgentHub.Orchestration.Coordinator;

public sealed class CoordinationOptions
{
    public int MaxDepth { get; set; } = 3;
    public int MaxChildrenPerParent { get; set; } = 10;
}
