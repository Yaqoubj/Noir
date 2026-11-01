using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Noir;
public partial class MainWindow
{
    Border? justWatchedCard;
    DispatcherTimer? justWatchedTimer;
    bool justWatchedInteracted;
    string justWatchedDirectory="";
    KeyEventHandler? justWatchedEscape;
    ContextMenu? justWatchedRatingMenu;
#if NOIR_VALIDATE
    bool validateJustWatched;
    string? justWatchedReviewsOpened;
#endif
    void JustWatchedInteraction(VideoItem item)
    {
        if(justWatchedDirectory==store.DataDirectory&&justWatchedCard?.Tag as string==item.Id)justWatchedInteracted=true;
        HistoryPrefs.JustWatchedIgnores=0;SaveHistoryPrefs();
    }
    FrameworkElement JustWatchedPanel(VideoItem item,bool compact)
    {
        var directory=store.DataDirectory;var root=new StackPanel{MaxWidth=420,FlowDirection=Loc.Arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight};
        var row=new WrapPanel{VerticalAlignment=VerticalAlignment.Center};
        var question=item.IsMovie?T("How was {0}?","كيف كان {0}؟",item.Title):T("How was S{0} E{1}?","كيف كانت الحلقة {1} من الموسم {0}؟",item.Season,item.Episode);
        var prompt=Text(question,compact?11:19,"#F4F1EB",FontWeights.SemiBold);prompt.VerticalAlignment=VerticalAlignment.Center;prompt.Margin=new(0,0,compact?5:0,compact?0:8);if(compact){prompt.MaxWidth=112;prompt.TextWrapping=TextWrapping.NoWrap;prompt.TextTrimming=TextTrimming.CharacterEllipsis;prompt.ToolTip=question;}
        if(compact)row.Children.Add(prompt);else{root.Children.Add(prompt);if(!item.IsMovie){var title=Text(item.Title,12,"#9F9A93");title.Margin=new(0,0,0,12);root.Children.Add(title);}}
        bool Active()=>directory==store.DataDirectory;
        Button Chip(string label,string name,Action action)
        {
            var b=ActionButton(label,()=>{if(Active())action();});b.Focusable=true;b.Padding=compact?new(3,2,3,2):new(9,5,9,5);b.Margin=new(0,0,compact?1:4,0);b.ToolTip=name;AutomationProperties.SetName(b,name);return b;
        }
        var reactions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};var reactionButtons=new List<(string Mood,Button Button)>();
        void RefreshReactions(){foreach(var (mood,button) in reactionButtons){var on=store.State.Reactions.GetValueOrDefault(item.Id)==mood;button.Background=Brush(on?"#45E4C084":"#222226");button.Foreground=Brush(on?"#E4C084":"#C7C2BC");AutomationProperties.SetHelpText(button,on?T("Selected","محدد"):"");}}
        foreach(var mood in new[]{("loved","♥",T("Loved","أحببته")),("funny","☺",T("Funny","مضحك")),("wow","✦",T("Wow","مذهل")),("sad","☂",T("Sad","حزين")),("meh","−",T("Meh","عادي"))})
        {
            var b=Chip(mood.Item2,mood.Item3,()=>{store.SetReaction(item.Id,store.State.Reactions.GetValueOrDefault(item.Id)==mood.Item1?"":mood.Item1);JustWatchedInteraction(item);RefreshReactions();});if(compact)b.Width=22;reactionButtons.Add((mood.Item1,b));reactions.Children.Add(b);
        }
        RefreshReactions();row.Children.Add(reactions);
        var size=compact?13:20;var rating=Chip("",T("Your rating · optional","تقييمك · اختياري"),()=>{});rating.FlowDirection=FlowDirection.LeftToRight;rating.Margin=new(compact?3:8,0,0,0);
        int Rating()=>store.State.PersonalReviews.GetValueOrDefault(item.Id)?.Rating10??0;
        var menu=new ContextMenu{FlowDirection=root.FlowDirection};rating.ContextMenu=menu;if(!compact)justWatchedRatingMenu=menu;
        void RefreshRating(){rating.Content=HalfRating(Rating(),size);AutomationProperties.SetHelpText(rating,T("{0:0.#} stars","{0:0.#} نجمة",Rating()/2.0));foreach(var choice in menu.Items.OfType<MenuItem>())choice.IsChecked=Equals(choice.Tag,Rating());}
        void Rate(int score){if(!Active())return;store.SetReview10(item,score,store.State.PersonalReviews.GetValueOrDefault(item.Id)?.Note??"");JustWatchedInteraction(item);RefreshRating();}
        for(int n=0;n<=10;n++){var score=n;var label=n==0?T("Clear rating","مسح التقييم"):T("{0:0.#} stars","{0:0.#} نجمة",n/2.0);var choice=new MenuItem{Header=label,Tag=n,IsCheckable=true};AutomationProperties.SetName(choice,label);choice.Click+=(_,_)=>Rate(score);menu.Items.Add(choice);}
        rating.PreviewMouseLeftButtonUp+=(_,e)=>{var score=Math.Clamp((int)Math.Ceiling((e.GetPosition(rating).X-rating.Padding.Left)/(size+2)*2),1,10);Rate(Rating()==score?0:score);e.Handled=true;};
        rating.Click+=(_,_)=>{menu.PlacementTarget=rating;menu.IsOpen=true;};rating.PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Left or Key.Right){Rate(Math.Clamp(Rating()+(e.Key==Key.Right?1:-1),0,10));e.Handled=true;}};RefreshRating();row.Children.Add(rating);root.Children.Add(row);
        var noteHost=new StackPanel();var editor=new TextBox{Text=store.State.PersonalReviews.GetValueOrDefault(item.Id)?.Note??"",Visibility=Visibility.Collapsed,MaxLength=10000,TextWrapping=TextWrapping.Wrap,MinHeight=38,MaxHeight=130,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new(9),Margin=new(0,8,0,0),Background=Brush("#222226"),Foreground=Brush("#F4F1EB")};AutomationProperties.SetName(editor,T("Private note · optional","ملاحظة خاصة · اختيارية"));
        var snippet=Text("",12,"#C7C2BC");snippet.TextWrapping=TextWrapping.NoWrap;snippet.TextTrimming=TextTrimming.CharacterEllipsis;snippet.Margin=new(0,7,0,0);var editing=false;string saved=editor.Text;
        Button? note=null;
        void RefreshNote(){saved=store.State.PersonalReviews.GetValueOrDefault(item.Id)?.Note??"";snippet.Text=saved.Replace('\r',' ').Replace('\n',' ');snippet.Visibility=saved.Length>0?Visibility.Visible:Visibility.Collapsed;if(note!=null){var name=saved.Length>0?T("Edit note","تعديل الملاحظة"):T("Add a note…","أضف ملاحظة…");note.Content=compact?"✎":name;note.ToolTip=name;AutomationProperties.SetName(note,name);}}
        void FinishNote(bool save)
        {
            if(!editing)return;editing=false;if(save&&Active()){store.SetReview10(item,Rating(),editor.Text.Trim());JustWatchedInteraction(item);}editor.Visibility=Visibility.Collapsed;RefreshNote();
        }
        note=Chip(compact?"✎":T("Add a note…","أضف ملاحظة…"),T("Add a note…","أضف ملاحظة…"),()=>{if(editing)return;editing=true;editor.Text=saved;editor.Visibility=Visibility.Visible;editor.Focus();editor.CaretIndex=editor.Text.Length;});
        editor.PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Enter or Key.Escape){FinishNote(e.Key==Key.Enter);note.Focus();e.Handled=true;}};editor.LostKeyboardFocus+=(_,_)=>FinishNote(true);
        var others=Chip(compact?"💬":T("💬 What others said","💬 آراء الآخرين"),T("What others said","آراء الآخرين"),()=>{JustWatchedInteraction(item);
#if NOIR_VALIDATE
            if(validationMode){justWatchedReviewsOpened=item.Id;return;}
#endif
            OpenReviews(item);
        });
        if(compact){row.Children.Add(note);row.Children.Add(others);}else{var links=new WrapPanel{Margin=new(0,12,0,0)};links.Children.Add(note);links.Children.Add(others);root.Children.Add(links);}
        noteHost.Children.Add(snippet);noteHost.Children.Add(editor);root.Children.Add(noteHost);RefreshNote();return root;
    }
    bool JustWatchedFinale(VideoItem item)
    {
        if(item.IsMovie)return true;if(item.Season<=0)return false;
        // A later season or an ended series confirms a season boundary; an incomplete airing schedule cannot.
        var episodes=store.EpisodesFor(item.GroupKey).Where(i=>i.Season>0).ToList();
        return !episodes.Any(i=>i.Season==item.Season&&i.Episode>item.Episode)&&(store.ShowEnded(item.GroupKey)||episodes.Any(i=>i.Season>item.Season));
    }
    void OfferJustWatched(VideoItem? finished,bool countdownRunning=false)
    {
        if(finished==null||countdownRunning||closing||current!=null||PlayerRoot.Visibility==Visibility.Visible||HistoryPrefs.AfterWatching=="Never"||HistoryPrefs.JustWatchedQuiet||HistoryPrefs.AfterWatching=="Finales"&&!JustWatchedFinale(finished))return;
        if(validationMode)
        {
#if NOIR_VALIDATE
            if(!validateJustWatched)return;
#else
            return;
#endif
        }
        DismissJustWatched(false);justWatchedDirectory=store.DataDirectory;justWatchedInteracted=false;
        var content=new StackPanel();var head=new DockPanel();var close=ActionButton("✕",()=>DismissJustWatched(true));close.Padding=new(6,2,6,2);close.HorizontalAlignment=HorizontalAlignment.Right;AutomationProperties.SetName(close,T("Dismiss Just watched","إغلاق بطاقة شاهدت للتو"));close.ToolTip=T("Close","إغلاق");DockPanel.SetDock(close,Dock.Right);head.Children.Add(close);head.Children.Add(Text(T("JUST WATCHED","شاهدت للتو"),10,"#E4C084",FontWeights.SemiBold));content.Children.Add(head);content.Children.Add(JustWatchedPanel(finished,false));
        MountJustWatched(content,finished.Id);StartJustWatchedTimer();
    }
    void MountJustWatched(FrameworkElement content,string id)
    {
        justWatchedCard=new Border{Tag=id,Width=420,Padding=new(18),Margin=new(22),CornerRadius=new(14),Background=Brush("#161618"),BorderBrush=Brush("#60E4C084"),BorderThickness=new(1),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,FlowDirection=Loc.Arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight,Child=content};
        // Grid mirrors its alignment with the library's FlowDirection; Arabic therefore lands bottom-left.
        AutomationProperties.SetName(justWatchedCard,T("Just watched","شاهدت للتو"));Grid.SetColumnSpan(justWatchedCard,2);Panel.SetZIndex(justWatchedCard,100);LibraryRoot.Children.Add(justWatchedCard);
        justWatchedEscape=(_,e)=>{if(e.Key!=Key.Escape||justWatchedCard==null||PlayerRoot.Visibility==Visibility.Visible||Keyboard.FocusedElement is TextBox box&&box.IsDescendantOf(justWatchedCard)||e.OriginalSource is TextBox source&&source.IsDescendantOf(justWatchedCard))return;DismissJustWatched(false);e.Handled=true;};AddHandler(Keyboard.PreviewKeyDownEvent,justWatchedEscape,true);
        if(SystemParameters.ClientAreaAnimation)Motion.FadeIn(justWatchedCard);
    }
    void StartJustWatchedTimer()
    {
        var idle=TimeSpan.Zero;var watch=Stopwatch.StartNew();justWatchedTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(200)};
        justWatchedTimer.Tick+=(_,_)=>{var elapsed=watch.Elapsed;watch.Restart();if(justWatchedCard==null)return;if(closing||current!=null||store.DataDirectory!=justWatchedDirectory){DismissJustWatched(false);return;}if(!justWatchedCard.IsMouseOver&&!justWatchedCard.IsKeyboardFocusWithin&&justWatchedRatingMenu?.IsOpen!=true)idle+=elapsed;if(idle>=TimeSpan.FromSeconds(12))DismissJustWatched(true);};justWatchedTimer.Start();
    }
    void DismissJustWatched(bool ignored)
    {
        if(justWatchedCard==null)return;var count=ignored&&!justWatchedInteracted&&justWatchedCard.Tag as string!="quiet"&&justWatchedDirectory==store.DataDirectory;
        justWatchedTimer?.Stop();justWatchedTimer=null;if(justWatchedRatingMenu!=null)justWatchedRatingMenu.IsOpen=false;justWatchedRatingMenu=null;if(justWatchedEscape!=null)RemoveHandler(Keyboard.PreviewKeyDownEvent,justWatchedEscape);justWatchedEscape=null;LibraryRoot.Children.Remove(justWatchedCard);justWatchedCard=null;
        if(!count)return;HistoryPrefs.JustWatchedIgnores=Math.Min(5,HistoryPrefs.JustWatchedIgnores+1);
        if(HistoryPrefs.JustWatchedIgnores>=5){HistoryPrefs.JustWatchedQuiet=true;if(!HistoryPrefs.JustWatchedNoticeShown){HistoryPrefs.JustWatchedNoticeShown=true;ShowJustWatchedQuietNotice();}}SaveHistoryPrefs();
    }
    void ShowJustWatchedQuietNotice()
    {
        var row=new StackPanel();row.Children.Add(Text(T("Noir will stop asking after episodes — you can still rate in History","سيتوقف نوار عن السؤال بعد الحلقات — يمكنك التقييم في السجل دائمًا"),12,"#C7C2BC"));var keep=ActionButton(T("Keep asking","استمر بالسؤال"),()=>SetAfterWatching("Always"));AutomationProperties.SetName(keep,T("Keep asking","استمر بالسؤال"));keep.Margin=new(0,9,0,0);row.Children.Add(keep);var close=ActionButton(T("Close","إغلاق"),()=>DismissJustWatched(false));AutomationProperties.SetName(close,T("Close","إغلاق"));row.Children.Add(close);MountJustWatched(row,"quiet");StartJustWatchedTimer();
    }
}
