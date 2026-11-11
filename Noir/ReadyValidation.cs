#if NOIR_VALIDATE
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Noir;

public partial class MainWindow
{
    bool readyValidationQueued;

    // The Downloads validation waits for this, so the two suites never fight over the sheets.
    Task? readyValidationRun;

    void QueueReadyValidation()
    {
        if (readyValidationQueued || !Environment.GetCommandLineArgs().Contains("--downloads-only"))
            return;
        readyValidationQueued = true;
        readyValidationRun = Dispatcher.InvokeAsync(async () => await ValidateReadySurfaces()).Task.Unwrap();
    }

    public async Task ValidateReadySurfaces()
    {
        var checks = new List<string>();
        NoirSheet? sheet = null;
        void Check(bool ok, string name)
        {
            checks.Add((ok ? "PASS: " : "FAIL: ") + name);
        }
        void Picture(FrameworkElement element, string name)
        {
            element.UpdateLayout();
            var w = (int)element.ActualWidth;
            var h = (int)element.ActualHeight;
            if (w < 1 || h < 1)
                return;
            var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
                context.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, w, h));
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(store.DataDirectory, name));
            encoder.Save(stream);
        }
        try
        {
            if (downloads == null)
                throw new IOException("No validation downloads");
            var manager = downloads;
            var item = store.Archive.First(i => !i.IsMovie);
            var snapshot = manager.ReadyState;
            await manager.ChangeReady(s =>
            {
                s.Titles[item.GroupKey] = new()
                {
                    Group = item.GroupKey,
                    Title = item.Title,
                    ItemId = item.Id,
                    State = "Preparing",
                    Reason = "Preparing on this PC",
                    Bytes = 2 * NoirReadyPolicy.GB,
                    Quality = "1080p",
                    Episodes =
                    [
                        new(
                            item.Id,
                            item.GroupKey,
                            item.Title,
                            1,
                            2,
                            false,
                            false,
                            false,
                            DateTimeOffset.UtcNow.AddDays(-1)
                        ),
                    ],
                };
                s.Titles["ready-budget-fixture"] = new()
                {
                    Group = "ready-budget-fixture",
                    Title = "Evening watch",
                    State = "Blocked",
                    Reason = "Preparation budget reached",
                    Quality = "1080p",
                    Episodes = [],
                };
                s.ManagedFiles.Add(
                    new()
                    {
                        JobId = "ready-validation",
                        Title = item.Title + " · S01 E01",
                        RemoveAt = DateTimeOffset.Now.AddDays(7),
                    }
                );
                s.Log("Queued confirmed ready file", item.Title);
                s.Log("Watched file removal scheduled", item.Title);
            });
            RefreshReadyCentre();
            Check(
                !Descendants(readyCentreHost!)
                    .OfType<TextBlock>()
                    .Any(t => t.Text is "Preparation centre" or "Noir Ready"),
                "The old Preparation centre / Noir Ready names are gone (U50)"
            );
            Check(
                Descendants(readyCentreHost!).OfType<TextBlock>().Any(t => t.Text == "Keep ready budget reached"),
                "Keep ready explains exact storage block"
            );
            keepReadyExpander!.IsExpanded = true;
            UpdateLayout();
            Check(
                Descendants(ContentPanel).OfType<Expander>().Any(x => ReferenceEquals(x, keepReadyExpander)),
                "The Keep ready centre sits under a Keep ready block on the Downloads page"
            );
            var expander = Descendants(readyCentreHost!).OfType<Expander>().First();
            expander.IsExpanded = true;
            UpdateLayout();
            Picture(readyCentreHost!, "ready-preparation-centre.png");
            Check(
                Descendants(readyCentreHost!).OfType<Button>().Any(b => b.Content as string == "Keep this file")
                    && !Descendants(readyCentreHost!)
                        .OfType<Button>()
                        .Any(b => b.Content as string is "Cancel removal" or "Keep file"),
                "A file scheduled for removal offers one button, Keep this file (U51)"
            );
            var visualItem = new VideoItem
            {
                Id = "ready-visual-fixture",
                CollectionKey = "ready-visual-fixture",
                Title = "The Northwater",
                Season = 1,
                Episode = 1,
                ImdbId = "tt123",
                Available = false,
            };
            store.State.Catalog[visualItem.Id] = new() { Item = visualItem };
            store.State.EpisodeSchedules[visualItem.GroupKey] =
            [
                new()
                {
                    Season = 1,
                    Episode = 1,
                    AirsAt = DateTimeOffset.UtcNow.AddDays(-2),
                },
                new()
                {
                    Season = 1,
                    Episode = 2,
                    AirsAt = DateTimeOffset.UtcNow.AddDays(-1),
                },
            ];
            ShowKeepReady(visualItem);
            sheet = FindSheet("Keep ready") ?? throw new IOException("Keep ready did not open as a sheet");
            if (keepReadyPreviewTask != null)
                await Task.WhenAny(keepReadyPreviewTask, Task.Delay(180));
            await Task.Delay(300);
            sheet.Panel.UpdateLayout();
            Picture(sheet.Panel, "ready-keep-ready-sheet.png");
            {
                var host = (FrameworkElement)LibraryRoot.Parent;
                host.UpdateLayout();
                var bitmap = new RenderTargetBitmap(
                    (int)host.ActualWidth,
                    (int)host.ActualHeight,
                    96,
                    96,
                    PixelFormats.Pbgra32
                );
                bitmap.Render(host);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(store.DataDirectory, "keep-ready-sheet-en.png"));
                encoder.Save(output);
            }
            Check(
                OwnedWindows.Count == 0
                    && sheet.Kind == SheetKind.Side
                    && sheet.Panel.CornerRadius.TopLeft >= 14
                    && sheet.Footer.Visibility == Visibility.Visible
                    && sheet.FooterButtons.Children.OfType<Button>().Last().Content as string
                        == "Keep ready on this PC",
                "Keep ready is a side sheet whose footer ends with the primary action"
            );
            Check(
                Descendants(sheet.Overlay).OfType<ComboBox>().Any(c => c.Items.Count == 4),
                "Keep ready offers 1, 2, 3 and whole season"
            );
            Check(
                Descendants(sheet.Overlay)
                    .OfType<CheckBox>()
                    .Any(c => c.Content as string == "Remove watched after 7 days"),
                "Keep ready cleanup requires explicit consent"
            );
            // The settings controls save on a background thread; this once threw a cross-thread error.
            var before = manager.ReadyViewState;
            var settings = ReadySettingsPanel();
            var checkBoxes = Descendants(settings).OfType<CheckBox>().ToList();
            var pause = checkBoxes.First(c => c.Content as string == "Pause keep ready");
            pause.IsChecked = !before.Paused;
            pause.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            for (var i = 0; i < 40 && manager.ReadyViewState.Paused == before.Paused; i++)
                await Task.Delay(50);
            Check(manager.ReadyViewState.Paused != before.Paused, "Pause keep ready saves from Settings");
            var cleanupBox = checkBoxes.First(c => c.Content as string == "Remove watched after 7 days");
            cleanupBox.IsChecked = !before.CleanupWatched;
            cleanupBox.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(
                !Descendants(settings).OfType<Button>().Any(b => b.Content as string == "Save preparation settings"),
                "Preparation settings have no Save button (U8)"
            );
            for (var i = 0; i < 40 && manager.ReadyViewState.CleanupWatched == before.CleanupWatched; i++)
                await Task.Delay(50);
            Check(
                manager.ReadyViewState.CleanupWatched != before.CleanupWatched,
                "The cleanup choice saves as soon as it is clicked"
            );
            await manager.ChangeReady(s =>
            {
                s.Paused = before.Paused;
                s.CleanupWatched = before.CleanupWatched;
            });
            sheet.Close(false);
            sheet = null;
            store.State.Catalog.Remove(visualItem.Id);
            store.State.EpisodeSchedules.Remove(visualItem.GroupKey);
            await manager.ChangeReady(s =>
            {
                s.Titles = snapshot.Titles;
                s.ManagedFiles = snapshot.ManagedFiles;
                s.Why = snapshot.Why;
            });
            RefreshReadyCentre();
        }
        catch (Exception e)
        {
            checks.Add("FAIL: " + e.Message);
        }
        finally
        {
            sheet?.Close(false);
            File.WriteAllText(
                Path.Combine(store.DataDirectory, "ready-ui-checks.json"),
                JsonSerializer.Serialize(checks)
            );
        }
    }
}
#endif
