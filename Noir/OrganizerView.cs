using System.Windows;
using System.Windows.Controls;

namespace Noir;

public partial class MainWindow
{
    void OpenOrganizer()
    {
        if (current != null || scanning || metadataRefreshing)
        {
            Status.Text = "Finish playback or the library refresh before organizing files.";
            return;
        }
        var dialog = OpenSheet("Organize your files", "A tidy library", SheetKind.Side, 720);
        var panel = new StackPanel();
        dialog.Add(panel);
        var plan = store.PlanOrganization();
        panel.Children.Add(
            Body(
                "Shows: Title (Year) / Season 01 / Title (Year) S01E01\nMovies: Title (Year) / Title (Year)\nMatching subtitles follow the video. Watch history and saved times stay attached.",
                13
            )
        );
        panel.Children.Add(SheetSection($"{plan.Moves.Count(m => m.Video)} videos ready to organize"));
        var message = Body("Original paths are kept for Undo. Nothing is overwritten or deleted.", 12);
        panel.Children.Add(message);
        var controls = dialog.FooterButtons;
        async Task Run(Func<int> operation)
        {
            if (current != null || scanning || metadataRefreshing)
            {
                message.Text = "Wait for playback or refresh to finish.";
                return;
            }
            controls.IsEnabled = false;
            StopWatchingFolders();
            try
            {
                var count = await Task.Run(operation);
                await ScanLibrary(false);
                message.Text = $"Finished · {count} videos. History and saved times kept.";
            }
            catch (Exception e)
            {
                store.Log(e);
                await ScanLibrary(false);
                message.Text = "Stopped safely: " + e.Message + " · You can undo completed changes.";
            }
            finally
            {
                controls.IsEnabled = true;
                WatchLibraryFolders();
            }
        }
        dialog.Action("Archive changes", () =>
        {
            store.ArchiveOrganization();
            message.Text = "Change record archived. Close and reopen this preview to organize more files.";
        });
        dialog.Action("Undo last organization", async () =>
            await Run(() => store.Organize(store.LastOrganization() ?? new(), true))
        );
        var apply = dialog.Action("Organize these files", async () => await Run(() => store.Organize(plan)), true);
        apply.IsEnabled = plan.Moves.Any();
        foreach (var move in plan.Moves.Where(m => m.Video))
        {
            panel.Children.Add(Body(move.From, 11));
            panel.Children.Add(Text("→ " + move.To, 12, "#E4C084"));
        }
        if (plan.Skipped.Count > 0)
        {
            panel.Children.Add(SheetSection("Left as they are"));
            foreach (var skipped in plan.Skipped)
                panel.Children.Add(Body(skipped, 12));
        }
    }
}
