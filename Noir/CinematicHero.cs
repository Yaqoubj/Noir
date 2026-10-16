using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Noir;
// Full-bleed artwork heroes: Home rotates through a few titles; title pages show one.
public partial class MainWindow
{
    double HeroHeight(bool detail)=>Math.Max(detail?440:500,(ContentScroll.ViewportHeight>0?ContentScroll.ViewportHeight:ActualHeight-40)*(detail?.66:.78));
    // Reserve the top navigation area even on short screens and with wrapped, long titles.
    void FitHero(Grid hero,FrameworkElement content,bool detail)
    {
        void Fit()
        {
            var width=hero.ActualWidth>0?hero.ActualWidth:Math.Max(780,ActualWidth);
            content.MaxWidth=Math.Min(700,Math.Max(300,width-2*sidePad));
            content.Measure(new Size(width,double.PositiveInfinity));
            hero.Height=Math.Max(HeroHeight(detail),content.DesiredSize.Height+TopBar.Height+24);
        }
        hero.Loaded+=(_,_)=>Fit();hero.SizeChanged+=(_,e)=>{if(e.WidthChanged)Fit();};
    }
    Button RoundAction(string glyph,string tip,Action action,bool on=false)
    {
        var b=new Button{Width=48,Height=48,Padding=new(0),ToolTip=tip,Background=Brush(on?"#E4C084":"#40FFFFFF"),Foreground=Brush(on?"#16120C":"#F4F1EB"),Margin=new(10,0,0,0)};
        b.Content=new TextBlock{Text=glyph,FontSize=19,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        b.Template=RoundTemplate();System.Windows.Automation.AutomationProperties.SetName(b,tip);b.Click+=(_,_)=>action();return b;
    }
    static ControlTemplate RoundTemplate()
    {
        var border=new FrameworkElementFactory(typeof(Border),"Chrome");border.SetValue(Border.CornerRadiusProperty,new CornerRadius(24));border.SetValue(Border.BorderThicknessProperty,new Thickness(1.2));
        border.SetValue(Border.BorderBrushProperty,Brush("#55FFFFFF"));border.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
        var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(HorizontalAlignmentProperty,HorizontalAlignment.Center);content.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(content);
        var template=new ControlTemplate(typeof(Button)){VisualTree=border};
        var hover=new Trigger{Property=IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(Border.BorderBrushProperty,Brush("#F4F1EB"),"Chrome"));template.Triggers.Add(hover);
        return template;
    }
    Button BigButton(string text,Action action,bool primary)
    {
        var b=ActionButton(text,action,primary);b.FontSize=16;b.Padding=new(28,14,30,14);b.FontWeight=FontWeights.SemiBold;
        if(!primary){b.Background=Brush("#40FFFFFF");b.Foreground=Brush("#F4F1EB");}
        b.Margin=new(0,0,12,0);return b;
    }
    FrameworkElement CinematicHero(VideoItem item,List<VideoItem> group,bool continuing,bool justFinished=false)
    {
        var detail=detailKey!=null;var height=HeroHeight(detail);
        var grid=new Grid{Height=height,ClipToBounds=true,Background=Brush("#151517")};
        var source=ImageSource(System.IO.File.Exists(item.Backdrop)?item.Backdrop:item.Poster,1920);
        if(source!=null)
        {
            // A slow push-in keeps the artwork alive without distracting.
            var image=new Image{Source=source,Stretch=Stretch.UniformToFill,VerticalAlignment=VerticalAlignment.Top,RenderTransformOrigin=new(.6,.3)};
            var zoom=new ScaleTransform(1,1);image.RenderTransform=zoom;
            var drift=new DoubleAnimation(1,1.07,TimeSpan.FromSeconds(24)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase()};
            zoom.BeginAnimation(ScaleTransform.ScaleXProperty,drift);zoom.BeginAnimation(ScaleTransform.ScaleYProperty,drift);grid.Children.Add(image);
        }
        var side=new LinearGradientBrush{StartPoint=new(0,.5),EndPoint=new(1,.5)};side.GradientStops.Add(new(Color.FromArgb(245,11,11,13),0));side.GradientStops.Add(new(Color.FromArgb(170,11,11,13),.38));side.GradientStops.Add(new(Color.FromArgb(0,11,11,13),.72));
        var floor=new LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(0,1)};floor.GradientStops.Add(new(Color.FromArgb(150,11,11,13),0));floor.GradientStops.Add(new(Color.FromArgb(0,11,11,13),.22));floor.GradientStops.Add(new(Color.FromArgb(0,11,11,13),.55));floor.GradientStops.Add(new(Color.FromArgb(255,11,11,13),1));
        grid.Children.Add(new Border{Background=side,IsHitTestVisible=false});grid.Children.Add(new Border{Background=floor,IsHitTestVisible=false});
        var panel=new StackPanel{Margin=new(sidePad,0,sidePad,detail?42:70),MaxWidth=700,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Bottom};
        var pro=store.GetProgress(item);var playable=CanPlay(item);
        var eyebrow=justFinished?(item.IsMovie?"YOU JUST WATCHED":"YOU FINISHED THIS SHOW"):continuing&&!playable?"UP NEXT · NOT ON THIS PC YET":continuing&&pro is {Completed:false,Position:>=1}?"CONTINUE WATCHING":continuing?"UP NEXT":detail?(store.TitleStatus(group)).ToUpperInvariant():"TONIGHT'S PICK";
        var tag=new Border{Background=Brush("#33E4C084"),BorderBrush=Brush("#66E4C084"),BorderThickness=new(1),CornerRadius=new(4),Padding=new(9,3,9,3),HorizontalAlignment=HorizontalAlignment.Left,Child=Text(eyebrow,11,"#F0D3A0",FontWeights.SemiBold)};panel.Children.Add(tag);
        var title=Text(item.Title,item.Title.Length>24?50:66,weight:FontWeights.Bold);title.FontFamily=new FontFamily("Segoe UI Variable Display, Segoe UI");title.LineHeight=item.Title.Length>24?56:72;title.Margin=new(0,14,0,10);panel.Children.Add(title);
        // One quiet line of facts instead of a row of badges.
        var facts=new List<string>();if(item.Year>0)facts.Add(item.Year.ToString());
        if(item.IsMovie){if(item.Duration>0)facts.Add($"{(int)(item.Duration/3600)}h {(int)(item.Duration%3600/60)}m");}else{var seasons=group.Where(i=>i.Season>0).Select(i=>i.Season).Distinct().Count();if(seasons>0)facts.Add(seasons==1?"1 season":seasons+" seasons");facts.Add(group.Count(i=>i.Season>0)+" episodes");}
        facts.AddRange(item.Genres.Split('·',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Take(3));
        var rating=store.State.PersonalReviews.GetValueOrDefault(item.Id)?.Rating??0;
        var factLine=new TextBlock{FontSize=14,Foreground=Brush("#D9D4CC"),TextWrapping=TextWrapping.Wrap};
        if(rating>0){factLine.Inlines.Add(new System.Windows.Documents.Run(new string('★',rating)+"  "){Foreground=Brush("#E4C084")});}
        factLine.Inlines.Add(new System.Windows.Documents.Run(string.Join("   ·   ",facts)));panel.Children.Add(factLine);
        var showEpisode=!item.IsMovie&&(continuing||detail);
        if(showEpisode){var ep=Text(item.EpisodeLabel+(item.EpisodeTitle.Length>0&&(!store.State.HideSpoilers||store.EverWatched(item)||pro is {Position:>0})?"  ·  "+item.EpisodeTitle:""),15,"#F4F1EB",FontWeights.SemiBold);ep.Margin=new(0,14,0,0);panel.Children.Add(ep);}
        var summaryText=item.Summary.Length>0?item.Summary:"";
        if(summaryText.Length>0){var summary=Text(summaryText,15,"#C9C4BD");summary.MaxHeight=66;summary.LineHeight=22;summary.TextTrimming=TextTrimming.WordEllipsis;summary.Margin=new(0,12,0,0);summary.MaxWidth=620;summary.HorizontalAlignment=HorizontalAlignment.Left;panel.Children.Add(summary);}
        if(pro is {Completed:false,Position:>=1,Duration:>0})
        {
            var progress=new DockPanel{Margin=new(0,18,0,0),MaxWidth=420,HorizontalAlignment=HorizontalAlignment.Left};
            var left=Text($"{Math.Max(1,(int)Math.Ceiling((pro.Duration-pro.Position)/60))} min left",12,"#D9D4CC");left.Margin=new(14,0,0,0);DockPanel.SetDock(left,Dock.Right);progress.Children.Add(left);
            progress.Children.Add(new ProgressBar{Value=pro.Position,Maximum=pro.Duration,Height=4,Width=300,VerticalAlignment=VerticalAlignment.Center,Background=Brush("#55FFFFFF")});panel.Children.Add(progress);
        }
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Margin=new(0,26,0,0)};
        if(justFinished)
        {
            actions.Children.Add(BigButton(rating>0?"★  Your rating":"★  Rate it",()=>OpenDiary(item),true));
            if(item.IsMovie&&playable)actions.Children.Add(BigButton("↻  Watch again",()=>_=PlayVideo(item,true),false));
        }
        else actions.Children.Add(BigButton(!playable?(ActiveDownload(item)!=null?"Downloading…":"↓  Get it on this PC"):pro is {Completed:false,Position:>=1}?"▶   Resume":continuing&&!item.IsMovie?"▶   Play "+item.EpisodeLabel.Replace(" · ",""):"▶   Play",()=>{if(playable)_=PlayVideo(item);else if(ActiveDownload(item)!=null)Navigate("Downloads");else FindDownloads(item);},true));
        if(!detail)actions.Children.Add(BigButton("ⓘ   More info",()=>OpenDetails(group),false));
        else if(playable&&pro is {Completed:false,Position:>=1})actions.Children.Add(BigButton("↺   Start over",()=>_=PlayVideo(item,true),false));
        var listed=store.State.Watchlist.Contains(item.GroupKey);
        actions.Children.Add(RoundAction(listed?"✓":"＋",listed?"In My List · click to remove":"Add to My List",()=>ToggleWatchlist(item),listed));
        if(!justFinished)actions.Children.Add(RoundAction(rating>0?"★":"☆","Rate & note",()=>OpenDiary(item)));
        actions.Children.Add(RoundAction("💬","Reviews & comments",()=>OpenReviews(item)));
        panel.Children.Add(actions);grid.Children.Add(panel);FitHero(grid,panel,detail);
        return Bleed(grid,detail?6:0);
    }
    // Home: up to five titles that matter right now, rotating every nine seconds with a cross-fade.
    FrameworkElement HeroCarousel(List<(VideoItem Item,List<VideoItem> Group,bool Continuing,bool Finished)> slides)
    {
        if(slides.Count==1) return CinematicHero(slides[0].Item,slides[0].Group,slides[0].Continuing,slides[0].Finished);
        var host=new Grid();var index=0;FrameworkElement? shown=null;
        var dots=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new(0,0,sidePad,78)};
        void Show(int next,bool animate)
        {
            index=(next+slides.Count)%slides.Count;var s=slides[index];var slide=CinematicHero(s.Item,s.Group,s.Continuing,s.Finished);slide.Margin=new(0);
            var old=shown;shown=slide;host.Children.Insert(0,slide);
            if(old!=null&&animate){var fade=new DoubleAnimation(0,TimeSpan.FromMilliseconds(700));fade.Completed+=(_,_)=>host.Children.Remove(old);old.BeginAnimation(OpacityProperty,fade);Panel.SetZIndex(old,1);Panel.SetZIndex(slide,0);}
            else if(old!=null)host.Children.Remove(old);
            for(int i=0;i<dots.Children.Count;i++){var d=(Border)dots.Children[i];d.Width=i==index?28:8;d.Background=Brush(i==index?"#E4C084":"#66FFFFFF");}
        }
        for(int i=0;i<slides.Count;i++){var target=i;var dot=new Border{Height=8,Width=8,CornerRadius=new(4),Margin=new(5,0,0,0),Cursor=Cursors.Hand,ToolTip=slides[i].Item.Title};dot.MouseLeftButtonDown+=(_,e)=>{Show(target,true);e.Handled=true;};dots.Children.Add(dot);}
        Panel.SetZIndex(dots,5);host.Children.Add(dots);Show(0,false);
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(9)};timer.Tick+=(_,_)=>{if(!host.IsMouseOver&&IsActive&&current==null)Show(index+1,true);};
        host.Loaded+=(_,_)=>timer.Start();host.Unloaded+=(_,_)=>timer.Stop();
        return Bleed(host);
    }
}
