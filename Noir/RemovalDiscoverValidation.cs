#if NOIR_VALIDATE
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Noir;

public partial class MainWindow
{
    // --removal-discover-only: hiding/forgetting titles, the one-click player controls, and the
    // personal Discover rails and watch options (live Cinemeta). Screenshots go beside the profile.
    async Task ValidateRemovalDiscover()
    {
        var results = new List<string>();
        void Check(bool ok, string label)
        {
            if (!ok)
                throw new Exception(label);
            results.Add("PASS: " + label);
        }
        async Task Settle(int ms = 600)
        {
            UpdateLayout();
            await Task.Delay(ms);
            UpdateLayout();
        }
        void Shot(string name) => CaptureLibrary(Path.Combine(store.DataDirectory, "round-" + name + ".png"));
        bool Shown(string title) => Descendants(ContentPanel).OfType<TextBlock>().Any(t => t.Text == title);
        try
        {
            WindowState = WindowState.Normal;
            Width = 1320;
            Height = 900;
            var show = store
                .Archive.Where(i => !i.IsMovie && store.EverWatched(i))
                .GroupBy(i => i.GroupKey)
                .First()
                .First();
            var viewings = store.EpisodesFor(show.GroupKey).Sum(i => store.ViewingHistory(i).Count);
            Navigate("Shows");
            await Settle();
            Check(Shown(show.Title), "The show is listed before removal");
            store.SetHidden(show.GroupKey, true);
            Navigate("Shows");
            await Settle();
            Check(!Shown(show.Title), "A removed show leaves the Shows page");
            Check(
                store.EpisodesFor(show.GroupKey).Sum(i => store.ViewingHistory(i).Count) == viewings,
                "Removing keeps its watch history"
            );
            trackerTab = "Shows";
            Navigate("History");
            await Settle();
            Check(Shown(show.Title), "History still lists the removed show");
            store.SetHidden(show.GroupKey, false);
            Navigate("Shows");
            await Settle();
            Check(Shown(show.Title), "Show in library again restores it");
            var movie = store.Archive.FirstOrDefault(i => i.IsMovie);
            if (movie != null)
            {
                store.ForgetTitle(movie.GroupKey);
                Check(
                    !store.Archive.Any(i => i.GroupKey == movie.GroupKey)
                        && !store.State.Viewings.ContainsKey(movie.Id)
                        && store.IsHidden(movie.GroupKey),
                    "Forgetting removes the title and its history and keeps it hidden"
                );
            }
            var episode = store.Archive.FirstOrDefault(CanPlay);
            if (episode != null)
            {
                await PlayVideo(episode);
                await Settle(1500);
                HideControls();
                await Settle(200);
                HandleVideoClick(1);
                Check(ControlsVisible, "One click on the video shows the controls immediately");
                HandleVideoClick(1);
                Check(!ControlsVisible, "Another click hides them");
                Check(
                    Window.GetWindow(PlayerOverlay) is not { } overlay
                        || overlay == this
                        || overlay.Tag as string == "noir-keys",
                    "Player keys are wired to the video overlay window"
                );
                ReturnToLibrary();
                await Settle();
            }
            Navigate("Discover");
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (discoverResults == null && DateTime.UtcNow < deadline)
                await Task.Delay(100);
            await Settle(6000);
            ContentScroll.ScrollToVerticalOffset(700);
            await Settle(800);
            Shot("discover-personal");
            Check(
                Descendants(ContentPanel)
                    .OfType<TextBlock>()
                    .Any(t => t.Text.StartsWith("Because you watched") || t.Text == "Picked for you"),
                "Discover shows personal recommendation rows"
            );
            var title = discoverResults!.Titles.First(t => !t.IsMovie);
            await OpenDiscover(title);
            await Settle(3000);
            Check(
                Descendants(ContentPanel)
                    .OfType<Button>()
                    .Any(b => IconSet.Src(b.Content) == "[[play]]  Stream in Stremio"),
                "Title pages offer Stream in Stremio"
            );
            Shot("discover-detail");
            ContentScroll.ScrollToEnd();
            await Settle(2500);
            Shot("discover-more-like-this");
        }
        catch (Exception error)
        {
            results.Add("FAIL: " + error);
        }
        File.WriteAllText(
            Path.Combine(store.DataDirectory, "removal-discover-checks.json"),
            JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })
        );
    }
}
#endif
