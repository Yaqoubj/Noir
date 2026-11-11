using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using LibVLCSharp.Shared.Structures;

namespace Noir;

// Skip recap/credits, the Up Next card, "Still watching?", chapters, speed and volume boost.
public partial class MainWindow
{
    readonly StillWatchingCounter stillWatching = new();
    readonly HashSet<SegmentKind> handledSegments = [];
    readonly List<(double Start, string Name)> chapterMarks = [];
    IntroSegment? recapSegment;
    SegmentKind activeSegment,
        noticeKind;
    double noticeTarget;
    DispatcherTimer? noticeTimer;
    DateTime countdownEnds,
        noticeEnds;
    TimeSpan? pausedCountdown,
        pausedNotice;
    const double CountdownSeconds = 8;
    int stillRequest;
    double sessionSpeed = 1;
    string speedGroup = "";
    VideoItem? askedNext;
    StreamOption? askedStreamNext;
    VideoItem? askedStream;

    void ResetPlayerFeatures(VideoItem item)
    {
        handledSegments.Clear();
        recapSegment = null;
        chapterMarks.Clear();
        ChapterTicks.Children.Clear();
        CancelSkipNotice();
        activeSegment = SegmentKind.None;
        StillWatchingPanel.Visibility = Visibility.Collapsed;
        askedNext = null;
        askedStream = null;
        if (item.GroupKey != speedGroup)
        {
            speedGroup = item.GroupKey;
            sessionSpeed = Prefs.DefaultSpeed;
        }
        UpdateSpeedButton();
        UpdateBoostButton();
        PlayerBackButton.Content = PL("[[back]]  Library", "[[back]]  المكتبة");
        PlayerEpisodesButton.Content = PL("Episodes", "الحلقات"); // U81
    }

    void LoadChapters(ChapterDescription[] chapters)
    {
        chapterMarks.Clear();
        for (var i = 0; i < chapters.Length; i++)
        {
            var start = chapters[i].TimeOffset / 1000d;
            chapterMarks.Add((start, (chapters[i].Name ?? "").Trim()));
            if (recapSegment == null && PlaybackPrefs.Classify(chapters[i].Name) == SegmentKind.Recap)
            {
                var end =
                    chapters[i].Duration > 0 ? start + chapters[i].Duration / 1000d
                    : i + 1 < chapters.Length ? chapters[i + 1].TimeOffset / 1000d
                    : 0;
                if (end > start && end - start < 600)
                    recapSegment = new() { Start = start, End = end };
            }
        }
        DrawChapterTicks();
    }

    void SeekBar_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChapterTicks();

    void DrawChapterTicks()
    {
        ChapterTicks.Children.Clear();
        var width = SeekBar.ActualWidth;
        if (currentDuration <= 0 || chapterMarks.Count < 2 || width < 40)
            return;
        foreach (var mark in chapterMarks.Where(m => m.Start > 1 && m.Start < currentDuration - 1))
        {
            var tick = new Rectangle
            {
                Width = 2.5,
                Height = 5,
                Fill = Brush("#E6101012"),
            };
            Canvas.SetLeft(tick, 7 + mark.Start / currentDuration * (width - 14) - 1.25);
            ChapterTicks.Children.Add(tick);
        }
    }

    string ChapterAt(double seconds) =>
        chapterMarks.Count < 2 ? "" : chapterMarks.LastOrDefault(m => m.Start <= seconds + .01).Name ?? "";

    void JumpChapter(int direction)
    {
        if (player == null || current == null)
            return;
        if (chapterMarks.Count < 2)
        {
            ShowPlayerHint(PL("This video has no chapters", "لا توجد فصول في هذا الفيديو"));
            return;
        }
        var origin = SeekOrigin;
        var target =
            direction > 0
                ? chapterMarks.FirstOrDefault(m => m.Start > origin + 1)
                : chapterMarks.LastOrDefault(m => m.Start < origin - 3);
        if (direction > 0 && target.Name == null)
        {
            ShowPlayerHint(PL("Last chapter", "الفصل الأخير"));
            return;
        }
        if (direction < 0 && target.Name == null)
            target = chapterMarks[0];
        SeekAbsolute(target.Start, false);
        var index = chapterMarks.IndexOf(target) + 1;
        ShowPlayerHint(
            PL("Chapter", "الفصل")
                + " "
                + index
                + (
                    target.Name.Length > 0 && !target.Name.StartsWith("Chapter", StringComparison.OrdinalIgnoreCase)
                        ? " · " + target.Name
                        : ""
                )
        );
    }

