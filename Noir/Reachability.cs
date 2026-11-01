using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Noir;
// Reachability: a visible ⋯ wherever title actions were right-click only, one complete shortcut sheet
// (? or F1, in the player and the library), and quiet one-time tips remembered in the library state.
public partial class MainWindow
{
    internal static readonly (string Keys,string English,string Arabic)[] PlayerShortcuts=
    [
        ("Space","Play / pause","تشغيل / إيقاف مؤقت"),("← →","Back / forward 10 seconds","رجوع / تقديم 10 ثوانٍ"),("Shift + ← →","Previous / next chapter","الفصل السابق / التالي"),
        ("↑ ↓","Volume","مستوى الصوت"),("M","Mute","كتم الصوت"),("Shift + <  >","Slower / faster","أبطأ / أسرع"),("[  ]","Subtitles earlier / later by 0.5 s","الترجمة أبكر / لاحقًا بنصف ثانية"),
        ("I","Skip a known intro","تخطي مقدمة معروفة"),("F · Double-click","Fullscreen","ملء الشاشة"),("Esc","Leave fullscreen, then back to the library","الخروج من ملء الشاشة ثم العودة إلى المكتبة"),("?","Show this list","عرض هذه القائمة"),
    ];
    internal static readonly (string Keys,string English,string Arabic)[] LibraryShortcuts=
    [
        ("Ctrl + F  ·  /","Search","البحث"),("Right-click  ·  ⋯","Every action for a title or episode","كل الإجراءات الخاصة بعمل أو حلقة"),("Tab · Enter","Move around and open","التنقل والفتح"),
        ("Esc","Close a panel or popup","إغلاق لوحة أو نافذة منبثقة"),("?  ·  F1","Show this list","عرض هذه القائمة"),
    ];
    Grid? shortcutSheet;
    void Help_Click(object sender,RoutedEventArgs e)=>ToggleShortcutSheet(false);
    // The ⋯ twin of right-click: ItemMenu is otherwise invisible on rows and tracker cards.
    Button MoreButton(VideoItem item)
    {
        var b=ActionButton("⋯",()=>{});b.Padding=new(11,2,11,8);b.FontSize=17;b.Margin=new(8,0,0,0);b.VerticalAlignment=VerticalAlignment.Center;b.Background=Brush("#1FFFFFFF");
        b.ToolTip=T("More: download, subtitles, match, mark watched… (or right-click)","المزيد: التنزيل والترجمة والمطابقة وتعليم المشاهدة… (أو النقر بالزر الأيمن)");System.Windows.Automation.AutomationProperties.SetName(b,T("More options for","خيارات أخرى لـ")+" "+item.Label);
        b.Click+=(_,e)=>{e.Handled=true;var menu=ItemMenu(item);b.ContextMenu=menu;menu.PlacementTarget=b;menu.Placement=PlacementMode.Bottom;menu.IsOpen=true;};return b;
    }
    // Returns true once per id, then remembers it so a tip never comes back.
    bool TipOnce(string id){if(validationMode||!store.State.TipsShown.Add(id))return false;store.Save();return true;}
    async void PlayerTipOnce()
    {
        if(validationMode||store.State.TipsShown.Contains("player-shortcuts"))return;var serialNow=serial;await Task.Delay(3500);
        if(serialNow!=serial||current==null||PlayerRoot.Visibility!=Visibility.Visible||!TipOnce("player-shortcuts"))return;
        ShowPlayerHint(PL("Tip · press ? for keyboard shortcuts, [ ] to shift subtitles","نصيحة · اضغط ? لاختصارات لوحة المفاتيح، و[ ] لضبط توقيت الترجمة"),5);
    }
    static bool IsHelpKey(KeyEventArgs e)=>e.Key==Key.F1||e.Key==Key.OemQuestion&&Keyboard.Modifiers==ModifierKeys.Shift;
    // Library keys: Ctrl+F or / focus search, ? or F1 opens the shortcut sheet. Typing is never intercepted.
    void LibraryKeyDown(KeyEventArgs e)
    {
        if(shortcutSheet!=null&&(e.Key==Key.Escape||IsHelpKey(e))){CloseShortcutSheet();e.Handled=true;return;}
        if(e.Key==Key.F&&Keyboard.Modifiers==ModifierKeys.Control&&SearchBox.IsVisible){SearchBox.Focus();SearchBox.SelectAll();e.Handled=true;return;}
        if(e.Key==Key.F1){ToggleShortcutSheet(false);e.Handled=true;return;}
        if(Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox||e.OriginalSource is TextBoxBase or PasswordBox)return;
        if(IsHelpKey(e)){ToggleShortcutSheet(false);e.Handled=true;}
        else if(e.Key is Key.OemQuestion or Key.Divide&&Keyboard.Modifiers==ModifierKeys.None&&SearchBox.IsVisible){SearchBox.Focus();SearchBox.SelectAll();e.Handled=true;}
    }
    void CloseShortcutSheet(){if(shortcutSheet is {} sheet&&sheet.Parent is Panel host)host.Children.Remove(sheet);shortcutSheet=null;}
    FrameworkElement ShortcutRows((string Keys,string English,string Arabic)[] rows,bool player)
    {
        var list=new StackPanel();
        foreach(var (keys,english,arabic) in rows)
        {
            var line=new DockPanel{Margin=new(0,0,0,9)};var caps=new WrapPanel{Width=player?150:170,VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(caps,Dock.Left);line.Children.Add(caps);
            // Keys keep their keyboard order in Arabic: "Shift + <  >", "[  ]" and "← →" must not mirror.
            foreach(var cap in keys.Split("  ·  ",StringSplitOptions.RemoveEmptyEntries)){var key=Text(Loc.Arabic?cap.Replace("Double-click","نقر مزدوج").Replace("Right-click","زر الفأرة الأيمن"):cap,12,"#F4F1EB",FontWeights.SemiBold);key.FlowDirection=FlowDirection.LeftToRight;caps.Children.Add(new Border{Background=Brush("#26FFFFFF"),BorderBrush=Brush("#33FFFFFF"),BorderThickness=new(1),CornerRadius=new(6),Padding=new(8,2,8,3),Margin=new(0,0,6,2),Child=key});}
            var label=Text(player?PL(english,arabic):T(english,arabic),13,"#D6D0C7");label.VerticalAlignment=VerticalAlignment.Center;line.Children.Add(label);list.Children.Add(line);
        }
        return list;
    }
    // Places people rarely find on their own; each closes the sheet and goes there.
    FrameworkElement ShortcutPlaces()
    {
        var list=new StackPanel();
        void Place(string english,string arabic,string hint,string hintArabic,Action go){var b=ActionButton("",()=>{CloseShortcutSheet();go();});var lines=new StackPanel();lines.Children.Add(Text(T(english,arabic),13.5,"#F4F1EB",FontWeights.SemiBold));lines.Children.Add(Text(T(hint,hintArabic),11.5,"#9F9A93"));b.Content=lines;b.Background=Brush("Transparent");b.Padding=new(10,6,10,6);b.Margin=new(-10,0,0,2);b.HorizontalAlignment=HorizontalAlignment.Stretch;b.HorizontalContentAlignment=HorizontalAlignment.Left;System.Windows.Automation.AutomationProperties.SetName(b,english);list.Children.Add(b);}
        Place("Compare taste with a friend","قارن ذوقك مع صديق","Share a taste card and see what you both loved","شارك بطاقة ذوقك واكتشف ما أحببتماه معًا",()=>OpenTaste());
        Place("Your year in stories","حكاية عامك","A swipeable recap of everything you watched","ملخص لكل ما شاهدته",()=>OpenYearStory(historyReviewYear));
        Place("Import, back up or export history","استيراد السجل أو نسخه أو تصديره","Netflix, Trakt, TV Time, Letterboxd, Stremio…","Netflix وTrakt وTV Time وLetterboxd وStremio…",()=>{trackerTab="Add history";Navigate("History");});
        Place("Export the episode calendar","تصدير تقويم الحلقات","Upcoming episodes as an .ics file","الحلقات القادمة كملف ‎.ics",ExportUpcomingCalendar);
        if(!TrackerOnly)Place("Edit Home rows","تعديل صفوف الرئيسية","Choose and reorder what Home shows","اختر ما تعرضه الرئيسية ورتّبه",()=>{Navigate("Home");Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,()=>ContentScroll.ScrollToEnd());});
        if(!TrackerOnly)Place("Organize filenames & folders","تنظيم أسماء الملفات والمجلدات","Tidy names, with Undo","أسماء مرتبة مع إمكانية التراجع",OpenOrganizer);
        Place("Pair a phone or TV","إقران هاتف أو تلفاز","Noir on Android phones and Google TV","Noir على هواتف Android وGoogle TV",()=>{settingsSection="Phone";Navigate("Settings");});
        return list;
    }
    void ToggleShortcutSheet(bool inPlayer)
    {
        if(shortcutSheet!=null){CloseShortcutSheet();return;}
        Panel host=inPlayer?PlayerOverlay:LibraryRoot;var shade=new Grid{Background=Brush("#B3080809")};Panel.SetZIndex(shade,40);if(!inPlayer)Grid.SetColumnSpan(shade,2);
        shade.MouseLeftButtonDown+=(_,e)=>{if(ReferenceEquals(e.OriginalSource,shade))CloseShortcutSheet();e.Handled=true;};
        // Close stays pinned below the scrolling list so it is never clipped.
        var body=new StackPanel();var frame=new DockPanel();frame.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});var card=new Border{Background=Brush("#F71A1A1D"),BorderBrush=Brush("#55E4C084"),BorderThickness=new(1),CornerRadius=new(16),Padding=new(28,24,28,20),Margin=new(24),MaxWidth=inPlayer?560:860,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Child=frame};
        string L(string english,string arabic)=>inPlayer?PL(english,arabic):T(english,arabic);
        // The player keeps a left-to-right layout; the sheet itself still reads right to left in Arabic.
        if(inPlayer&&Loc.Arabic)card.FlowDirection=FlowDirection.RightToLeft;
        body.Children.Add(Text(L(inPlayer?"Keyboard shortcuts":"Shortcuts & tips",inPlayer?"اختصارات لوحة المفاتيح":"الاختصارات والنصائح"),22,weight:FontWeights.SemiBold));
        var sub=Text(L(inPlayer?"Subtitles, audio and timing live under CC. Speed is the 1× button.":"Everything here also has a button; these are the quick ways.",inPlayer?"الترجمة والصوت والتوقيت تحت CC. السرعة هي زر 1×.":"لكل ما هنا زر أيضًا؛ هذه هي الطرق السريعة."),12.5,"#9F9A93");sub.Margin=new(0,4,0,18);body.Children.Add(sub);
        if(inPlayer)body.Children.Add(ShortcutRows(PlayerShortcuts,true));
        else
        {
            var columns=new WrapPanel();body.Children.Add(columns);
            var left=new StackPanel{Width=380,Margin=new(0,0,28,8)};left.Children.Add(Text(T("In the library","في المكتبة"),12,"#E4C084",FontWeights.SemiBold));left.Children.Add(new Border{Height=8});left.Children.Add(ShortcutRows(LibraryShortcuts,false));
            left.Children.Add(Text(T("In the player","في المشغّل"),12,"#E4C084",FontWeights.SemiBold));left.Children.Add(new Border{Height=8});left.Children.Add(ShortcutRows(PlayerShortcuts,false));columns.Children.Add(left);
            var right=new StackPanel{Width=340};right.Children.Add(Text(T("Places you might miss","أماكن قد تفوتك"),12,"#E4C084",FontWeights.SemiBold));right.Children.Add(new Border{Height=6});right.Children.Add(ShortcutPlaces());columns.Children.Add(right);
        }
        var close=ActionButton(L("Close  ·  Esc","إغلاق  ·  Esc"),CloseShortcutSheet);close.HorizontalAlignment=HorizontalAlignment.Right;close.Margin=new(0,10,0,0);System.Windows.Automation.AutomationProperties.SetName(close,"Close shortcuts");DockPanel.SetDock(close,Dock.Bottom);frame.Children.Insert(0,close);
        shade.Children.Add(card);host.Children.Add(shade);shortcutSheet=shade;shade.Opacity=0;Motion.Opacity(shade,1);
        if(inPlayer)ShowControls();else Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,()=>{if(shortcutSheet==shade)close.Focus();});
    }
    // History's overflow: the tracker extras that otherwise sit at the bottom of Stats or Upcoming.
    void ShowHistoryMore(Button target)
    {
        var menu=new ContextMenu();void Add(string english,string arabic,Action act){var row=new MenuItem{Header=T(english,arabic)};row.Click+=(_,_)=>act();menu.Items.Add(row);}
        Add("Your year in stories","حكاية عامك",()=>OpenYearStory(historyReviewYear));Add("Compare taste with a friend","قارن ذوقك مع صديق",()=>OpenTaste());
        Add("Export the episode calendar (.ics)","تصدير تقويم الحلقات (.ics)",ExportUpcomingCalendar);menu.Items.Add(new Separator());
        Add("Back up or export history","نسخ السجل احتياطيًا أو تصديره",()=>{settingsSection="Backup";Navigate("Settings");});Add("Shortcuts & tips  (?)","الاختصارات والنصائح  (?)",()=>ToggleShortcutSheet(false));
        menu.PlacementTarget=target;menu.Placement=PlacementMode.Bottom;menu.IsOpen=true;
    }
}
