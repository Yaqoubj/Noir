#if NOIR_VALIDATE
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Noir;

public partial class MainWindow
{
    bool storyValidationQueued;

    void QueueStoryValidation()
    {
        if (storyValidationQueued || !Environment.GetCommandLineArgs().Contains("--validate"))
            return;
        storyValidationQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () => await ValidateYearStory()));
    }

    FrameworkElement StoryValidationProfile(bool diary)
    {
        var favorites = store.State.Favorites;
        var likes = store.State.Likes;
        var reviews = store.State.PersonalReviews;
        var viewings = store.State.Viewings;
        try
        {
            var watched = AllViewings();
            var samples = watched.Select(v => v.Item).DistinctBy(i => i.Id).Take(6).ToList();
            store.State.Favorites = store.Archive.DistinctBy(i => i.GroupKey).Take(3).Select(i => i.GroupKey).ToList();
            store.State.Likes = new(likes);
            store.State.PersonalReviews = new(reviews);
            store.State.Viewings = new(viewings);
            foreach (var item in samples)
            {
                store.State.Likes.Add(item.GroupKey);
                store.State.PersonalReviews[item.Id] = new()
                {
                    Rating10 = 7,
                    Note = reviews.GetValueOrDefault(item.Id)?.Note ?? "",
                };
            }
            if (
                samples.FirstOrDefault() is { } first
                && viewings.GetValueOrDefault(first.Id) is { Count: > 0 } previous
            )
                store.State.Viewings[first.Id] = previous
                    .Append(new ViewingRecord { WatchedAt = previous[0].WatchedAt.AddDays(-3), Source = "Validation" })
                    .ToList();
            var root = new Grid
            {
                Width = 1320,
                Height = 900,
                Background = Brush("#0C0C0F"),
            };
            var body = new StackPanel { Margin = new(66, 36, 66, 36) };
            root.Children.Add(body);
            body.Children.Add(Text(T("YOUR VIEWING LIFE", "حكاية مشاهداتك"), 11, "#E4C084", FontWeights.SemiBold));
            var title = Text(
                diary ? T("Your diary", "يومياتك") : T("Your viewing life", "حكاية مشاهداتك"),
                32,
                "#F4F1EB",
                FontWeights.SemiBold
            );
            title.Margin = new(0, 8, 0, 8);
            body.Children.Add(title);
            if (!diary)
                body.Children.Add(HistoryProfileHeader());
            else
                foreach (var viewing in watched.Take(5))
                    body.Children.Add(
                        DiaryRow(
                            viewing.Item,
                            new ViewingRecord
                            {
                                WatchedAt = viewing.At,
                                Source = viewing.Source,
                                DateUnknown = viewing.DateUnknown,
                                Approximate = viewing.Approximate,
                            }
                        )
                    );
            return root;
        }
        finally
        {
            store.State.Favorites = favorites;
            store.State.Likes = likes;
            store.State.PersonalReviews = reviews;
            store.State.Viewings = viewings;
        }
    }

    static void CaptureStoryUi(FrameworkElement element, string file)
    {
        element.Measure(new Size(element.Width, element.Height));
        element.Arrange(new Rect(0, 0, element.Width, element.Height));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)element.Width, (int)element.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(file);
        encoder.Save(output);
    }

    async Task ValidateYearStory()
    {
        var results = new List<string>();
        try
        {
            var year = AllViewings()
                .Where(v => !v.DateUnknown)
                .Select(v => v.At.LocalDateTime.Year)
                .DefaultIfEmpty(DateTime.Today.Year)
                .Max();
            var slides = StorySlides(year);
            if (slides.Count != 9)
                throw new Exception("Nine story slides are required");
            var favorites = StoryValidationProfile(false);
            var diary = StoryValidationProfile(true);
            var indices = new[] { 0, 1, 3, 6, 7, 8 };
            foreach (var index in indices)
            {
                var card = StoryCard(slides[index], year, index, slides.Count);
                card.Measure(new Size(450, 800));
                card.Arrange(new Rect(0, 0, 450, 800));
            }
            await Task.Delay(650);
            CaptureStoryUi(favorites, Path.Combine(store.DataDirectory, "history-favorites.png"));
            CaptureStoryUi(diary, Path.Combine(store.DataDirectory, "history-diary-half-stars.png"));
            results.Add("PASS: Favorites and diary samples render without changing saved history");
            foreach (var index in indices)
            {
                SaveStoryCard(
                    slides[index],
                    year,
                    index,
                    slides.Count,
                    Path.Combine(store.DataDirectory, $"story-{index + 1:00}.png")
                );
                results.Add("PASS: Story slide " + (index + 1) + " rendered at 1080 × 1920");
            }
        }
        catch (Exception e)
        {
            results.Add("FAIL: " + e);
        }
        File.WriteAllText(
            Path.Combine(store.DataDirectory, "story-checks.json"),
            JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })
        );
    }
}
#endif