    // Decides which skip pill to show; Auto mode gives a two-second notice with Undo before skipping.
    void UpdateSkipSegments(double time)
    {
        if (current == null || player == null)
            return;
        var kind = SegmentKind.None;
        double end = 0;
        if (store.GetIntro(current) is { Valid: true } intro && time >= intro.Start && time < intro.End - .3)
        {
            kind = SegmentKind.Intro;
            end = intro.End;
            if (introHandled)
                handledSegments.Add(kind);
        }
        else if (recapSegment is { } recap && time >= recap.Start && time < recap.End - .3)
        {
            kind = SegmentKind.Recap;
            end = recap.End;
        }
        else if (
            nextItem == null
            && creditsReached
            && store.GetOutro(current) is { Valid: true } outro
            && time >= outro.Start
            && time < currentDuration - 2
        )
        {
            kind = SegmentKind.Credits;
            end = currentDuration;
        }
        if (kind != activeSegment)
        {
            activeSegment = kind;
            if (noticeKind != kind)
                CancelSkipNotice();
        }
        var mode = Prefs.ModeFor(kind);
        if (kind == SegmentKind.None || mode == SkipMode.Off)
        {
            if (noticeKind == SegmentKind.None)
                SkipIntro.Visibility = Visibility.Collapsed;
            return;
        }
        if (mode == SkipMode.Auto && !handledSegments.Contains(kind) && player.IsPlaying && !seeking)
        {
            handledSegments.Add(kind);
            noticeKind = kind;
            noticeTarget = end;
            SkipIntro.Content = SkipNoticeText(kind) + "   " + PL("Undo", "تراجع");
            SkipIntro.Visibility = Visibility.Visible;
            pausedNotice = null;
            noticeEnds = DateTime.UtcNow.AddSeconds(2);
            noticeTimer ??= new DispatcherTimer();
            noticeTimer.Interval = TimeSpan.FromSeconds(2);
            noticeTimer.Tick -= NoticeElapsed;
            noticeTimer.Tick += NoticeElapsed;
            noticeTimer.Stop();
            noticeTimer.Start();
            return;
        }
        if (noticeKind != SegmentKind.None)
            return;
        SkipIntro.Content = SkipButtonText(kind);
        SkipIntro.Visibility = Visibility.Visible;
    }

    static string SkipButtonText(SegmentKind kind) =>
        kind switch
        {
            SegmentKind.Recap => PL("Skip recap  [[forward]]", "تخطَّ الملخص  [[back]]"),
            SegmentKind.Credits => PL("Skip credits  [[forward]]", "تخطَّ الشارة  [[back]]"),
            _ => PL("Skip intro  [[forward]]", "تخطَّ المقدمة  [[back]]"),
        };

    static string SkipNoticeText(SegmentKind kind) =>
        kind switch
        {
            SegmentKind.Recap => PL("Skipping recap…", "جارٍ تخطي الملخص…"),
            SegmentKind.Credits => PL("Skipping credits…", "جارٍ تخطي الشارة…"),
            _ => PL("Skipping intro…", "جارٍ تخطي المقدمة…"),
        };

    void NoticeElapsed(object? sender, EventArgs e)
    {
        if (player?.IsPlaying != true)
        {
            SetAutomationPaused(true);
            return;
        }
        noticeTimer?.Stop();
        var target = noticeTarget;
        var kind = noticeKind;
        noticeKind = SegmentKind.None;
        SkipIntro.Visibility = Visibility.Collapsed;
        if (current == null || kind == SegmentKind.None)
            return;
        if (kind == SegmentKind.Intro)
            introHandled = true;
        SkipTo(kind, target);
    }

