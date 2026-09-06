using System.Collections.ObjectModel;
using ElinTextureManager.App.Mvvm;
using ElinTextureManager.Core.Guide;
using ElinTextureManager.Core.Logging;

namespace ElinTextureManager.App.ViewModels;

/// <summary>One line of a topic, with the flags the view styles it by.</summary>
public sealed class GuideLineViewModel
{
    public GuideLineViewModel(GuideLine line)
    {
        Text = line.Text;
        Kind = line.Kind;
    }

    public string Text { get; }
    public GuideLineKind Kind { get; }

    public bool IsCode => Kind == GuideLineKind.Code;
    public bool IsTrap => Kind == GuideLineKind.Trap;
    public bool IsBullet => Kind == GuideLineKind.Bullet;
    public bool IsBody => Kind == GuideLineKind.Body;
}

public sealed class GuideTopicViewModel
{
    public GuideTopicViewModel(GuideTopic topic)
    {
        Title = topic.Title;
        Summary = topic.Summary;

        foreach (var line in topic.Lines) Lines.Add(new GuideLineViewModel(line));
        foreach (var link in topic.Links) Links.Add(link);
    }

    public string Title { get; }
    public string Summary { get; }
    public ObservableCollection<GuideLineViewModel> Lines { get; } = new();
    public ObservableCollection<GuideLink> Links { get; } = new();

    public bool HasLinks => Links.Count > 0;
}

public sealed class GuideSectionViewModel
{
    public GuideSectionViewModel(GuideSection section)
    {
        Title = section.Title;
        foreach (var topic in section.Topics) Topics.Add(new GuideTopicViewModel(topic));
    }

    public string Title { get; }
    public ObservableCollection<GuideTopicViewModel> Topics { get; } = new();
}

/// <summary>
/// The modding reference page.
///
/// Kept short on purpose: it covers what fails silently, and points at the community's
/// own documentation for everything else rather than trying to replace it.
/// </summary>
public sealed class GuideViewModel : ObservableObject
{
    public GuideViewModel()
    {
        foreach (var section in ModdingGuide.Sections)
            Sections.Add(new GuideSectionViewModel(section));

        OpenLinkCommand = new RelayCommand(p => Open((p as GuideLink)?.Url));
    }

    public ObservableCollection<GuideSectionViewModel> Sections { get; } = new();

    public RelayCommand OpenLinkCommand { get; }

    public string Subtitle =>
        "The parts of Elin modding that go wrong quietly - a portrait the game loads and "
        + "never shows, a spreadsheet row that throws away everything under it. Everything "
        + "else is in the community's own documentation, linked at the bottom.";

    private static void Open(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppLog.Error($"Could not open {url}", ex);
        }
    }
}
