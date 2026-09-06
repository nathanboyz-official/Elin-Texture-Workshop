using System.Windows.Media;

namespace ElinTextureManager.App.ViewModels;

/// <summary>
/// Sidebar icons as vector geometry.
///
/// Drawn rather than taken from an icon font, for two reasons: nothing has to be
/// installed for them to render, and stroke-only line art at 16px stays quiet enough
/// to sit beside a label without competing with it. Each path is authored on a 16x16
/// box and stroked, never filled - a filled glyph at this size reads as a button.
/// </summary>
public static class NavIcons
{
    private static Geometry P(string data)
    {
        var g = Geometry.Parse(data);
        g.Freeze();
        return g;
    }

    /// <summary>Stacked sheets: the whole library.</summary>
    public static Geometry AllTextures { get; } =
        P("M8,2 L14,5 L8,8 L2,5 Z M2,8 L8,11 L14,8 M2,11 L8,14 L14,11");

    /// <summary>Head and shoulders.</summary>
    public static Geometry Characters { get; } =
        P("M8,2.6 A2.3,2.3 0 1,1 7.99,2.6 Z M3.4,13.6 C3.4,10.4 5.6,8.9 8,8.9 "
          + "C10.4,8.9 12.6,10.4 12.6,13.6");

    /// <summary>A crate.</summary>
    public static Geometry Items { get; } =
        P("M2.5,5.2 L8,2.4 L13.5,5.2 L13.5,11.4 L8,14.2 L2.5,11.4 Z "
          + "M2.5,5.2 L8,8 L13.5,5.2 M8,8 L8,14.2");

    /// <summary>A framed portrait.</summary>
    public static Geometry Portraits { get; } =
        P("M3,2.6 L13,2.6 L13,13.4 L3,13.4 Z "
          + "M8,5.6 A1.7,1.7 0 1,1 7.99,5.6 Z "
          + "M5.3,12.2 C5.3,10.1 6.6,9.3 8,9.3 C9.4,9.3 10.7,10.1 10.7,12.2");

    /// <summary>A placed object on a tile.</summary>
    public static Geometry Objects { get; } =
        P("M8,2.4 L13.6,5.6 L8,8.8 L2.4,5.6 Z M2.4,5.6 L2.4,10.4 L8,13.6 "
          + "L13.6,10.4 L13.6,5.6 M8,8.8 L8,13.6");

    /// <summary>Warning: more than one mod wants the same slot.</summary>
    public static Geometry Conflicts { get; } =
        P("M8,2.4 L14.4,13.6 L1.6,13.6 Z M8,6.2 L8,10 M8,11.5 L8,12.3");

    /// <summary>A seal: the mark of a deliberate choice.</summary>
    public static Geometry Overrides { get; } =
        P("M8,1.8 L9.9,6.1 L14.2,8 L9.9,9.9 L8,14.2 L6.1,9.9 L1.8,8 L6.1,6.1 Z");

    /// <summary>Installed packages.</summary>
    public static Geometry Mods { get; } =
        P("M2.6,3 L6.8,3 L6.8,7.2 L2.6,7.2 Z M9.2,3 L13.4,3 L13.4,7.2 L9.2,7.2 Z "
          + "M2.6,8.8 L6.8,8.8 L6.8,13 L2.6,13 Z M9.2,8.8 L13.4,8.8 L13.4,13 L9.2,13 Z");

    /// <summary>Reordering.</summary>
    public static Geometry LoadOrder { get; } =
        P("M5,13 L5,3.2 M2.9,5.3 L5,3.2 L7.1,5.3 M11,3 L11,12.8 M8.9,10.7 L11,12.8 L13.1,10.7");

    /// <summary>Layered paper-doll parts.</summary>
    public static Geometry Pcc { get; } =
        P("M8,1.8 A1.9,1.9 0 1,1 7.99,1.8 Z M4.6,6.4 L11.4,6.4 L11.4,10.2 L4.6,10.2 Z "
          + "M3.2,12.2 L12.8,12.2 M5.4,14.2 L10.6,14.2");