    void SkipTo(SegmentKind kind, double target)
    {
        if (kind == SegmentKind.Credits)
            SeekAbsolute(Math.Max(0, currentDuration - .3), false, false);
        else
            SeekAbsolute(target, false, false);
    }

    void CancelSkipNotice()
    {
        noticeTimer?.Stop();
        pausedNotice = null;
        noticeKind = SegmentKind.None;
    }

    void SetAutomationPaused(bool paused)
    {
        var now = DateTime.UtcNow;
        if (paused)
        {
            if (noticeTimer?.IsEnabled == true)
            {
                pausedNotice = noticeEnds > now ? noticeEnds - now : TimeSpan.Zero;
                noticeTimer.Stop();
            }
            if (nextTimer?.IsEnabled == true)
            {
                pausedCountdown = countdownEnds > now ? countdownEnds - now : TimeSpan.Zero;
                nextTimer.Stop();
            }
            return;
        }
        if (pausedNotice is { } notice)
        {
            pausedNotice = null;
            noticeEnds = now + notice;
            if (noticeKind != SegmentKind.None && noticeTimer != null)
            {
                noticeTimer.Interval = notice > TimeSpan.Zero ? notice : TimeSpan.FromMilliseconds(1);
                noticeTimer.Start();
            }
        }
        if (pausedCountdown is { } countdown)
        {
            pausedCountdown = null;
            countdownEnds = now + countdown;
            nextTimer?.Start();
        }
    }

    void SkipPillClicked()
    {
        if (noticeKind != SegmentKind.None)
        {
            CancelSkipNotice();
            ShowPlayerHint(PL("Skip cancelled", "أُلغي التخطي"));
            return;
        }
        if (current == null)
            return;
        if (activeSegment == SegmentKind.None)
        {
            SkipKnownIntro();
            return;
        }
        var end =
            activeSegment == SegmentKind.Intro ? store.GetIntro(current)?.End ?? 0
            : activeSegment == SegmentKind.Recap ? recapSegment?.End ?? 0
            : currentDuration;
        handledSegments.Add(activeSegment);
        if (activeSegment == SegmentKind.Intro)
            introHandled = true;
        SkipIntro.Visibility = Visibility.Collapsed;
        if (end > 0)
            SkipTo(activeSegment, end);
    }

    void ShowUpNext(VideoItem next, string message)
    {
        NextLabel.Text = PL("UP NEXT", "التالي");
        NextPlay.Content = PL("Play now", "شغّل الآن");
        NextCancel.Content = PL("Cancel", "إلغاء");
        endAction = null;
        var hide = store.State.HideSpoilers && store.GetProgress(next) is not { Completed: true };
        NextTitle.Text = next.EpisodeLabel + (next.EpisodeTitle.Length > 0 && !hide ? " · " + next.EpisodeTitle : "");
        NextCountdown.Text = message;
        NextRingHost.Visibility = Visibility.Collapsed;
        SetRing(0); // U77: Play now is the action; the thumbnail is clickable
        SetNextStill(next);
    }

