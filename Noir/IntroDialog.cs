using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Noir;

public partial class MainWindow
{
    async void Intro_Click(object sender, RoutedEventArgs e)
    {
        if (current == null || player == null)
            return;
        var resume = player.IsPlaying;
        if (resume)
            player.SetPause(true);
        SavePlayback();
        var intro = store.GetIntro(current);
        var outro = store.GetOutro(current);
        var dialog = OpenSheet(
            "Intro & credits · " + current.Title,
            "Use seconds or mm:ss. Openings can be skipped; credits mark the video watched while it keeps playing.",
            SheetKind.Side,
            460
        );
        var content = new StackPanel();
        dialog.Add(content);
        content.Children.Add(SheetSection("Opening", "Optional. Leave both blank for no skip button."));
        var startLabel = Text("Intro starts · optional", 12, "#9F9A93");
        startLabel.Margin = new(0, 6, 0, 0);
        content.Children.Add(startLabel);
        var start = new TextBox
        {
            Text = intro != null ? LibraryStore.Time(intro.Start) : "",
            Margin = new(0, 4, 0, 8),
            Padding = new(10, 7, 10, 7),
        };
        System.Windows.Automation.AutomationProperties.SetName(start, "Intro starts");
        content.Children.Add(start);
        content.Children.Add(Text("Intro ends", 12, "#9F9A93"));
        var end = new TextBox
        {
            Text = intro != null ? LibraryStore.Time(intro.End) : "",
            Margin = new(0, 4, 0, 0),
            Padding = new(10, 7, 10, 7),
        };
        System.Windows.Automation.AutomationProperties.SetName(end, "Intro ends");
        content.Children.Add(end);
        content.Children.Add(
            SheetSection("Credits start", "Optional. Leave blank to use the detected ending or the near-end fallback.")
        );
        var credits = new TextBox
        {
            Text = outro != null ? LibraryStore.Time(outro.Start) : "",
            Margin = new(0, 4, 0, 8),
            Padding = new(10, 7, 10, 7),
        };
        System.Windows.Automation.AutomationProperties.SetName(credits, "Credits start");
        content.Children.Add(credits);
        content.Children.Add(
            ActionButton("Use current position for credits", () => credits.Text = LibraryStore.Time(lastPosition))
        );
        var all = new CheckBox
        {
            Content = "Use these times for this whole season",
            IsEnabled = !current.IsMovie,
            Margin = new(0, 12, 0, 0),
        };
        content.Children.Add(all);
        var error = Text("", 12, "#FFAC9C");
        error.Margin = new(0, 8, 0, 0);
        content.Children.Add(error);
        var item = current;
        dialog.Action("Cancel", () => dialog.Close(false));
        dialog.Action(
            "Save times",
            () =>
            {
                var haveIntro = start.Text.Trim().Length > 0 || end.Text.Trim().Length > 0;
                double a = 0,
                    b = 0,
                    c = 0;
                if (
                    haveIntro
                    && (
                        !ParseTime(start.Text, out a)
                        || !ParseTime(end.Text, out b)
                        || !new IntroSegment { Start = a, End = b }.Valid
                        || (currentDuration > 0 && b >= currentDuration)
                    )
                )
                {
                    error.Text = "Enter a valid opening range, or leave both opening fields blank.";
                    return;
                }
                var haveCredits = credits.Text.Trim().Length > 0;
                if (
                    haveCredits
                    && (
                        !ParseTime(credits.Text, out c)
                        || c < currentDuration * .5
                        || c >= currentDuration
                        || currentDuration - c > 600
                    )
                )
                {
                    error.Text = "Credits must start in the final 10 minutes and after halfway through the video.";
                    return;
                }
                var introKey = all.IsChecked == true ? item.GroupKey + ":season:" + item.Season : item.Path;
                var outroKey = all.IsChecked == true ? item.GroupKey + ":season:" + item.Season : item.Id;
                if (haveIntro)
                    store.State.Intros[introKey] = new() { Start = a, End = b };
                else
                    store.State.Intros.Remove(introKey);
                if (haveCredits)
                    store.State.Outros[outroKey] = new() { Start = c, End = currentDuration };
                else
                    store.State.Outros.Remove(outroKey);
                if (all.IsChecked == true)
                    store.State.Intros.Remove(item.Path);
                if (all.IsChecked == true)
                    store.State.Outros.Remove(item.Id);
                store.Save();
                dialog.Close(true);
            },
            true
        );
        await dialog.Done;
        if (resume && !ended && player != null)
            player.SetPause(false);
    }

    static bool ParseTime(string text, out double value)
    {
        value = 0;
        var parts = text.Trim().Split(':');
        if (parts.Length > 3 || parts.Length == 0)
            return false;
        foreach (var p in parts)
        {
            if (!double.TryParse(p, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) || v < 0)
                return false;
            value = value * 60 + v;
        }
        return double.IsFinite(value);
    }
}
