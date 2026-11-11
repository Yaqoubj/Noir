using System.IO;
using System.Windows;
using System.Windows.Controls;
using LibVLCSharp.Shared;

namespace Noir;

public partial class MainWindow
{
    CancellationTokenSource? streamSubtitleCancellation;
    int streamSubtitleAttempt = -1;

    async void EnsureStreamSubtitles()
    {
        if (currentStream == null || current == null || streamSubtitleAttempt == serial)
            return;
        streamSubtitleAttempt = serial;
        var session = serial;
        var item = current;
        var stream = currentStream;
        streamSubtitleCancellation?.Cancel();
        streamSubtitleCancellation = new CancellationTokenSource();
        var ct = streamSubtitleCancellation.Token;
        try
        {
            // Let embedded tracks arrive before looking for an external subtitle.
            await Task.Delay(2500, ct);
            if (session != serial || closing || player == null)
                return;
            var preferred = Preference(item).SubtitleTrack;
            var language = SubtitleDownloads.Language(preferred.Length == 0 ? store.State.Subtitles : preferred);
            if (
                language.Length == 0
                || attachedStreamSubtitles.Any(p => SubtitleDownloads.SidecarLanguage(p) == language)
            )
                return;
            if (
                player.SpuDescription.Any(t =>
                    t.Id >= 0
                    && System.Text.RegularExpressions.Regex.IsMatch(
                        t.Name,
                        language == "ar" ? @"(?i)\b(arabic|ara|ar)\b" : @"(?i)\b(english|eng|en)\b"
                    )
                )
            )
                return;
            SavedLabel.Text =
                language == "ar"
                    ? PL("Finding Arabic subtitles…", "جارٍ البحث عن الترجمة العربية…‏")
                    : PL("Finding English subtitles…", "جارٍ البحث عن الترجمة الإنجليزية…‏");
            var engine = new SubtitleDownloads(store.DataDirectory, source: () =>
                SubtitleSelection.From(downloads?.Config ?? new DownloadCredentials())
            );
            var target = SubtitleDownloads.StreamTarget(store.DataDirectory, item, stream.Name);
            var result = await engine.Fetch(item, stream.Name, [language], false, ct, streamTarget: target);
            if (session != serial || closing)
                return;
            // Respect a manual Off/track change made while the request was running.
            if (Preference(item).SubtitleTrack == preferred)
            {
                AttachStreamSubtitles();
                ApplyPreferredTracks();
            }
            SavedLabel.Text = SubtitleResultText(result);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            store.Log(e);
            if (session == serial && !closing)
                SavedLabel.Text = PL(
                    "Subtitles unavailable · retry in Subtitles",
                    "الترجمة غير متاحة · أعد المحاولة من قائمة الترجمة"
                );
        }
    }