    // The still loads off the UI thread into a reserved box, so the card never changes size.
    async void SetNextStill(VideoItem item)
    {
        NextMedia.Visibility = Visibility.Visible;
        NextStill.Background = Brush("#1E1E21");
        var request = ++stillRequest;
        var spoiler = store.State.HideSpoilers && store.GetProgress(item) is not { Completed: true };
        var path = new[] { spoiler ? "" : item.EpisodeImage, item.Backdrop, item.Poster }.FirstOrDefault(p =>
            !string.IsNullOrEmpty(p) && File.Exists(p)
        );
        if (path == null)
            return;
        var image = await Task.Run(() =>
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 704;
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();
                bitmap.Freeze();
                return (BitmapSource?)bitmap;
            }
            catch (Exception)
            {
                return null;
            }
        });
        if (request != stillRequest || image == null)
            return;
        NextStill.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        Motion.FadeIn(NextStill);
    }

    void StartCountdown()
    {
        pausedCountdown = null;
        countdownEnds = DateTime.UtcNow.AddSeconds(CountdownSeconds);
        nextTimer ??= new DispatcherTimer();
        nextTimer.Interval = TimeSpan.FromMilliseconds(100);
        nextTimer.Tick -= CountdownTick;
        nextTimer.Tick += CountdownTick;
        nextTimer.Start();
        CountdownTick(null, EventArgs.Empty);
    }

    void CountdownTick(object? sender, EventArgs e)
    {
        if (!ended && player?.IsPlaying != true)
        {
            SetAutomationPaused(true);
            return;
        }
        var left = Math.Max(0, (countdownEnds - DateTime.UtcNow).TotalSeconds);
        SetRing(1 - left / CountdownSeconds);
        var seconds = (int)Math.Ceiling(left);
        NextCountdown.Text =
            streamNext != null
                ? PL($"Streaming in {seconds} seconds", $"يبدأ البث خلال {seconds} ث")
                : PL($"Playing in {seconds} seconds", $"تبدأ خلال {seconds} ث");
        if (left <= 0)
        {
            nextTimer?.Stop();
            AutoAdvance();
        }
    }

    void SetRing(double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        const double c = 34,
            r = 32;
        if (fraction <= .001)
        {
            NextRing.Data = null;
            return;
        }
        if (fraction >= .999)
        {
            NextRing.Data = new EllipseGeometry(new Point(c, c), r, r);
            return;
        }
        var angle = fraction * Math.PI * 2;
        var figure = new PathFigure { StartPoint = new(c, c - r) };
        figure.Segments.Add(
            new ArcSegment(
                new Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle)),
                new Size(r, r),
                0,
                fraction > .5,
                SweepDirection.Clockwise,
                true
            )
        );
        NextRing.Data = new PathGeometry([figure]);
    }

    void AutoAdvance()
    {
        var stream = streamNext;
        var item = nextItem;
        if (stream == null && item == null)
            return;
        if (stillWatching.ShouldAsk(Prefs.StillWatchingAfter))
        {
            AskStillWatching(item, stream);
            return;
        }
        stillWatching.AutoAdvanced();
        streamNext = null;
        if (stream != null)
        {
            StreamItem(stream);
            return;
        }
        _ = PlayVideo(item!);
    }

    void AskStillWatching(VideoItem? item, VideoItem? stream)
    {
        if (player?.IsPlaying == true)
            player.SetPause(true);
        SavePlayback();
        askedNext = item;
        askedStream = stream;
        streamNext = null;
        NextPanel.Visibility = Visibility.Collapsed;
        StillWatchingTitle.Text = PL(
            "Still watching " + (current?.Title ?? "") + "?",
            "هل ما زلت تشاهد " + (current?.Title ?? "") + "؟"
        );
        StillWatchingBody.Text = PL(
            "You've watched " + stillWatching.Count + " episodes in a row. The next one waits for you.",
            "شاهدت " + stillWatching.Count + " حلقات متتالية. الحلقة التالية بانتظارك."
        );
        StillWatchingContinue.Content = PL("Continue watching", "متابعة المشاهدة");
        StillWatchingBack.Content = PL("Back to Library", "العودة إلى المكتبة"); // U82
        StillWatchingPanel.Visibility = Visibility.Visible;
        ShowControls();
        StillWatchingContinue.Focus();
    }

    void StillWatchingContinue_Click(object sender, RoutedEventArgs e)
    {
        stillWatching.UserInput();
        StillWatchingPanel.Visibility = Visibility.Collapsed;
        var stream = askedStream;
        var item = askedNext;
        askedStream = null;
        askedNext = null;
        if (stream != null)
        {
            StreamItem(stream);
            return;
        }
        if (item != null)
        {
            _ = PlayVideo(item);
            return;
        }
        if (player is { IsPlaying: false } && !ended)
            player.SetPause(false);
    }

    void ApplySpeed()
    {
        if (player != null)
            player.SetRate((float)sessionSpeed);
    }

    void SetSpeed(double speed)
    {
        sessionSpeed = PlaybackPrefs.ClampSpeed(speed);
        ApplySpeed();
        UpdateSpeedButton();
        ShowPlayerHint(PL("Speed", "السرعة") + " · " + PlaybackPrefs.SpeedLabel(sessionSpeed));
    }

    void StepSpeed(int direction)
    {
        var index = Array.IndexOf(PlaybackPrefs.Speeds, PlaybackPrefs.ClampSpeed(sessionSpeed));
        SetSpeed(
            PlaybackPrefs.Speeds[Math.Clamp((index < 0 ? 2 : index) + direction, 0, PlaybackPrefs.Speeds.Length - 1)]
        );
    }

    void UpdateSpeedButton()
    {
        SpeedButton.Content = PlaybackPrefs.SpeedLabel(sessionSpeed);
        SpeedButton.Foreground = Brush(Math.Abs(sessionSpeed - 1) < .01 ? "#F4F1EB" : "#E4C084");
    }

    void Speed_Click(object sender, RoutedEventArgs e)
    {
        stillWatching.UserInput();
        var menu = new ContextMenu();
        foreach (var speed in PlaybackPrefs.Speeds)
        {
            var value = speed;
            var row = new MenuItem
            {
                Header = PlaybackPrefs.SpeedLabel(value) + (value == 1 ? "  · " + PL("Normal", "عادية") : ""),
                IsCheckable = true,
                IsChecked = Math.Abs(value - sessionSpeed) < .01,
            };
            row.Click += (_, _) => SetSpeed(value);
            menu.Items.Add(row);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(
            new MenuItem
            {
                Header = PL("Shift+. / Shift+, change speed", "Shift+. / Shift+, لتغيير السرعة"),
                IsEnabled = false,
            }
        );
        OpenTrackMenu(menu, (Button)sender);
    }

    void ApplyVolume()
    {
        if (player != null)
            player.Volume = PlaybackPrefs.NativeVolume(VolumeBar.Value, Prefs.VolumeBoost);
        UpdateBoostButton();
    }

    void UpdateBoostButton()
    {
        BoostButton.Content = Prefs.VolumeBoost ? PL("Boost on", "التعزيز يعمل") : PL("Boost", "تعزيز");
        BoostButton.Foreground = Brush(Prefs.VolumeBoost ? "#E4C084" : "#B8B2AA");
        VolumeIcon.Stroke = Brush(Prefs.VolumeBoost ? "#E4C084" : "#F4F1EB");
    } // U79: the bar has no Boost word; a gold speaker shows boost is on

    // U80: at narrow widths only the inline slider is dropped; the speaker mutes on click and shows the slider on hover.
    bool narrowVolume;

    void ApplyVolumeWidth(bool narrow)
    {
        narrowVolume = narrow;
        VolumeBar.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
    }

    void PlayerVolume_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (narrowVolume)
            VolumeBar.Visibility = Visibility.Visible;
    }

    void PlayerVolume_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (narrowVolume)
            VolumeBar.Visibility = Visibility.Collapsed;
    }

    void Mute_Click(object sender, RoutedEventArgs e)
    {
        stillWatching.UserInput();
        ToggleMute();
        ShowControls();
    }

    void ToggleMute()
    {
        muted = !muted;
        if (player != null)
            player.Mute = muted;
        if (VolumeIcon.Parent is Button mute)
            System.Windows.Automation.AutomationProperties.SetName(
                mute,
                muted ? PL("Unmute", "إلغاء الكتم") : PL("Mute", "كتم")
            );
        VolumeIcon.Opacity = muted ? .45 : 1;
    }

    void Boost_Click(object sender, RoutedEventArgs e)
    {
        stillWatching.UserInput();
        Prefs.VolumeBoost = !Prefs.VolumeBoost;
        SavePrefs();
        ApplyVolume();
        ShowControls();
        ShowPlayerHint(
            Prefs.VolumeBoost
                ? PL("Volume boost on · up to 200%", "تعزيز الصوت يعمل · حتى 200٪")
                : PL("Volume boost off", "تعزيز الصوت متوقف")
        );
    }
}

