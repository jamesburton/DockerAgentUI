using System.Text.RegularExpressions;
using AgentHub.Contracts;
using AgentHub.Orchestration.Config;

namespace AgentHub.Orchestration.Security;

/// <summary>
/// Result of a trust tier evaluation for an action.
/// </summary>
public sealed record TrustTierDecision(TrustTier Tier, bool RequiresApproval, bool Denied);

public sealed class BasicSanitizationService : ISanitizationService
{
    /// <summary>
    /// Original dangerous patterns from Phase 1 (backward compatible).
    /// </summary>
    private static readonly string[] DangerousPatterns =
    [
        "rm -rf /",
        "del /f /s",
        "format c:",
        "shutdown /r",
        "curl .*AWS_SECRET_ACCESS_KEY",
        "BEGIN PRIVATE KEY",
        @":\(\)\{:\|:&\};:"
    ];

    /// <summary>
    /// Extended dangerous patterns for Phase 2 security layer.
    /// </summary>
    private static readonly string[] ShellInjectionPatterns =
    [
        @"(?<!\w);(?!\s*$)",       // Semicolon command chaining (not at end of line)
        @"&&",                      // Double-ampersand chaining
        @"\|\|",                    // OR chaining
        @"`[^`]+`",                 // Backtick subshell
        @"\$\(",                    // $() subshell execution
        @"\$\{"                     // ${} variable expansion
    ];

    private static readonly string[] PathTraversalPatterns =
    [
        @"\.\./",                   // Unix path traversal
        @"\.\.\\"                   // Windows path traversal
    ];

    private static readonly string[] EnvVarExfiltrationPatterns =
    [
        @"\$[A-Z_]{3,}.*(?:curl|wget|nc|ncat)",  // Env var before external tool
        @"(?:curl|wget|nc|ncat).*\$[A-Z_]{3,}"   // External tool before env var
    ];

    private static readonly string[] Base64PayloadPatterns =
    [
        @"base64.*\|\s*(?:bash|sh|eval)",        // base64 piped to shell
        @"\|\s*base64\s+-d\s*\|\s*(?:bash|sh|eval)"  // piped decode to shell
    ];

    /// <summary>
    /// Evaluates input for dangerous patterns (backward compatible).
    /// </summary>
    public SanitizationDecision Evaluate(SendInputRequest request, SessionSummary session, SkillManifest? skill)
    {
        return Evaluate(request, session, skill, policy: null);
    }

    /// <summary>
    /// Evaluates input for dangerous patterns with optional policy-based configurable rules.
    /// </summary>
    public SanitizationDecision Evaluate(SendInputRequest request, SessionSummary session, SkillManifest? skill, ScopedPolicyConfig? policy)
    {
        var input = request.Input?.Trim() ?? string.Empty;
        var normalizedInput = Regex.Replace(input, @"\s+", " ");

        var reasons = new List<string>();
        var risk = skill?.Risk ?? RiskLevel.Medium;

        // Check original dangerous patterns (backward compatible)
        foreach (var pattern in DangerousPatterns)
        {
            if (Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase))
                reasons.Add($"Matched blocked pattern: {pattern}");
        }

        // Check extended patterns (Phase 2)
        CheckPatterns(input, ShellInjectionPatterns, "shell injection", reasons);
        CheckPatterns(input, PathTraversalPatterns, "path traversal", reasons);
        CheckPatterns(input, EnvVarExfiltrationPatterns, "env var exfiltration", reasons);
        CheckPatterns(input, Base64PayloadPatterns, "base64 encoded payload", reasons);

        // Check configurable patterns from policy
        if (policy?.DisallowedTools is { Length: > 0 } disallowed)
        {
            foreach (var tool in disallowed)
            {
                if (Regex.IsMatch(input, Regex.Escape(tool), RegexOptions.IgnoreCase))
                    reasons.Add($"Matched policy-blocked pattern: {tool}");
            }
        }

        // SSH without skill check
        if (string.Equals(session.Backend, "ssh", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(request.SkillId))
        {
            risk = RiskLevel.High;
            reasons.Add("Raw SSH input is blocked unless wrapped in a skill.");
        }

        if (reasons.Count > 0)
            return new SanitizationDecision(false, normalizedInput, risk, reasons.ToArray());

        return new SanitizationDecision(true, normalizedInput, risk, ["Input accepted"]);
    }

    /// <summary>
    /// Evaluates an action against trust tiers from policy configuration.
    /// Returns the trust tier decision indicating whether approval is needed.
    /// </summary>
    public TrustTierDecision EvaluateWithTrustTier(string action, ScopedPolicyConfig? policy)
    {
        if (policy is null)
        {
            // No policy: default to Prompt (safe default)
            return new TrustTierDecision(TrustTier.Prompt, RequiresApproval: true, Denied: false);
        }

        if (policy.TrustTiers.TryGetValue(action, out var tier))
        {
            return tier switch
            {
                TrustTier.AlwaysAllow => new TrustTierDecision(TrustTier.AlwaysAllow, RequiresApproval: false, Denied: false),
                TrustTier.AlwaysDeny => new TrustTierDecision(TrustTier.AlwaysDeny, RequiresApproval: false, Denied: true),
                TrustTier.Prompt => new TrustTierDecision(TrustTier.Prompt, RequiresApproval: true, Denied: false),
                _ => new TrustTierDecision(TrustTier.Prompt, RequiresApproval: true, Denied: false)
            };
        }

        // Action not found in tiers: default to Prompt (safe default)
        return new TrustTierDecision(TrustTier.Prompt, RequiresApproval: true, Denied: false);
    }

    private static void CheckPatterns(string input, string[] patterns, string category, List<string> reasons)
    {
        foreach (var pattern in patterns)
        {
            if (Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase))
            {
                reasons.Add($"Detected {category}: matched pattern");
                return; // One match per category is enough
            }
        }
    }
}
