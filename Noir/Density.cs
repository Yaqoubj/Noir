using System.IO;
using System.Text.Json;
using System.Windows;

namespace Noir;

internal enum UiDensity
{
    Compact,
    Comfortable,
}

internal static class Density
{
    internal static UiDensity Mode { get; private set; } = UiDensity.Compact;
    internal static bool Compact => Mode == UiDensity.Compact;

    internal static double Pick(double compact, double comfortable) => Compact ? compact : comfortable;

    internal static double Sidebar => Pick(212, 236);
    internal static double NavFont => Pick(13.5, 14);
    internal static Thickness NavPadding => Compact ? new(12, 8, 12, 8) : new(14, 11, 14, 11);
    internal static double TopBar => Pick(60, 78);
    internal static double Search => Pick(38, 42);
    internal static double IconButton => Pick(36, 42);
    internal static double ButtonFont => Pick(13, 14);
    internal static Thickness ButtonPadding => Compact ? new(14, 7, 14, 7) : new(17, 11, 17, 11);
    internal static Thickness InputPadding => Compact ? new(12, 7, 12, 7) : new(12, 9, 12, 9);
    internal static double HeroPillFont => Pick(13, 15);
    internal static Thickness HeroPillPadding => Compact ? new(18, 9, 18, 9) : new(26, 12, 28, 12);
    internal static double HeroLinkFont => Pick(13, 14.5);
    internal static Thickness HeroLinkPadding => Compact ? new(12, 9, 12, 9) : new(16, 12, 16, 12);
    internal static Thickness HeroButtonPadding => Compact ? HeroPillPadding : new(28, 14, 30, 14);
    internal static double HeroButtonFont => Pick(13, 16);
    internal static double ChipFont => Pick(12, 14);
    internal static Thickness ChipPadding => Compact ? new(12, 6, 12, 6) : new(16, 8, 16, 8);
    internal static double SectionTitle => Pick(18, 21);
    internal static double SectionSubtitle => Pick(11.5, 12);
    internal static Thickness SectionMargin => Compact ? new(0, 18, 0, 12) : new(0, 24, 0, 15);
    internal static Thickness RailHeadingMargin => Compact ? new(0, 16, 0, 10) : new(0, 22, 0, 14);
    internal static double Text => Pick(13, 14);
    internal static double Body => Pick(13, 14);
    internal static Thickness BodyMargin => Compact ? new(0, 3, 0, 9) : new(0, 4, 0, 12);
    internal static double PageTitle => Pick(30, 40);

    internal static double PageSide(double width) =>
        width < 1000 ? Pick(20, 24)
        : width < 1400 ? Pick(28, 40)
        : Pick(36, 56);

    internal static double HomeHeroMin => Pick(380, 500);
    internal static double HomeHeroFactor => Pick(.56, .78);
    internal static double DetailHeroMin => Pick(360, 440);
    internal static double DetailHeroFactor => Pick(.52, .66);
    internal static double CarouselMin => Pick(380, 620);

    internal static double HeroTitle(bool longTitle) => longTitle ? Pick(40, 50) : Pick(48, 66);

    internal static double HeroLine(bool longTitle) => longTitle ? Pick(45, 56) : Pick(54, 72);

    internal static double HeroSummary => Pick(14, 15);
    internal static double HeroSummaryLine => Pick(20, 22);
    internal static double HeroSummaryMax => HeroSummaryLine * 3;

    internal static double HeroLogoHeight(bool detail) => detail ? Pick(64, 120) : Pick(72, 150);

    internal static double HeroLogoWidth => Pick(320, 480);
    internal static double HeroRound => Pick(38, 48);
    internal static double PosterWidth => Pick(150, 176);
    internal static double PosterHeight => Pick(225, 264);
    internal static double LatestWidth => Pick(260, 300);
    internal static double LatestHeight => Pick(146, 168);
    internal static double ContinueWidth => Pick(272, 312);
    internal static double ContinueHeight => Pick(153, 176);
    internal static double CardGap => Pick(14, 18);
    internal static double CardTitle => Pick(13, 14);
    internal static double DiscoveryWide => Pick(400, 470);
    internal static double DiscoveryWideHeight => Pick(225, 264);

    internal static double LibraryWidth(string size) =>
        size == "S" ? Pick(120, 140)
        : size == "L" ? Pick(192, 224)
        : Pick(150, 180);

    internal static double LibraryListHeight(string size) =>
        size == "S" ? Pick(62, 72)
        : size == "L" ? Pick(102, 120)
        : Pick(80, 94);

    internal static double MoreWidth => Pick(30, 34);
    internal static double MoreHeight => Pick(28, 30);
    internal static double MoreFont => Pick(20, 22);
    internal static double EpisodeActionMin => 100;
    static string? file;

    internal static void Load(string directory)
    {
        file = Path.Combine(directory, "ui-density.json");
        Mode = UiDensity.Compact;
        try
        {
            if (
                File.Exists(file)
                && JsonSerializer.Deserialize<string>(File.ReadAllText(file)) == nameof(UiDensity.Comfortable)
            )
                Mode = UiDensity.Comfortable;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        Apply();
    }

    internal static void Set(UiDensity mode, bool save = true)
    {
        Mode = mode;
        Apply();
        if (save && file != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(mode.ToString()));
        }
    }

    internal static void Apply()
    {
        if (Application.Current == null)
            return;
        var r = Application.Current.Resources;
        r["Density.Sidebar"] = new GridLength(Sidebar);
        r["Density.InputPadding"] = InputPadding;
        r["Density.NavFont"] = NavFont;
        r["Density.NavPadding"] = NavPadding;
        r["Density.TopBar"] = TopBar;
        r["Density.Search"] = Search;
        r["Density.IconButton"] = IconButton;
        r["Density.ButtonFont"] = ButtonFont;
        r["Density.ButtonPadding"] = ButtonPadding;
        r["Density.HeroPillFont"] = HeroPillFont;
        r["Density.HeroPillPadding"] = HeroPillPadding;
        r["Density.HeroLinkFont"] = HeroLinkFont;
        r["Density.HeroLinkPadding"] = HeroLinkPadding;
        r["Density.PageTitle"] = PageTitle;
        r["Density.Text"] = Text;
    }
}