public partial class MainWindow
{
    PlaybackPrefs? playbackPrefs;
    PlaybackPrefs Prefs => playbackPrefs ??= PlaybackPrefs.Load(store.DataDirectory, store.State.AutoSkipIntro);

    static string PL(string english, string arabic) => Loc.Arabic ? arabic : english;

    void SavePrefs()
    {
        Prefs.Save(store.DataDirectory);
        if (store.State.AutoSkipIntro != (Prefs.SkipIntro == SkipMode.Auto))
        {
            store.State.AutoSkipIntro = Prefs.SkipIntro == SkipMode.Auto;
            store.Save();
        }
    }

    // Settings → Playback: skip choices, still-watching check, default speed and volume boost.
    public FrameworkElement PlaybackPrefsPanel()
    {
        var panel = new StackPanel { Margin = new(0, 6, 0, 6) };
        string[] modes = [PL("Show button", "زر التخطي"), PL("Skip automatically", "تخطٍّ تلقائي"), PL("Off", "إيقاف")];
        void SkipRow(string title, Func<SkipMode> get, Action<SkipMode> set)
        {
            panel.Children.Add(Text(title, 12, "#C7C2BC"));
            panel.Children.Add(Chips(modes, modes[(int)get()], label =>
                {
                    set((SkipMode)Array.IndexOf(modes, label));
                    SavePrefs();
                    Render();
                }));
        }
        SkipRow(PL("Intros", "المقدمات"), () => Prefs.SkipIntro, v => Prefs.SkipIntro = v);
        SkipRow(
            PL("Recaps (“Previously on…”)", "الملخصات («في الحلقات السابقة»)"),
            () => Prefs.SkipRecap,
            v => Prefs.SkipRecap = v
        );
        SkipRow(PL("End credits", "شارة النهاية"), () => Prefs.SkipCredits, v => Prefs.SkipCredits = v);
        panel.Children.Add(
            Body(
                PL(
                    "Uses saved markers or labelled chapters. Automatic skips show a short notice with Undo. Skipping credits goes to Up Next.",
                    "تعتمد على العلامات المحفوظة أو أسماء الفصول. يظهر إشعار قصير مع زر تراجع عند التخطي التلقائي. تخطي الشارة ينتقل إلى الحلقة التالية."
                ),
                12
            )
        );
        panel.Children.Add(Text(PL("Ask “Still watching?”", "اسأل «هل ما زلت تشاهد؟»"), 12, "#C7C2BC"));
        string[] counts =
        [
            PL("Never", "أبداً"),
            PL("After 2 episodes", "بعد حلقتين"),
            PL("After 3 episodes", "بعد 3 حلقات"),
            PL("After 5 episodes", "بعد 5 حلقات"),
        ];
        panel.Children.Add(
            Chips(
                counts,
                counts[Math.Max(0, Array.IndexOf(PlaybackPrefs.StillWatchingChoices, Prefs.StillWatchingAfter))],
                label =>
                {
                    Prefs.StillWatchingAfter = PlaybackPrefs.StillWatchingChoices[Array.IndexOf(counts, label)];
                    SavePrefs();
                    Render();
                }
            )
        );
        panel.Children.Add(
            Body(
                PL(
                    "Counts episodes that start on their own with no key, click or mouse movement.",
                    "تُحسب الحلقات التي تبدأ تلقائياً دون أي ضغطة أو حركة للفأرة."
                ),
                12
            )
        );
        panel.Children.Add(Text(PL("Default speed", "السرعة الافتراضية"), 12, "#C7C2BC"));
        var speeds = PlaybackPrefs.Speeds.Select(PlaybackPrefs.SpeedLabel).ToArray();
        panel.Children.Add(Chips(speeds, PlaybackPrefs.SpeedLabel(Prefs.DefaultSpeed), label =>
            {
                Prefs.DefaultSpeed = PlaybackPrefs.Speeds[Array.IndexOf(speeds, label)];
                SavePrefs();
                Render();
            }));
        var boost = new CheckBox
        {
            Content = PL("Volume boost (up to 200%) for quiet videos", "تعزيز الصوت (حتى 200٪) للفيديوهات الهادئة"),
            IsChecked = Prefs.VolumeBoost,
            Margin = new(0, 10, 0, 0),
        };
        boost.Click += (_, _) =>
        {
            Prefs.VolumeBoost = boost.IsChecked == true;
            SavePrefs();
            ApplyVolume();
        };
        panel.Children.Add(boost);
        return panel;
    }
}
