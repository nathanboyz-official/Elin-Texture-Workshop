namespace ElinTextureManager.Core.Model;

/// <summary>
/// Limits the game applies to package.xml, taken from its own code rather than from
/// documentation.
///
/// BaseModPackage declares them and then does exactly this when reading the file:
///
///     public const int defaultLoadPriority = 100;
///     public const int minLoadPriority = -999;
///     public const int maxLoadPriority = 999;
///     ...
///     case "loadPriority":
///         if (int.TryParse(ModXml.Text(item), out var result))
///             num = Mathf.Clamp(result, -999, 999);
///         break;
///
/// Two silent failures fall out of those five lines. A number outside the range is
/// clamped, so a mod asking to load last with 114514 lands on 999 alongside every other
/// mod that asked for too much - and load order between them is then decided by
/// something other than what their authors wrote. And a value that is not a number at
/// all fails the TryParse, leaving the priority untouched at the default of 100, with
/// nothing said anywhere.
/// </summary>
public static class PackageLimits
{
    public const int DefaultLoadPriority = 100;
    public const int MinLoadPriority = -999;
    public const int MaxLoadPriority = 999;

    /// <summary>The priority the game will use for a declared value.</summary>
    public static int Effective(string? declared)
    {
        if (!int.TryParse(declared?.Trim(), out var value)) return DefaultLoadPriority;

        return Math.Clamp(value, MinLoadPriority, MaxLoadPriority);
    }
}
