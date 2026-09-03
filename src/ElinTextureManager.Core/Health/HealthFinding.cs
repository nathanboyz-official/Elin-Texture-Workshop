namespace ElinTextureManager.Core.Health;

public enum HealthSeverity
{
    /// <summary>Will break at runtime. The game has already told you, or is about to.</summary>
    Broken = 0,
    /// <summary>Two mods disagree. One of them loses, and which one is not always obvious.</summary>
    Conflict = 1,
    /// <summary>Worth knowing, not necessarily wrong.</summary>
    Notice = 2,
}

/// <summary>
/// One thing wrong with the installed set of mods.
///
/// Findings name the mod actually responsible, which is the whole point: Elin's own
/// error dialog lists the Harmony patches wrapping a call, so the mod at the top of
/// the trace is usually innocent and the one people uninstall.
/// </summary>
public sealed class HealthFinding
{
    public required HealthSeverity Severity { get; init; }

    /// <summary>Which check produced this, for grouping.</summary>
    public required string Check { get; init; }

    /// <summary>One line, naming the culprit.</summary>
    public required string Title { get; init; }

    /// <summary>What is actually wrong, in plain words.</summary>
    public required string Detail { get; init; }

    /// <summary>What to do about it, when there is a clear answer.</summary>
    public string? Suggestion { get; init; }

    /// <summary>Workshop IDs involved, so the UI can offer to disable them.</summary>
    public List<string> ModKeys { get; } = new();

    /// <summary>Mod names involved, for display.</summary>
    public List<string> ModNames { get; } = new();

    /// <summary>Supporting lines - signatures, paths, versions.</summary>
    public List<string> Evidence { get; } = new();

    public string SeverityLabel => Severity switch
    {
        HealthSeverity.Broken => "BROKEN",
        HealthSeverity.Conflict => "CONFLICT",
        _ => "NOTICE",
    };
}

/// <summary>Everything one health scan produced.</summary>
public sealed class HealthReport
{
    public List<HealthFinding> Findings { get; } = new();

    /// <summary>Mods that ship code, which is the population most checks apply to.</summary>
    public int CodeModCount { get; set; }

    public int ScannedAssemblies { get; set; }
    public int GameMethodCount { get; set; }
    public DateTimeOffset RanAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Set when the game's own assembly could not be read, which disables the API check.</summary>
    public string? GameAssemblyError { get; set; }

    public int BrokenCount => Findings.Count(f => f.Severity == HealthSeverity.Broken);
    public int ConflictCount => Findings.Count(f => f.Severity == HealthSeverity.Conflict);
    public int NoticeCount => Findings.Count(f => f.Severity == HealthSeverity.Notice);

    public bool IsClean => Findings.Count == 0;
}