    /// <summary>An announcement sheet.</summary>
    public static Geometry News { get; } =
        P("M3.4,2.6 L12.6,2.6 L12.6,13.4 L3.4,13.4 Z M5.8,5.6 L10.2,5.6 "
          + "M5.8,8 L10.2,8 M5.8,10.4 L8.6,10.4");

    /// <summary>A figure with pieces added at its sides.</summary>
    public static Geometry DressUp { get; } =
        P("M8,2.2 A1.7,1.7 0 1,1 7.99,2.2 Z M5.2,6.4 L10.8,6.4 L10.8,10 L5.2,10 Z "
          + "M6.2,10 L6.2,14 M9.8,10 L9.8,14 M3.6,7.2 L5.2,7.2 M10.8,7.2 L12.4,7.2");

    /// <summary>A list split in two: the halving this page does.</summary>
    public static Geometry Bisect { get; } =
        P("M2.6,4 L13.4,4 M2.6,7 L13.4,7 M2.6,12 L13.4,12 M8,9.2 L8,14.8 M5.6,9.6 L10.4,9.6");

    /// <summary>Stacked cards: one saved arrangement among several.</summary>
    public static Geometry Setups { get; } =
        P("M2.4,5.4 L8,2.8 L13.6,5.4 L8,8 Z M2.4,8.4 L8,11 L13.6,8.4 "
          + "M2.4,11.2 L8,13.8 L13.6,11.2");

    /// <summary>A magnifier over a small frame: looking something up by its picture.</summary>
    public static Geometry Identify { get; } =
        P("M2.6,2.6 L8.4,2.6 L8.4,8.4 L2.6,8.4 Z M9.9,9.9 A2.6,2.6 0 1,1 9.89,9.9 Z "
          + "M11.9,11.9 L14.2,14.2");

    /// <summary>A pulse line: the shape a diagnostic reading makes.</summary>
    public static Geometry Health { get; } =
        P("M1.5,8 L4.5,8 L6,4.5 L8.5,11.5 L10,8 L14.5,8");

    /// <summary>Settings.</summary>
    public static Geometry Settings { get; } =
        P("M8,5.7 A2.3,2.3 0 1,1 7.99,5.7 Z M8,1.5 L8,3.3 M8,12.7 L8,14.5 "
          + "M1.5,8 L3.3,8 M12.7,8 L14.5,8 M3.4,3.4 L4.7,4.7 M11.3,11.3 L12.6,12.6 "
          + "M12.6,3.4 L11.3,4.7 M4.7,11.3 L3.4,12.6");

    /// <summary>A grid, for the source sheets.</summary>
    public static Geometry Sheets { get; } =
        Geometry.Parse(
            "M2.5,3.5 L13.5,3.5 L13.5,12.5 L2.5,12.5 Z M2.5,6.5 L13.5,6.5 M2.5,9.5 L13.5,9.5 "
            + "M6.2,3.5 L6.2,12.5 M9.9,3.5 L9.9,12.5");

    /// <summary>An open book.</summary>
    public static Geometry Guide { get; } =
        Geometry.Parse(
            "M8,4.2 C6.6,3.2 4.6,3 2.5,3.2 L2.5,12.4 C4.6,12.2 6.6,12.4 8,13.4 "
            + "C9.4,12.4 11.4,12.2 13.5,12.4 L13.5,3.2 C11.4,3 9.4,3.2 8,4.2 Z M8,4.2 L8,13.4");

    /// <summary>Looks up an icon by nav key. Unknown keys get no icon rather than a wrong one.</summary>
    public static Geometry? ForKey(string key) => key switch
    {
        "Guide" => Guide,
        "Sheets" => Sheets,
        "AllTextures" => AllTextures,
        "Characters" => Characters,
        "Items" => Items,
        "Portraits" => Portraits,
        "Objects" => Objects,
        "Pcc" => Pcc,
        "Conflicts" => Conflicts,
        "Overrides" => Overrides,
        "Mods" => Mods,
        "LoadOrder" => LoadOrder,
        "Health" => Health,
        "Identify" => Identify,
        "Setups" => Setups,
        "Bisect" => Bisect,
        "DressUp" => DressUp,
        "News" => News,
        "Settings" => Settings,
        _ => null,
    };
}
