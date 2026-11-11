#if NOIR_VALIDATE
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Noir;

public partial class MainWindow
{
    bool detailShowcaseStarted;
    public Task DetailShowcaseTask { get; private set; } = Task.CompletedTask;

    void DetailValidationHook(VideoItem item, FrameworkElement host)
    {
        if (
            detailShowcaseStarted
            || !validationMode
            || ActualWidth < 1200
            || !Environment.GetCommandLineArgs().Contains("--p2-detail-shots")
        )
            return;
        detailShowcaseStarted = true;
        DetailShowcaseTask = ValidateDetailShowcase(item);
    }

    public async Task ValidateDetailShowcase(VideoItem item)
    {
        Window? detail = null,
            personWindow = null;
        try
        {
            var people = new TitleCredits
            {
                Cast =
                [
                    new()
                    {
                        Name = "Bryan Cranston",
                        Character = "Walter White",
                        TvmazeId = 14245,
                        PhotoUrl = "https://static.tvmaze.com/uploads/images/medium_portrait/195/488839.jpg",
                    },
                    new()
                    {
                        Name = "Aaron Paul",
                        Character = "Jesse Pinkman",
                        TvmazeId = 12328,
                        PhotoUrl = "https://static.tvmaze.com/uploads/images/medium_portrait/264/660079.jpg",
                    },
                    new()
                    {
                        Name = "Anna Gunn",
                        Character = "Skyler White",
                        TvmazeId = 11207,
                        PhotoUrl = "https://static.tvmaze.com/uploads/images/medium_portrait/2/6596.jpg",
                    },
                    new()
                    {
                        Name = "Dean Norris",
                        Character = "Hank Schrader",
                        TvmazeId = 9,
                        PhotoUrl = "https://static.tvmaze.com/uploads/images/medium_portrait/461/1154757.jpg",
                    },
                    new()
                    {
                        Name = "Bob Odenkirk",
                        Character = "Saul Goodman",
                        TvmazeId = 21268,
                        PhotoUrl = "https://static.tvmaze.com/uploads/images/medium_portrait/54/136764.jpg",
                    },
                    new()
                    {
                        Name = "Jonathan Banks",
                        Character = "Mike Ehrmantraut",
                        TvmazeId = 12153,
                        PhotoUrl = "https://static.tvmaze.com/uploads/images/medium_portrait/2/5737.jpg",
                    },
                ],
                Directors = [new() { Name = "Vince Gilligan" }],
                Writers = [new() { Name = "Vince Gilligan" }, new() { Name = "Peter Gould" }],
            };
            var videos = new List<TrailerInfo>
            {
                new()
                {
                    YouTubeId = "HhesaQXLuRY",
                    Name = T("Official trailer", "الإعلان الرسمي"),
                    Kind = "Trailer",
                    Official = true,
                },
                new()
                {
                    YouTubeId = "HhesaQXLuRY",
                    Name = T("Inside the story", "داخل القصة"),
                    Kind = "Featurette",
                },
                new()
                {
                    YouTubeId = "HhesaQXLuRY",
                    Name = T("A closer look", "نظرة عن قرب"),
                    Kind = "Teaser",
                },
            };
            var body = new StackPanel { Margin = new(34, 28, 34, 24) };
            body.Children.Add(Text("Breaking Bad", 38, weight: FontWeights.Bold));
            var facts = Text(T("2008–2013 · Crime · Drama", "٢٠٠٨–٢٠١٣ · جريمة · دراما"), 13, "#B2A99C");
            facts.Margin = new(0, 10, 0, 26);
            body.Children.Add(facts);
            var cast = new StackPanel();
            RenderCreditStrip(cast, people);
            body.Children.Add(cast);
            var extras = new StackPanel { Margin = new(0, 25, 0, 0) };
            RenderExtraRail(extras, videos);
            body.Children.Add(extras);
            detail = new Window
            {
                Owner = this,
                Title = "Noir · Detail validation",
                Width = 1060,
                Height = 640,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brush("#101012"),
                Foreground = Brush("#F4F1EB"),
                Content = new ScrollViewer
                {
                    Content = body,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                },
                ShowActivated = false,
            };
            detail.Show();
            var known = store
                .Archive.GroupBy(i => i.GroupKey)
                .Select(g => g.First())
                .Take(6)
                .Select(i => new CreditTitle
                {
                    ImdbId = i.ImdbId,
                    Name = i.Title,
                    Year = i.Year,
                    IsMovie = i.IsMovie,
                    Poster = i.Poster,
                    Role = T("Cast", "تمثيل"),
                })
                .ToList();
            if (known.Count == 0)
                known.Add(
                    new()
                    {
                        Name = "Breaking Bad",
                        ImdbId = "tt0903747",
                        Year = 2008,
                        Role = "Walter White",
                    }
                );
            personWindow = OpenPersonWindow(people.Cast[0], known);
            personWindow.ShowActivated = false;
            await Task.Delay(1400);
            if (closing)
                return;
            detail.UpdateLayout();
            personWindow.UpdateLayout();
            CaptureDetailVisual(
                (FrameworkElement)detail.Content,
                Path.Combine(store.DataDirectory, "p2-detail-credits.png")
            );
            CaptureDetailVisual(
                (FrameworkElement)personWindow.Content,
                Path.Combine(store.DataDirectory, "p2-person.png")
            );
            await Task.Delay(1400);
            if (!closing)
            {
                CaptureDetailVisual(
                    (FrameworkElement)detail.Content,
                    Path.Combine(store.DataDirectory, "p2-detail-credits.png")
                );
                CaptureDetailVisual(
                    (FrameworkElement)personWindow.Content,
                    Path.Combine(store.DataDirectory, "p2-person.png")
                );
            }
        }
        catch (Exception error)
        {
            store.Log(error);
            await File.WriteAllTextAsync(
                Path.Combine(store.DataDirectory, "p2-detail-showcase-error.txt"),
                error.ToString()
            );
        }
        finally
        {
            personWindow?.Close();
            detail?.Close();
        }
    }

    static void CaptureDetailVisual(FrameworkElement visual, string file)
    {
        visual.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(visual.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(visual.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(file);
        encoder.Save(output);
    }
}
#endif
