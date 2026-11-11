using System.IO;
using System.Text.Json;
#if !NOIR_HEADLESS
using System.Windows;
using System.Windows.Controls;
#endif

namespace Noir;

// Profile-local preferences; kept separate from the shared phone library format.
public sealed class HistorySettings
{
    public int WeeklyGoal { get; set; } = 5;
    public string AfterWatching { get; set; } = "Always";
    public int JustWatchedIgnores { get; set; }
    public bool JustWatchedQuiet { get; set; }
    public bool JustWatchedNoticeShown { get; set; }

    public static HistorySettings Load(string directory)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<HistorySettings>(
                File.ReadAllText(Path.Combine(directory, "history-settings.json"))
            ) ?? new();
            settings.WeeklyGoal = Math.Clamp(settings.WeeklyGoal, 0, 100);
            if (settings.AfterWatching is not ("Always" or "Finales" or "Never"))
                settings.AfterWatching = "Always";
            settings.JustWatchedIgnores = Math.Clamp(settings.JustWatchedIgnores, 0, 5);
            return settings;
        }
        catch (IOException)
        {
            return new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    public void Save(string directory)
    {
        var path = Path.Combine(directory, "history-settings.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this));
        File.Move(path + ".tmp", path, true);
    }
}

#if !NOIR_HEADLESS
public partial class MainWindow
{
    void SaveHistoryPrefs()
    {
        try
        {
            HistoryPrefs.Save(store.DataDirectory);
        }
        catch (IOException error)
        {
            store.Log(error);
        }
    }

    void SetAfterWatching(string choice)
    {
        HistoryPrefs.AfterWatching = choice;
        HistoryPrefs.JustWatchedIgnores = 0;
        HistoryPrefs.JustWatchedQuiet = false;
        SaveHistoryPrefs();
        DismissJustWatched(false);
    }

    // U53: Settings → History is gone; its one option is the "After watching" group in Playback.
    void RenderHistorySettings()
    {
        ContentPanel.Children.Add(Heading(T("After watching", "بعد المشاهدة")));
        var row = AfterWatchingSettingsRow();
        row.Margin = new(0, 4, 0, 12);
        ContentPanel.Children.Add(row);
    }

    FrameworkElement AfterWatchingSettingsRow()
    {
        var row = new StackPanel { Margin = new(0, 22, 0, 12) };
        row.Children.Add(Text(T("Ask me how it was", "اسألني عن رأيي"), 17, weight: FontWeights.SemiBold));
        var choices = new[]
        {
            T("Always", "دائمًا"),
            T("Finales and movies only", "الحلقات الختامية والأفلام فقط"),
            T("Never", "أبدًا"),
        };
        var values = new[] { "Always", "Finales", "Never" };
        var select = new ComboBox
        {
            ItemsSource = choices,
            SelectedIndex = Math.Max(0, Array.IndexOf(values, HistoryPrefs.AfterWatching)),
            MaxWidth = 330,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new(10, 7, 10, 7),
            Margin = new(0, 10, 0, 0),
        };
        System.Windows.Automation.AutomationProperties.SetName(select, T("Ask me how it was", "اسألني عن رأيي"));
        row.Children.Add(select);
        var normal = T(
            "A small, optional card. Reactions, ratings and notes are always available in History.",
            "بطاقة صغيرة اختيارية. التفاعلات والتقييمات والملاحظات متاحة دائمًا في السجل."
        );
        var help = Body(
            HistoryPrefs.JustWatchedQuiet
                ? T(
                    "Prompts are paused; you can still rate in History.",
                    "توقفت الأسئلة مؤقتًا؛ يمكنك التقييم في السجل دائمًا."
                )
                : normal,
            12
        );
        row.Children.Add(help);
        var resume = ActionButton(T("Keep asking", "استمر بالسؤال"), () =>
        {
            SetAfterWatching("Always");
            select.SelectedIndex = 0;
            RenderQuiet();
        });
        System.Windows.Automation.AutomationProperties.SetName(resume, T("Keep asking", "استمر بالسؤال"));
        resume.Visibility = HistoryPrefs.JustWatchedQuiet ? Visibility.Visible : Visibility.Collapsed;
        row.Children.Add(resume);
        select.SelectionChanged += (_, _) =>
        {
            if (select.SelectedIndex < 0)
                return;
            SetAfterWatching(values[select.SelectedIndex]);
            help.Text = normal;
            resume.Visibility = Visibility.Collapsed;
        };
        return row;
    }
}
#endif
