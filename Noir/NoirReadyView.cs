using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Noir;
public partial class MainWindow
{
    static string ReadyText(string value)=>value switch{
        "Preparation paused"=>T(value,"التحضير متوقف مؤقتًا"),
        "Preparation budget reached"=>T(value,"تم بلوغ ميزانية التحضير"),
        "Keeping free space available"=>T(value,"الحفاظ على المساحة الحرة المطلوبة"),
        "Availability or size unconfirmed; recheck scheduled"=>T(value,"لم يتأكد التوفر أو الحجم؛ تمت جدولة إعادة التحقق"),
        "Waiting for an aired unwatched episode"=>T(value,"بانتظار حلقة عُرضت ولم تُشاهد"),
        "Playable offline"=>T(value,"جاهز للمشاهدة دون اتصال"),
        "Preparing on this PC"=>T(value,"جارٍ التحضير على هذا الكمبيوتر"),
        "Download paused"=>T(value,"التنزيل متوقف مؤقتًا"),
        "Connect a download source in Settings"=>T(value,"اربط مصدر تنزيل في الإعدادات"),
        "Source check failed; recheck scheduled"=>T(value,"تعذر فحص المصدر؛ تمت جدولة إعادة التحقق"),
        "Ready to download"=>T(value,"جاهز للتنزيل"),
        "Checking sources"=>T(value,"جارٍ فحص المصادر"),
        "Preparation check"=>T(value,"فحص التحضير"),
        "Queued confirmed ready file"=>T(value,"تمت جدولة ملف مؤكد الجاهزية"),
        "Migrated keep ready preferences"=>T(value,"تم نقل تفضيلات إبقاء الحلقات جاهزة"),
        "Watched file removal scheduled"=>T(value,"تمت جدولة إزالة ملف تمت مشاهدته"),
        "Removed watched managed file"=>T(value,"تمت إزالة ملف مُدار تمت مشاهدته"),
        "File changed; removal cancelled"=>T(value,"تغير الملف؛ أُلغيت الإزالة"),
        "Preparation could not finish"=>T(value,"تعذر إكمال التحضير"),
        "Preferences changed"=>T(value,"تغيرت التفضيلات"),
        "Pause preparation"=>T(value,"إيقاف التحضير مؤقتًا"),
        "Resume preparation"=>T(value,"استئناف التحضير"),
        "Keep file"=>T(value,"الاحتفاظ بالملف"),
        "Cancel removal"=>T(value,"إلغاء الإزالة"),
        "Pin file"=>T(value,"تثبيت الملف"),
        "Unpin file"=>T(value,"إلغاء تثبيت الملف"),
        "Startup"=>T(value,"بدء التشغيل"),
        "Episode finished"=>T(value,"انتهت الحلقة"),
        "New episode"=>T(value,"حلقة جديدة"),
        "Six-hour refresh"=>T(value,"تحديث كل ست ساعات"),
        "Retry"=>T(value,"إعادة المحاولة"),
        _=>T(value)
    };
    static string ReadySize(long bytes)=>bytes==0?"0 GB":DownloadNaming.Size(bytes);
    Border ReadyCard(UIElement child)=>new(){Background=Brush("#19191C"),BorderBrush=Brush("#343029"),BorderThickness=new(1),CornerRadius=new(14),Padding=new(20),Margin=new(0,8,0,12),Child=child};
    public string? ReadyTitleInfo(string group)
    {
        var title=downloads?.PreparedTitle(group);if(title==null)return null;
        return downloads!.ReadyViewState.Paused?ReadyText("Preparation paused"):ReadyText(title.Reason)+(title.Bytes>0?" · "+ReadySize(title.Bytes):"");
    }
    public FrameworkElement? ReadyShelf()
    {
        if(downloads==null)return null;var state=downloads.ReadyViewState;var titles=state.Titles.Values.Where(t=>t.Episodes.Count>0).Take(12).ToList();if(titles.Count==0)return null;
        var panel=new StackPanel{Margin=new(0,18,0,10)};panel.Children.Add(Heading(T("Ready","جاهز"),T("Your next watch is already here","مشاهدتك القادمة جاهزة هنا")));
        var rail=new StackPanel{Orientation=Orientation.Horizontal};panel.Children.Add(new ScrollViewer{Content=rail,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled});
        foreach(var title in titles){var column=new StackPanel{Width=250};column.Children.Add(Text(title.Title,17,"#F4F1EB",FontWeights.SemiBold));column.Children.Add(Body(state.Paused?ReadyText("Preparation paused"):ReadyText(title.Reason),12));
            var first=title.Episodes.First();column.Children.Add(Text(first.Season==0?T("Movie","فيلم"):$"S{first.Season:00} · E{first.Episode:00}",12,"#E4C084"));
            if(first.Local){var item=store.EpisodesFor(title.Group).FirstOrDefault(e=>e.Id==first.ItemId);if(item!=null)column.Children.Add(ActionButton(T("Play offline","شاهد دون اتصال"),()=>_=PlayVideo(item),true));}
            else column.Children.Add(ActionButton(T("Preparation centre","مركز التحضير"),ShowDownloads));
            var card=ReadyCard(column);card.Margin=new(0,0,14,6);rail.Children.Add(card);
        }Motion.EnterOnce(panel);return panel;
    }
    public async void OfferKeepReady(VideoItem finished)
    {
        if(downloads==null||finished.IsMovie)return;var manager=downloads;
        await manager.ChangeReady(s=>{});if(manager.ReadyOfferShown(finished.GroupKey)||NoirReadyPolicy.Enabled(manager.SeasonPreferences(finished.Id)))return;
        await manager.ChangeReady(s=>s.Offered.Add(finished.GroupKey));if(!closing)ShowKeepReady(finished);
    }
    public async Task SetAlsoDownload(VideoItem item,bool enabled)
    {
        if(downloads==null)return;var manager=downloads;await Task.Run(()=>{var preference=manager.SeasonPreferences(item.Id);preference.AlsoDownload=enabled;manager.SaveSeasonPreferences(item.Id,preference);});await manager.PrepareReady("Preferences changed");
    }
    public FrameworkElement AlsoDownloadToggle(VideoItem item)
    {
        var toggle=new CheckBox{Content=T("Also download automatically","تنزيل تلقائي أيضًا"),IsChecked=downloads?.SeasonPreferences(item.Id).AlsoDownload==true,Margin=new(0,10,0,10)};toggle.Click+=async(_,_)=>await SetAlsoDownload(item,toggle.IsChecked==true);return toggle;
    }
    public void ShowKeepReady(VideoItem item)
    {
        if(downloads==null)return;var manager=downloads;var preference=manager.SeasonPreferences(item.Id);
        var window=new Window{Owner=this,ShowActivated=!validationMode,FlowDirection=Loc.Arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight,Title=T("Keep ready","إبقاء الحلقات جاهزة"),Width=690,Height=780,MinWidth=540,MinHeight=560,Background=Brush("#101012"),Foreground=Brush("#F4F1EB"),WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var panel=new StackPanel{Margin=new(30),Background=Brush("#101012")};window.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Background=Brush("#101012")};
        panel.Children.Add(Text(T("NOIR READY · THIS PC","جاهز من نوار · هذا الكمبيوتر"),11,"#E4C084",FontWeights.SemiBold));
        panel.Children.Add(Heading(T("Your next watch is already here","مشاهدتك القادمة جاهزة هنا"),item.Title));
        panel.Children.Add(Body(T("Keep the next unwatched, aired episodes ready. Started shows prepare first.","أبقِ الحلقات القادمة المعروضة وغير المشاهدة جاهزة. الأولوية للمسلسلات التي بدأت مشاهدتها."),13));
        var counts=new[]{T("1 episode","حلقة واحدة"),T("2 episodes","حلقتان"),T("3 episodes","٣ حلقات"),T("Whole season","الموسم كاملًا")};int[] values=[1,2,3,-1];var count=new ComboBox{ItemsSource=counts,SelectedIndex=Math.Max(0,Array.IndexOf(values,preference.KeepReadyCount)),Margin=new(0,14,0,12),Padding=new(10)};panel.Children.Add(count);
        var rows=new StackPanel();panel.Children.Add(ReadyCard(rows));var impact=Body("",12);panel.Children.Add(impact);
        int revision=0;
        async Task Preview(){var rev=++revision;rows.Children.Clear();rows.Children.Add(Text(T("UP NEXT","الحلقات القادمة"),11,"#E4C084"));rows.Children.Add(Body(T("Checking sources…","جارٍ فحص المصادر…"),13));
            var selected=values[Math.Max(0,count.SelectedIndex)];var preview=await Task.Run(()=>{var inputs=store.ReadyPreview(item.GroupKey);return NoirReadyPolicy.Next(inputs,selected,DateTimeOffset.UtcNow);});if(rev!=revision||!window.IsVisible)return;
            rows.Children.Clear();rows.Children.Add(Text(T("UP NEXT","الحلقات القادمة"),11,"#E4C084"));foreach(var episode in preview){rows.Children.Add(Body($"S{episode.Season:00} E{episode.Episode:00} · "+(episode.Local?ReadyText("Playable offline"):T("Will prepare","سيتم التحضير")),15));}
            if(preview.Count==0)rows.Children.Add(Body(ReadyText("Waiting for an aired unwatched episode"),13));
            impact.Text=T("Quality: {0} · Budget: {1} · Keep free: {2}","الجودة: {0} · الميزانية: {1} · المساحة الحرة: {2}",preference.Resolution,ReadySize(manager.ReadyViewState.BudgetBytes),ReadySize(manager.ReadyViewState.KeepFreeBytes));
            try{var missing=preview.Where(e=>!e.Local).ToList();if(missing.Count==0)return;var plan=await Task.Run(()=>manager.PlanSeason(item.Id,new(){Scope="Specific episodes",SpecificEpisodes=missing.Select(e=>new SeasonEpisode(e.Season,e.Episode)).ToList()},readyOnly:true));if(rev!=revision||!window.IsVisible)return;
                var choices=await Task.Run(()=>manager.PreviewReadyChoices(plan));if(rev!=revision||!window.IsVisible)return;long bytes=0;rows.Children.Clear();rows.Children.Add(Text(T("UP NEXT","الحلقات القادمة"),11,"#E4C084"));foreach(var (e,decision) in choices){if(decision.Source is {} chosen){bytes+=decision.Charge;rows.Children.Add(Body($"S{e.Season:00} E{e.Episode:00} · {chosen.Quality} · {ReadySize(chosen.FileBytes)}",15));}else rows.Children.Add(Body($"S{e.Season:00} E{e.Episode:00} · "+ReadyText(decision.Reason),12));}
                impact.Text+=Environment.NewLine+T("Budget impact: {0}","أثر الميزانية: {0}",ReadySize(bytes));
            }catch(Exception){if(rev==revision&&window.IsVisible)rows.Children.Add(Body(ReadyText("Source check failed; recheck scheduled"),12));}
        }
        var cleanup=new CheckBox{Content=T("Remove watched after 7 days","إزالة ما تمت مشاهدته بعد ٧ أيام"),IsChecked=manager.ReadyViewState.CleanupWatched,Margin=new(0,16,0,8)};panel.Children.Add(cleanup);
        panel.Children.Add(Body(T("Only files prepared by Noir Ready. Pinned files stay. Removal waits until every profile has finished; unknown progress keeps the file.","فقط الملفات التي جهّزها نوار. تبقى الملفات المثبتة. تنتظر الإزالة حتى تنتهي جميع الملفات الشخصية من المشاهدة؛ وعند غياب معلومات التقدم يبقى الملف."),12));
        panel.Children.Add(Body(T("Storage is shared across your prepared shows. If it fills up, preparation pauses and your unwatched episodes stay.","المساحة مشتركة بين المسلسلات المُحضّرة. عند امتلائها يتوقف التحضير مؤقتًا وتبقى الحلقات غير المشاهدة."),12));
        var feedback=Body("",12);panel.Children.Add(feedback);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,8,0,0)};panel.Children.Add(buttons);
        buttons.Children.Add(ActionButton(T("Keep ready on this PC","إبقاء الحلقات جاهزة على هذا الكمبيوتر"),async()=>{try{preference.KeepReadyEnabled=true;preference.AutoDownload=true;preference.KeepReadyCount=values[Math.Max(0,count.SelectedIndex)];await Task.Run(()=>manager.SaveSeasonPreferences(item.Id,preference));var removeWatched=cleanup.IsChecked==true;await manager.ChangeReady(s=>{s.CleanupWatched=removeWatched;s.Log("Preferences changed",item.Title);});window.Close();_=manager.PrepareReady("Preferences changed");}catch(Exception){feedback.Text=T("Couldn't save preparation preferences.","تعذر حفظ تفضيلات التحضير.");}},true));
        var notNow=ActionButton(T("Not now","ليس الآن"),()=>window.Close());notNow.Margin=new(10,0,0,0);buttons.Children.Add(notNow);count.SelectionChanged+=(_,_)=>keepReadyPreviewTask=Preview();window.Closed+=(_,_)=>revision++;window.Show();Motion.EnterOnce(panel);keepReadyPreviewTask=Preview();
    }
    public FrameworkElement ReadySettingsPanel()
    {
        var panel=new StackPanel();panel.Children.Add(Heading(T("Noir Ready","جاهز من نوار"),T("A little room for your next watch","مساحة لمشاهدتك القادمة")));if(downloads==null)return panel;var manager=downloads;var state=manager.ReadyViewState;
        var fields=new WrapPanel();panel.Children.Add(fields);
        TextBox Number(string title,long bytes){var column=new StackPanel{Margin=new(0,0,18,8)};column.Children.Add(Body(title,12));var box=new TextBox{Text=(bytes/(double)NoirReadyPolicy.GB).ToString("0.##",System.Globalization.CultureInfo.InvariantCulture),Width=130,Padding=new(10),Margin=new(0,6,0,0)};column.Children.Add(box);fields.Children.Add(column);return box;}
        var budget=Number(T("Preparation budget · GB","ميزانية التحضير · GB"),state.BudgetBytes);var free=Number(T("Keep free · at least 10 GB","المساحة الحرة · 10 GB على الأقل"),state.KeepFreeBytes);
        var cleanup=new CheckBox{Content=T("Remove watched after 7 days","إزالة ما تمت مشاهدته بعد ٧ أيام"),IsChecked=state.CleanupWatched,Margin=new(0,8,0,8)};panel.Children.Add(cleanup);
        var pause=new CheckBox{Content=ReadyText("Pause preparation"),IsChecked=state.Paused,Margin=new(0,8,0,8)};panel.Children.Add(pause);pause.Click+=async(_,_)=>{var paused=pause.IsChecked==true;await manager.ChangeReady(s=>{s.Paused=paused;s.Log(s.Paused?"Pause preparation":"Resume preparation");});if(!manager.ReadyViewState.Paused)_=manager.PrepareReady("Preferences changed");};
        var status=Body("",12);panel.Children.Add(status);panel.Children.Add(ActionButton(T("Save preparation settings","حفظ إعدادات التحضير"),async()=>{if(!double.TryParse(budget.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var b)||!double.IsFinite(b)||b<1||b>10000||!double.TryParse(free.Text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var f)||!double.IsFinite(f)||f<10||f>10000){status.Text=T("Use a budget of 1–10000 GB and keep at least 10 GB free.","استخدم ميزانية من 1 إلى 10000 GB واترك 10 GB حرة على الأقل.");return;}var removeWatched=cleanup.IsChecked==true;await manager.ChangeReady(s=>{s.BudgetBytes=(long)(b*NoirReadyPolicy.GB);s.KeepFreeBytes=(long)(f*NoirReadyPolicy.GB);s.CleanupWatched=removeWatched;s.Log("Preferences changed");});status.Text=T("Preparation settings saved.","تم حفظ إعدادات التحضير.");_=manager.PrepareReady("Preferences changed");},true));return ReadyCard(panel);
    }
    FrameworkElement PreparationCentre()
    {
        var panel=new StackPanel();panel.Children.Add(Heading(T("Preparation centre","مركز التحضير"),T("Your next watch is already here","مشاهدتك القادمة جاهزة هنا")));if(downloads==null)return panel;var manager=downloads;var state=manager.ReadyViewState;var storage=manager.ReadyStorage;
        var tools=new WrapPanel();panel.Children.Add(tools);tools.Children.Add(ActionButton(ReadyText(state.Paused?"Resume preparation":"Pause preparation"),async()=>{await manager.ChangeReady(s=>{s.Paused=!s.Paused;s.Log(s.Paused?"Pause preparation":"Resume preparation");});if(!manager.ReadyViewState.Paused)_=manager.PrepareReady("Preferences changed");RefreshReadyCentre();}));
        tools.Children.Add(ActionButton(T("Check now","تحقق الآن"),async()=>{await manager.PrepareReady("Preferences changed");RefreshReadyCentre();}));
        var usage=Body("",12);panel.Children.Add(usage);
        var storageBar=new ProgressBar{Height=5,Foreground=Brush("#E4C084"),Background=Brush("#29282A"),Margin=new(0,6,0,8)};panel.Children.Add(storageBar);
        panel.Children.Add(Body(T("At least {0} stays free. Unwatched episodes stay on your PC.","تبقى مساحة {0} حرة على الأقل. تبقى الحلقات غير المشاهدة على الكمبيوتر.",ReadySize(state.KeepFreeBytes)),12));
        var titleRows=new StackPanel();panel.Children.Add(titleRows);
        void DrawTitles(){titleRows.Children.Clear();foreach(var title in state.Titles.Values.Take(40)){var row=new StackPanel();row.Children.Add(Text(title.Title,15,"#F4F1EB",FontWeights.SemiBold));row.Children.Add(Body(state.Paused?ReadyText("Preparation paused"):ReadyText(title.Reason),12));if(title.Bytes>0)row.Children.Add(Text(title.Quality+" · "+ReadySize(title.Bytes),12,"#E4C084"));titleRows.Children.Add(ReadyCard(row));}if(state.Titles.Count==0)titleRows.Children.Add(Body(T("Choose Keep ready in a season planner. My List titles prepare automatically; downloading is your choice.","اختر إبقاء الحلقات جاهزة في مخطط الموسم. تُحضّر عناوين قائمتي تلقائيًا؛ والتنزيل باختيارك."),13));}
        var files=new StackPanel();void DrawFiles(){files.Children.Clear();foreach(var file in state.ManagedFiles.TakeLast(60)){var row=new StackPanel();row.Children.Add(Body(file.Title,13));row.Children.Add(Body(file.RemoveAt is {} at?T("Scheduled removal · {0}","الإزالة المجدولة · {0}",at.ToLocalTime().ToString("g",Loc.Culture)):file.Pinned?T("Pinned · stays on this PC","مثبّت · يبقى على هذا الكمبيوتر"):T("Managed by Noir Ready","يديره نوار جاهز"),12));
            var actions=new WrapPanel();row.Children.Add(actions);void Change(string code,Action<ReadyManagedFile> edit){actions.Children.Add(ActionButton(ReadyText(code),async()=>{await manager.ChangeReady(s=>{var current=s.ManagedFiles.FirstOrDefault(f=>f.JobId==file.JobId);if(current!=null){edit(current);s.Log(code,file.Title);}});RefreshReadyCentre();}));}
            Change(file.Pinned?"Unpin file":"Pin file",f=>{f.Pinned=!f.Pinned;if(f.Pinned)f.RemoveAt=null;});if(file.RemoveAt!=null){Change("Keep file",f=>{f.Pinned=true;f.RemoveAt=null;});Change("Cancel removal",f=>{f.CleanupCancelled=true;f.RemoveAt=null;});}files.Children.Add(ReadyCard(row));}}
        panel.Children.Add(new Expander{Header=T("Managed files · pins & scheduled removals","الملفات المُدارة · التثبيت والإزالة المجدولة"),Content=files,Margin=new(0,10,0,8)});
        var log=new StackPanel();void DrawLog(){log.Children.Clear();foreach(var entry in state.Why.TakeLast(200).Reverse())log.Children.Add(Body(entry.At.ToLocalTime().ToString("g",Loc.Culture)+" · "+ReadyText(entry.Code)+(entry.Title.Length>0?" · "+entry.Title:""),12));}panel.Children.Add(new Expander{Header=T("Why · recent preparation activity","السبب · نشاط التحضير الأخير"),Content=new ScrollViewer{Content=log,MaxHeight=320,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},Margin=new(0,10,0,10)});
        string drawn="";readyCentreUpdate=()=>{state=manager.ReadyViewState;storage=manager.ReadyStorage;usage.Text=T("{0} used + {1} reserved · {2} budget","{0} مستخدمة + {1} محجوزة · الميزانية {2}",ReadySize(storage.UsedBytes),ReadySize(storage.ReservedBytes),ReadySize(state.BudgetBytes));storageBar.Maximum=Math.Max(1,state.BudgetBytes);storageBar.Value=Math.Min(state.BudgetBytes,storage.UsedBytes+storage.ReservedBytes);var stamp=state.Paused+string.Join("|",state.Titles.Values.Select(t=>t.Group+t.Reason+t.Bytes))+string.Join("|",state.ManagedFiles.Select(f=>f.JobId+f.Pinned+f.RemoveAt+f.CleanupCancelled))+state.Why.LastOrDefault()?.At.ToString("O");if(stamp!=drawn){drawn=stamp;DrawTitles();DrawFiles();DrawLog();}};readyCentreUpdate();Motion.EnterOnce(panel);return ReadyCard(panel);
    }
    Border? readyCentreHost;
    Task? keepReadyPreviewTask;
    Action? readyCentreUpdate;
    void RefreshReadyCentre(){if(readyCentreHost!=null)readyCentreHost.Child=PreparationCentre();}
}
