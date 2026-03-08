using System.Text.RegularExpressions;
using AgentHub.Contracts;

namespace AgentHub.Orchestration.Security;

public sealed class BasicSanitizationService : ISanitizationService
{
    private static readonly string[] DangerousPatterns =
    [
        "rm -rf /",
        "del /f /s",
        "format c:",
        "shutdown /r",
        "curl .*AWS_SECRET_ACCESS_KEY",
        "BEGIN PRIVATE KEY",
        ":\\(\\)\\{:\\|:&\\};:"
    ];

    public SanitizationDecision Evaluate(SendInputRequest request, SessionSummary session, SkillManifest? skill)
    {
        var input = request.Input?.Trim() ?? string.Empty;
        input = Regex.Replace(input, "\\s+", " ");

        var reasons = new List<string>();
        var risk = skill?.Risk ?? RiskLevel.Medium;

        foreach (var pattern in DangerousPatterns)
        {
            if (Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase))
                reasons.Add($"Matched blocked pattern: {pattern}");
        }

        if (string.Equals(session.Backend, "ssh", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(request.SkillId))
        {
            risk = RiskLevel.High;
            reasons.Add("Raw SSH input is blocked unless wrapped in a skill.");
        }

        if (reasons.Count > 0)
            return new SanitizationDecision(false, input, risk, reasons.ToArray());

        return new SanitizationDecision(true, input, risk, ["Input accepted"]);
    }
}