    void DownloadSubtitleFiles(VideoItem item, bool syncOnly = false)
    {
        var stream = current?.Id == item.Id ? currentStream : null;
        var playbackSerial = serial;
        if (stream != null)
            streamSubtitleCancellation?.Cancel();
        var streamTarget =
            stream == null ? null : SubtitleDownloads.StreamTarget(store.DataDirectory, item, stream.Name);
        if (syncOnly && stream != null && streamTarget != null)
        {
            SyncStreamSubtitles(item, stream, streamTarget);
            return;
        }
        var window = OpenSheet(PL("Subtitles", "الترجمة"), item.Label, SheetKind.Side, 500);
        var body = new StackPanel();
        window.Add(body);
        var english = new CheckBox
        {
            Content = PL("English", "الإنجليزية"),
            IsChecked = true,
            Margin = new(0, 4, 0, 6),
        };
        var arabic = new CheckBox
        {
            Content = PL("Arabic", "العربية"),
            IsChecked = true,
            Margin = new(0, 4, 0, 6),
        };
        var auto = new CheckBox
        {
            Content = PL(
                "Automatically adjust timing when the match is plausible",
                "اضبط التوقيت تلقائيًا عندما تكون المطابقة مرجّحة"
            ),
            IsChecked = true,
            Margin = new(0, 4, 0, 6),
        };
        body.Children.Add(
            SheetSection(
                syncOnly ? PL("Check subtitle timing", "فحص توقيت الترجمة") : PL("Download subtitles", "تنزيل الترجمة")
            )
        );
        if (!syncOnly)
        {
            body.Children.Add(english);
            body.Children.Add(arabic);
            if (stream == null)
                body.Children.Add(auto);
        }
        body.Children.Add(
            Body(
                stream != null
                    ? PL(
                        "Download subtitles while you stream. They are saved on this PC for this release and load again next time. Use Subtitles → Timing if the text appears early or late.",
                        "نزّل الترجمة أثناء البث. تُحفظ على هذا الكمبيوتر لهذه النسخة وتُحمَّل مجددًا في المرة القادمة. استخدم الترجمة ← التوقيت إذا ظهر النص مبكرًا أو متأخرًا."
                    )
                    : PL(
                        "Subtitles stay beside your video and work offline on PC and phone. Timing analysis uses the local video and keeps the original subtitle backup. Subtitles built into the video are checked too; a corrected copy is saved beside it only when they are off.",
                        "تبقى الترجمة بجانب الفيديو وتعمل دون اتصال على الكمبيوتر والهاتف. يستخدم تحليل التوقيت الفيديو المحفوظ ويحتفظ بنسخة احتياطية من الترجمة الأصلية. تُفحص الترجمة المضمّنة في الفيديو أيضًا، ولا تُحفظ نسخة مصحّحة بجانبه إلا إذا كان توقيتها غير مضبوط."
                    ),
                12
            )
        );
        var feedback = Body("", 13);
        body.Children.Add(feedback);
        var cancellation = new CancellationTokenSource();
        window.Closed += _ =>
        {
            cancellation.Cancel();
        };
        window.Action(PL("Close", "إغلاق"), () => window.Close(false));
        Button? start = null;
        start = window.Action(
            syncOnly ? PL("Check subtitle timing", "فحص توقيت الترجمة") : PL("Find and download", "ابحث ونزّل"),
            async () =>
            {
                if (!syncOnly && english.IsChecked != true && arabic.IsChecked != true)
                {
                    feedback.Text = PL("Choose at least one language.", "اختر لغة واحدة على الأقل.");
                    return;
                }
                start!.IsEnabled = false;
                feedback.Text = syncOnly
                    ? PL("Analyzing subtitle timing in the background…", "جارٍ تحليل توقيت الترجمة في الخلفية…")
                    : PL(
                        "Matching subtitles for this episode / movie…",
                        "جارٍ مطابقة الترجمة مع هذه الحلقة / هذا الفيلم…"
                    );
                try
                {
                    var engine = new SubtitleDownloads(store.DataDirectory, source: () =>
                        SubtitleSelection.From(downloads?.Config ?? new DownloadCredentials())
                    );
                    string message;
                    var corrected = new List<string>();
                    if (syncOnly)
                    {
                        var saved = MobileSubtitles.Find(item);
                        var results = new List<string>();
                        foreach (var sub in saved)
                        {
                            var outcome = await engine.Sync(item.Path, sub.Path, cancellation.Token);
                            if (outcome.StartsWith("auto-synced", StringComparison.Ordinal))
                                corrected.Add(sub.Path);
                            results.Add(sub.Info.Name + " · " + outcome);
                        }
                        // Built-in tracks get a checked copy unless one was already made for that language.
                        foreach (var lang in await EmbeddedSubtitles.Probe(item.Path, cancellation.Token))
                            if (
                                !saved.Any(s =>
                                    SubtitleDownloads.IsSyncedCopy(Path.GetFileNameWithoutExtension(s.Path))
                                    && SubtitleDownloads.SidecarLanguage(s.Path) == lang
                                )
                            )
                            {
                                var outcome = await engine.SyncEmbedded(item.Path, lang, cancellation.Token);
                                if (outcome.StartsWith("auto-synced", StringComparison.Ordinal))
                                    corrected.AddRange(
                                        MobileSubtitles
                                            .Find(item)
                                            .Select(s => s.Path)
                                            .Where(p =>
                                                SubtitleDownloads.IsSyncedCopy(Path.GetFileNameWithoutExtension(p))
                                                && SubtitleDownloads.SidecarLanguage(p) == lang
                                            )
                                    );
                                results.Add(
                                    (
                                        lang == "ar"
                                            ? PL(
                                                "Arabic (built into the video)",
                                                "الترجمة العربية (المضمّنة في الفيديو)"
                                            )
                                            : PL(
                                                "English (built into the video)",
                                                "الترجمة الإنجليزية (المضمّنة في الفيديو)"
                                            )
                                    )
                                        + " · "
                                        + outcome
                                );
                            }
                        message =
                            results.Count > 0
                                ? string.Join("\n", results)
                                : PL(
                                    "No English or Arabic subtitles found in or beside this video. Download subtitles first.",
                                    "لم يُعثر على ترجمة إنجليزية أو عربية داخل هذا الفيديو أو بجانبه. نزّل الترجمة أولًا."
                                );
                    }
                    else
                    {
                        if (item.ImdbId.Length == 0)
                        {
                            using var metadata = new MetadataClient(store.DataDirectory);
                            var id = await metadata.DownloadIdentity(item, cancellation.Token);
                            if (id.Length == 0)
                                throw new IOException("Correct this title's match before finding subtitles.");
                            store.SetDownloadIdentity(item.GroupKey, id);
                            item.ImdbId = id;
                        }
                        var release = stream?.Name
                            ?? downloads
                                ?.Jobs()
                                .Where(j => j.Target.Id == item.Id)
                                .OrderByDescending(j => j.CreatedAt)
                                .FirstOrDefault()
                                ?.RemoteName
                            ?? Path.GetFileName(item.Path);
                        message = await engine.Fetch(
                            item,
                            release,
                            new[] { english.IsChecked == true ? "en" : "", arabic.IsChecked == true ? "ar" : "" }
                                .Where(l => l.Length > 0)
                                .ToList(),
                            auto.IsChecked == true,
                            cancellation.Token,
                            streamTarget: streamTarget
                        );
                    }
                    if (cancellation.IsCancellationRequested)
                        return;
                    feedback.Text = SubtitleResultText(message);
                    // The result above is already true; trouble reloading into the player must not replace it with an error.
                    try
                    {
                        if (current?.Id == item.Id && serial == playbackSerial && player != null)
                        {
                            if (stream != null)
                                AttachStreamSubtitles();
                            else if (corrected.Count > 0)
                                ReloadCorrectedSubtitles(corrected);
                            else
                            {
                                foreach (var sub in MobileSubtitles.Find(item))
                                    player.AddSlave(MediaSlaveType.Subtitle, new Uri(sub.Path).AbsoluteUri, false);
                                ApplyPreferredTracks();
                            }
                        }
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        store.Log(e);
                    }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                catch (Exception e)
                {
                    store.Log(e);
                    feedback.Text =
                        e is IOException ? SubtitleResultText(e.Message)
                        : syncOnly
                            ? PL(
                                "Couldn't check the subtitle timing. Your subtitles were not changed.",
                                "تعذر فحص توقيت الترجمة. لم تتغير ترجمتك."
                            )
                        : PL(
                            "Couldn't get subtitles. Playback is unaffected; retry when connected.",
                            "تعذر جلب الترجمة. لم يتأثر التشغيل؛ أعد المحاولة عند الاتصال."
                        );
                }
                finally
                {
                    if (!cancellation.IsCancellationRequested)
                        start.IsEnabled = true;
                }
            },
            true
        );
    }

    // Auto-sync on a stream: a few short audio samples through a loopback proxy, with progress and Cancel.
    // Cancelled by Cancel, closing the sheet, Stop, another stream or episode, or leaving the app; nothing is written afterwards.
    void SyncStreamSubtitles(VideoItem item, StreamOption stream, VideoItem target)
    {
        var session = serial;
        var duration = currentDuration;
        var audio = StreamAudioIndex();
        var config = downloads?.Config;
        var subs = MobileSubtitles
            .Find(target, requireVideo: false)
            .Where(s => SubtitleDownloads.SidecarLanguage(s.Path) is "en" or "ar")
            .Select(s => s.Path)
            .ToList();
        var window = OpenSheet(
            PL("Auto-sync to the audio", "مزامنة تلقائية مع الصوت"),
            item.Label,
            SheetKind.Side,
            500
        );
        var body = new StackPanel();
        window.Add(body);
        body.Children.Add(SheetSection(PL("Check subtitle timing", "فحص توقيت الترجمة")));
        body.Children.Add(
            Body(
                PL(
                    "Noir listens to a few short parts of this stream, a small share of the whole, and shifts your saved subtitles to match its speech. Nothing changes unless the match is clear, and the original subtitles are backed up.",
                    "يستمع Noir إلى أجزاء قصيرة من هذا البث، وهي نسبة صغيرة من مجموعه، ويضبط توقيت ترجمتك المحفوظة ليطابق كلامه. لا يتغير شيء ما لم تكن المطابقة واضحة، وتُحفظ نسخة احتياطية من الترجمة الأصلية."
                ),
                12
            )
        );
        var feedback = Body(
            subs.Count == 0
                ? PL(
                    "No English or Arabic subtitles are saved for this stream yet. Download subtitles first.",
                    "لا توجد ترجمة إنجليزية أو عربية محفوظة لهذا البث بعد. نزّل الترجمة أولًا."
                )
                : "",
            13
        );
        body.Children.Add(feedback);
        var cancellation = new CancellationTokenSource();
        var watch = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        watch.Tick += (_, _) =>
        {
            if (serial != session || closing || current?.Id != item.Id)
                cancellation.Cancel();
        };
        watch.Start();
        EventHandler quit = (_, _) => cancellation.Cancel();
        Dispatcher.ShutdownStarted += quit;
        window.Closed += _ =>
        {
            cancellation.Cancel();
            watch.Stop();
            Dispatcher.ShutdownStarted -= quit;
        };
        var cancel = window.Action(PL("Cancel", "إلغاء"), () => window.Close(false));
        if (subs.Count == 0)
            return;
        Button? start = null;
        start = window.Action(
            PL("Start", "ابدأ"),
            async () =>
            {
                start!.IsEnabled = false;
                feedback.Text = PL("Getting ready…", "جارٍ التحضير…");
                var engine = new SubtitleDownloads(store.DataDirectory);
                var local =
                    Uri.TryCreate(stream.Url, UriKind.Absolute, out var uri)
                    && (uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));
                Func<CancellationToken, Task<List<StreamOption>>>? refresh =
                    config == null || StreamSources.Manifest(config).Length == 0
                        ? null
                        : async token =>
                        {
                            using var http = DownloadSecurity.Client(TimeSpan.FromSeconds(40));
                            return await StreamSources.Find(config, item, http, token);
                        };
                void Progress(int i, int n) =>
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (!cancellation.IsCancellationRequested)
                            feedback.Text = Loc.Arabic
                                ? $"الاستماع إلى العينة {i} من {n}"
                                : $"Listening to sample {i} of {n}";
                    });
                try
                {
                    var result = await Task.Run(() =>
                        engine.SyncStream(
                            stream,
                            subs,
                            duration,
                            audio,
                            refresh,
                            Progress,
                            cancellation.Token,
                            new StreamSyncSettings { TrustLocal = local }
                        )
                    );
                    if (cancellation.IsCancellationRequested)
                        return;
                    feedback.Text = SubtitleResultText(result.Message);
                    // The result above is already true; trouble reloading into the player must not replace it with an error.
                    try
                    {
                        if (result.Corrected.Count > 0 && current?.Id == item.Id && serial == session && player != null)
                            ReloadCorrectedSubtitles(result.Corrected, SelectedSubtitleLanguage());
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        store.Log(e);
                    }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                catch (Exception e)
                {
                    store.Log(e);
                    if (!cancellation.IsCancellationRequested)
                        feedback.Text = PL(
                            "Couldn't check the subtitle timing. Your subtitles were not changed.",
                            "تعذر فحص توقيت الترجمة. لم تتغير ترجمتك."
                        );
                }
                finally
                {
                    if (!cancellation.IsCancellationRequested)
                    {
                        cancel.Content = PL("Close", "إغلاق");
                        System.Windows.Automation.AutomationProperties.SetName(cancel, PL("Close", "إغلاق"));
                        watch.Stop();
                    }
                }
            },
            true
        );
    }

    int StreamAudioIndex()
    {
        if (player == null)
            return 0;
        var tracks = player.AudioTrackDescription.Where(t => t.Id >= 0).ToList();
        var at = tracks.FindIndex(t => t.Id == player.AudioTrack);
        return at < 0 ? 0 : at;
    }

    // The language of the subtitle track in use ("" when Off or something else), so a reload keeps the viewer's choice.
    string SelectedSubtitleLanguage()
    {
        if (player == null || player.Spu < 0)
            return "";
        var name = player.SpuDescription.FirstOrDefault(t => t.Id == player.Spu).Name ?? "";
        return System.Text.RegularExpressions.Regex.IsMatch(name, @"(?i)\b(arabic|ara|ar)\b") ? "ar"
            : System.Text.RegularExpressions.Regex.IsMatch(name, @"(?i)\b(english|eng|en)\b") ? "en"
            : "";
    }

    // VLC keeps a loaded subtitle in memory and picks tracks by name, so a corrected file at the same path never shows.
    // Load a copy from a fresh folder (same file name, so the track label stays) and select it directly.
    // select: null picks the interface language's copy; "" selects none (subtitles Off or another track stay as they are); "en"/"ar" selects that language's copy.
    void ReloadCorrectedSubtitles(List<string> corrected, string? select = null)
    {
        if (player == null)
            return;
        var root = Path.Combine(store.DataDirectory, "subtitle-reload");
        try
        {
            if (Directory.Exists(root))
                foreach (var old in Directory.EnumerateDirectories(root))
                    if (Directory.GetCreationTimeUtc(old) < DateTime.UtcNow.AddDays(-1))
                        try
                        {
                            Directory.Delete(old, true);
                        }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
        }
        catch (IOException) { }
        var preferred =
            select == null
                ? corrected.FirstOrDefault(p => SubtitleDownloads.SidecarLanguage(p) == (Loc.Arabic ? "ar" : "en"))
                    ?? corrected[0]
            : select.Length == 0 ? null
            : corrected.FirstOrDefault(p => SubtitleDownloads.SidecarLanguage(p) == select);
        foreach (var path in corrected.OrderBy(p => p == preferred))
            try
            {
                var folder = Path.Combine(root, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                var copy = Path.Combine(folder, Path.GetFileName(path));
                File.Copy(path, copy);
                player.AddSlave(
                    MediaSlaveType.Subtitle,
                    new Uri(copy).AbsoluteUri,
                    preferred != null && path == preferred
                );
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // The subtitle engine reports in English ("Arabic saved · auto-synced (+1.20s)"); Arabic mode translates each " · " part.
    static string SubtitleResultText(string message)
    {
        if (!Loc.Arabic)
            return message;
        static string Part(string part)
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                part,
                @"^(English|Arabic) (already saved|built into the video|lookup unavailable|not available|saved|download unavailable)$"
            );
            if (m.Success)
            {
                var lang = m.Groups[1].Value == "Arabic" ? "العربية" : "الإنجليزية";
                return m.Groups[2].Value switch
                {
                    "already saved" => $"الترجمة {lang} محفوظة مسبقًا",
                    "built into the video" => $"الترجمة {lang} مضمّنة في الفيديو",
                    "lookup unavailable" => $"تعذر البحث عن الترجمة {lang}",
                    "not available" => $"الترجمة {lang} غير متاحة",
                    "saved" => $"حُفظت الترجمة {lang}",
                    _ => $"تعذر تنزيل الترجمة {lang}",
                };
            }
            // LRM keeps "+1.20" in one piece inside right-to-left text.
            m = System.Text.RegularExpressions.Regex.Match(part, @"^auto-synced( copy saved)? \(([+-]?[0-9.,]+)s\)$");
            if (m.Success)
                return (m.Groups[1].Success ? "حُفظت نسخة مُزامنة (" : "تمت المزامنة تلقائيًا (")
                    + "‎"
                    + m.Groups[2].Value
                    + " ث)";
            return part switch
            {
                "timing already matches" => "التوقيت مطابق للصوت",
                "too little speech to check the timing" => "كلام قليل جدًا لفحص التوقيت",
                "samples disagree" => "العينات غير متفقة",
                "Stream doesn't support seeking" => "البث لا يدعم التنقل",
                "data limit reached" => "بلغ حد استهلاك البيانات",
                "link expired" => "انتهت صلاحية الرابط",
                "couldn't read the stream" => "تعذرت قراءة البث",
                "couldn't find the length of this stream" => "تعذر معرفة مدة هذا البث",
                "stream too short to sample" => "البث أقصر من أن يؤخذ منه عينات",
                "Download subtitles first" => "نزّل الترجمة أولًا",
                "English" => "الترجمة الإنجليزية",
                "Arabic" => "الترجمة العربية",
                "automatic timing tool unavailable" => "أداة ضبط التوقيت التلقائي غير متاحة",
                "picture subtitles" => "ترجمة مصوّرة",
                "timing can't be checked" => "لا يمكن فحص توقيتها",
                "timing check unavailable" => "فحص التوقيت غير متاح",
                "timing matches the video" => "التوقيت مطابق للفيديو",
                "timing uncertain" => "التوقيت غير مؤكد",
                "built-in track kept" => "أُبقي المسار المضمّن",
                "original kept" => "أُبقي الأصل",
                "timing needs manual adjustment" => "التوقيت يحتاج إلى ضبط يدوي",
                "timing check timed out" => "انتهت مهلة فحص التوقيت",
                "timing unchanged" => "لم يتغير التوقيت",
                "adjust in Subtitles → Timing if needed" => "اضبطه من الترجمة ← التوقيت عند الحاجة",
                "release matched" => "مطابقة للنسخة",
                "Correct this title's match before finding subtitles." =>
                    "صحّح مطابقة هذا العنوان قبل البحث عن الترجمة.",
                "Open a stream or save the video before downloading subtitles." =>
                    "افتح بثًا أو احفظ الفيديو قبل تنزيل الترجمة.",
                "Save the video and subtitles first." => "احفظ الفيديو والترجمة أولًا.",
                _ => part,
            };
        }
        return string.Join(
            "\n",
            message.Split('\n').Select(line => string.Join(" · ", line.Split(" · ").Select(Part)))
        );
    }

    readonly HashSet<string> attachedStreamSubtitles = [];

    void AttachStreamSubtitles()
    {
        if (current == null || currentStream == null || player == null)
            return;
        var target = SubtitleDownloads.StreamTarget(store.DataDirectory, current, currentStream.Name);
        var preference = Preference(current).SubtitleTrack;
        var language = preference.Length == 0 ? store.State.Subtitles : preference;
        foreach (var sub in MobileSubtitles.Find(target, requireVideo: false))
            if (
                !attachedStreamSubtitles.Contains(sub.Path)
                && player.AddSlave(
                    MediaSlaveType.Subtitle,
                    new Uri(sub.Path).AbsoluteUri,
                    SubtitleDownloads.Language(language) == SubtitleDownloads.SidecarLanguage(sub.Path)
                )
            )
                attachedStreamSubtitles.Add(sub.Path);
    }
}
