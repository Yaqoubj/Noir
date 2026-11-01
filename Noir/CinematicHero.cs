using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
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
        var b=new Button{Width=48,Height=48,Padding=new(0),ToolTip=T(tip),Background=Brush(on?"#E4C084":"#40FFFFFF"),Foreground=Brush(on?"#16120C":"#F4F1EB"),Margin=new(10,0,0,0)};
        b.Content=new TextBlock{Text=glyph,FontSize=19,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        Motion.ButtonFeedback(b);b.Template=RoundTemplate();System.Windows.Automation.AutomationProperties.SetName(b,T(tip));b.Click+=(_,_)=>action();return b;
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
        var media=new Border{ClipToBounds=true};var artwork=new Grid();media.Child=artwork;HeroArtwork(artwork,File.Exists(item.Backdrop)?item.Backdrop:item.Poster,item.GroupKey);grid.Children.Add(media);var overlay=new Grid();while(artwork.Children.Count>1){var shade=artwork.Children[1];artwork.Children.RemoveAt(1);overlay.Children.Add(shade);}grid.Children.Add(overlay);PreviewHost.Configure(store.DataDirectory,store.GetProgress,store.EverWatched,TmdbKey());PreviewHost.Attach(media,item,detail?1800:1500);PreviewHost.SetOverlay(media,overlay);
        var panel=new StackPanel{Margin=new(sidePad,0,sidePad,detail?42:70),MaxWidth=700,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Bottom};
        var pro=store.GetProgress(item);var playable=CanPlay(item);
        var eyebrow=justFinished?(item.IsMovie?"YOU JUST WATCHED":"YOU FINISHED THIS SHOW"):continuing&&!playable?"UP NEXT · NOT ON THIS PC YET":continuing&&pro is {Completed:false,Position:>=1}?"CONTINUE WATCHING":continuing?"UP NEXT":detail?(store.TitleStatus(group)).ToUpperInvariant():"TONIGHT'S PICK";
        var tag=new Border{Background=Brush("#33E4C084"),BorderBrush=Brush("#66E4C084"),BorderThickness=new(1),CornerRadius=new(4),Padding=new(9,3,9,3),HorizontalAlignment=HorizontalAlignment.Left,Child=Text(T(eyebrow),11,"#F0D3A0",FontWeights.SemiBold)};panel.Children.Add(tag);
        // The title's own logo artwork when Cinemeta has one; otherwise the name in display type.
        if(TitleLogo(item,detail?120:150,480) is {} logo){logo.Margin=new(0,18,0,18);panel.Children.Add(logo);}
        else{var title=Text(item.Title,item.Title.Length>24?50:66,weight:FontWeights.Bold);title.FontFamily=new FontFamily("Segoe UI Variable Display, Segoe UI");title.LineHeight=item.Title.Length>24?56:72;title.Margin=new(0,14,0,10);panel.Children.Add(title);}
        // One quiet line of facts instead of a row of badges.
        var rating=store.State.PersonalReviews.GetValueOrDefault(item.Id)?.Rating??0;
        panel.Children.Add(HeroFacts(item,group,rating));
        var showEpisode=!item.IsMovie&&(continuing||detail);
        if(showEpisode){var ep=Bidi(Text(item.EpisodeLabel+(item.EpisodeTitle.Length>0&&(!store.State.HideSpoilers||store.EverWatched(item)||pro is {Position:>0})?"  ·  "+item.EpisodeTitle:""),15,"#F4F1EB",FontWeights.SemiBold));ep.Margin=new(0,14,0,0);panel.Children.Add(ep);}
        var summaryText=item.Summary.Length>0?item.Summary:"";
        if(summaryText.Length>0){var summary=Bidi(Text(summaryText,15,"#C9C4BD"));summary.MaxHeight=66;summary.LineHeight=22;summary.TextTrimming=TextTrimming.WordEllipsis;summary.Margin=new(0,12,0,0);summary.MaxWidth=520;summary.HorizontalAlignment=HorizontalAlignment.Left;panel.Children.Add(summary);}
        if(pro is {Completed:false,Position:>=1,Duration:>0})
        {
            var progress=new DockPanel{Margin=new(0,18,0,0),MaxWidth=420,HorizontalAlignment=HorizontalAlignment.Left};
            var left=Text(Loc.MinutesLeft(Math.Max(1,(int)Math.Ceiling((pro.Duration-pro.Position)/60))),12,"#D9D4CC");left.Margin=new(14,0,0,0);DockPanel.SetDock(left,Dock.Right);progress.Children.Add(left);
            progress.Children.Add(new ProgressBar{Value=pro.Position,Maximum=pro.Duration,Height=4,Width=300,VerticalAlignment=VerticalAlignment.Center,Background=Brush("#55FFFFFF")});panel.Children.Add(progress);
        }
        if(!detail&&continuing&&CatchUpCard(group,true) is {} catchUp)panel.Children.Add(catchUp);
        var actions=new WrapPanel{Orientation=Orientation.Horizontal,Margin=new(0,26,0,0)};
        if(justFinished)
        {
            actions.Children.Add(HeroButton(rating>0?"★  Your rating":"★  Rate it",()=>OpenDiary(item),true));
            if(item.IsMovie&&playable)actions.Children.Add(HeroButton("↻  Watch again",()=>_=PlayVideo(item,true),false));
        }
        else if(!playable&&StreamOffered&&ActiveDownload(item)==null&&System.Text.RegularExpressions.Regex.IsMatch(item.ImdbId,@"^tt\d+$"))
        {
            // Not on this PC: stream it now, or download it to keep.
            actions.Children.Add(HeroButton(pro is {Completed:false,Position:>=1}?"▶   Resume stream":"▶   Stream"+(continuing&&!item.IsMovie?" "+item.EpisodeLabel.Replace(" · ",""):""),()=>StreamItem(item),true));
            actions.Children.Add(HeroButton("↓  Download",()=>FindDownloads(item),false));
        }
        else actions.Children.Add(HeroButton(!playable?(ActiveDownload(item)!=null?"Downloading…":"↓  Get it on this PC"):pro is {Completed:false,Position:>=1}?"▶   Resume":continuing&&!item.IsMovie?"▶   Play "+item.EpisodeLabel.Replace(" · ",""):"▶   Play",()=>{if(playable)_=PlayVideo(item);else if(ActiveDownload(item)!=null)Navigate("Downloads");else FindDownloads(item);},true));
        var listed=store.State.Watchlist.Contains(item.GroupKey);
        var list=HeroButton(listed?"✓  In My List":"＋  Add to My List",()=>ToggleWatchlist(item),false);list.ToolTip=T(listed?"In My List · click to remove":"Add to My List");actions.Children.Add(list);
        if(!detail)actions.Children.Add(HeroButton("ⓘ   More info",()=>OpenDetails(group),false));
        else if(playable&&pro is {Completed:false,Position:>=1})actions.Children.Add(HeroButton("↺   Start over",()=>_=PlayVideo(item,true),false));
        if(!justFinished)actions.Children.Add(RoundAction(rating>0?"★":"☆","Rate & note",()=>OpenDiary(item)));
        actions.Children.Add(RoundAction("💬","Reviews & comments",()=>OpenReviews(item)));
        if(detail)actions.Children.Add(TrailerAction(item));
        foreach(FrameworkElement action in actions.Children)action.Margin=new(action.Margin.Left,action.Margin.Top,action.Margin.Right,8);panel.Children.Add(actions);overlay.Children.Add(panel);FitHero(grid,panel,detail);panel.SizeChanged+=(_,e)=>{if(e.HeightChanged)FitHeroContent();};void FitHeroContent(){panel.Measure(new Size(Math.Max(300,grid.ActualWidth-2*sidePad),double.PositiveInfinity));grid.Height=Math.Max(HeroHeight(detail),panel.DesiredSize.Height+TopBar.Height+24);}
        return Bleed(grid,detail?6:0);
    }
    readonly Dictionary<string,Task<BitmapImage?>> heroPreloads=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,Task<Color>> heroTints=new(StringComparer.OrdinalIgnoreCase);
    Task<BitmapImage?> PreloadHero(string path)
    {
        if(CachedImage(path,1920) is {} cached)return Task.FromResult<BitmapImage?>(cached);
        if(heroPreloads.TryGetValue(path,out var pending))return pending;
        if(heroPreloads.Count>=3)heroPreloads.Remove(heroPreloads.Keys.First());
        return heroPreloads[path]=Task.Run(()=>DecodeImage(path,1920));
    }
    Task<Color> HeroTint(string key,string path)
    {
        if(heroTints.TryGetValue(key,out var known))return known;
        if(heroTints.Count>=128)heroTints.Remove(heroTints.Keys.First());
        return heroTints[key]=Task.Run(()=>
        {
            try
            {
                var small=DecodeImage(path,32);if(small==null)return Color.FromRgb(91,66,39);
                var bitmap=new FormatConvertedBitmap(small,PixelFormats.Bgra32,null,0);bitmap.Freeze();var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);
                var buckets=new long[64];var red=new long[64];var green=new long[64];var blue=new long[64];
                for(int i=0;i<pixels.Length;i+=4){int b=pixels[i],g=pixels[i+1],r=pixels[i+2];var high=Math.Max(r,Math.Max(g,b));var low=Math.Min(r,Math.Min(g,b));if(high<30||low>230)continue;int bin=(r/64)*16+(g/64)*4+b/64;var weight=1+high-low;buckets[bin]+=weight;red[bin]+=r*weight;green[bin]+=g*weight;blue[bin]+=b*weight;}
                var best=Array.IndexOf(buckets,buckets.Max());var count=buckets[best];return count==0?Color.FromRgb(91,66,39):Color.FromRgb((byte)(red[best]/count),(byte)(green[best]/count),(byte)(blue[best]/count));
            }
            catch{return Color.FromRgb(91,66,39);}
        });
    }
    void HeroArtwork(Grid hero,string path,string key)
    {
        var fallback=new RadialGradientBrush(Color.FromRgb(62,47,35),Color.FromRgb(11,11,13)){Center=new(.8,.35),GradientOrigin=new(.8,.35),RadiusX=.9,RadiusY=1};hero.Background=fallback;
        // Stretch inside an explicit viewport: a top-aligned Image otherwise keeps its natural height.
        var viewport=new Grid{ClipToBounds=true,IsHitTestVisible=false};var image=new Image{Stretch=Stretch.UniformToFill,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch};viewport.Children.Add(image);hero.Children.Add(viewport);Motion.Drift(image);
        var side=new LinearGradientBrush{StartPoint=new(0,.5),EndPoint=new(1,.5)};side.GradientStops.Add(new(Color.FromArgb(255,11,11,13),0));side.GradientStops.Add(new(Color.FromArgb(245,11,11,13),.32));side.GradientStops.Add(new(Color.FromArgb(210,11,11,13),.55));side.GradientStops.Add(new(Color.FromArgb(35,11,11,13),.75));side.GradientStops.Add(new(Color.FromArgb(8,11,11,13),.90));
        hero.SizeChanged+=(_,_)=>{var width=hero.ActualWidth;if(width<=0)return;var edge=(sidePad+Math.Min(520,width-2*sidePad))/width;side.GradientStops[1].Offset=Math.Clamp(edge-.23,.32,.62);side.GradientStops[2].Offset=Math.Clamp(edge+.02,.55,.82);side.GradientStops[3].Offset=Math.Min(.96,side.GradientStops[2].Offset+.20);side.GradientStops[4].Offset=Math.Min(1,side.GradientStops[3].Offset+.15);};
        var floor=new LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(0,1)};floor.GradientStops.Add(new(Color.FromArgb(38,11,11,13),0));floor.GradientStops.Add(new(Color.FromArgb(0,11,11,13),.35));floor.GradientStops.Add(new(Color.FromArgb(20,11,11,13),.63));floor.GradientStops.Add(new(Color.FromArgb(255,11,11,13),1));
        hero.Children.Add(new Border{Background=side,IsHitTestVisible=false});
        var glow=new Border{Opacity=.18,IsHitTestVisible=false};hero.Children.Add(glow);hero.Children.Add(new Border{Background=floor,IsHitTestVisible=false});
        void Tint(Color color){glow.Background=new RadialGradientBrush(Color.FromArgb(150,color.R,color.G,color.B),Colors.Transparent){Center=new(.68,.38),GradientOrigin=new(.68,.38),RadiusX=.7,RadiusY=.85};if(hero.IsLoaded&&cinematic)PageAmbient.Background=new LinearGradientBrush(Color.FromArgb(30,color.R,color.G,color.B),Colors.Transparent,90);}
        var requested=false;var painted=false;var tintKey=key+"|"+path;
        async void Paint()
        {
            if(requested)return;requested=true;var source=await PreloadHero(path);heroPreloads.Remove(path);if(closing)return;if(source!=null){image.Source=Remember(path,1920,source);painted=true;if(hero.IsLoaded)Motion.FadeIn(image);}
            var tint=await HeroTint(tintKey,path);if(!closing)Tint(tint);
        }
        hero.Loaded+=(_,_)=>{if(painted)Motion.FadeIn(image);Paint();if(heroTints.TryGetValue(tintKey,out var tint)&&tint.IsCompletedSuccessfully)Tint(tint.Result);};
        Paint();
    }
    bool CarouselVisible(FrameworkElement host)
    {
        if(!host.IsLoaded||!host.IsVisible)return false;
        try{var bounds=host.TransformToAncestor(ContentScroll).TransformBounds(new Rect(host.RenderSize));return bounds.Bottom>0&&bounds.Top<ContentScroll.ViewportHeight;}catch(InvalidOperationException){return false;}
    }
    // Home: up to five titles that matter right now, rotating every nine seconds with a cross-fade.
    FrameworkElement HeroCarousel(List<(VideoItem Item,List<VideoItem> Group,bool Continuing,bool Finished)> slides)
    {
        if(slides.Count==1) return CinematicHero(slides[0].Item,slides[0].Group,slides[0].Continuing,slides[0].Finished);
        var host=new Grid();var index=0;FrameworkElement? shown=null;
        var dots=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom,Margin=new(0,0,0,26)};
        void Show(int next,bool animate)
        {
            index=(next+slides.Count)%slides.Count;var s=slides[index];var slide=CinematicHero(s.Item,s.Group,s.Continuing,s.Finished);slide.Margin=new(0);
            var old=shown;shown=slide;host.Children.Insert(0,slide);
            if(old!=null&&animate&&Motion.Enabled){Motion.FadeIn(slide);Panel.SetZIndex(old,1);Panel.SetZIndex(slide,0);var fade=new DoubleAnimation(0,TimeSpan.FromMilliseconds(650));fade.Completed+=(_,_)=>{old.BeginAnimation(OpacityProperty,null);host.Children.Remove(old);};old.Unloaded+=(_,_)=>old.BeginAnimation(OpacityProperty,null);old.BeginAnimation(OpacityProperty,fade);}
            else if(old!=null)host.Children.Remove(old);
            var upcoming=slides[(index+1)%slides.Count].Item;_=PreloadHero(File.Exists(upcoming.Backdrop)?upcoming.Backdrop:upcoming.Poster);
            for(int i=0;i<dots.Children.Count;i++){var d=(Border)dots.Children[i];d.Width=i==index?30:16;d.Background=Brush(i==index?"#F4F1EB":"#4DFFFFFF");}
        }
        for(int i=0;i<slides.Count;i++){var target=i;var dot=new Border{Height=4,Width=16,CornerRadius=new(2),Margin=new(3,0,3,0),Cursor=Cursors.Hand,ToolTip=slides[i].Item.Title,Background=Brush("#4DFFFFFF")};dot.MouseLeftButtonDown+=(_,e)=>{Show(target,true);e.Handled=true;};dots.Children.Add(dot);}
        Panel.SetZIndex(dots,5);host.Children.Add(dots);Show(0,false);
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(9)};timer.Tick+=(_,_)=>{if(!host.IsMouseOver&&!host.IsKeyboardFocusWithin&&IsActive&&current==null&&CarouselVisible(host))Show(index+1,true);};
        host.Loaded+=(_,_)=>{if(Motion.Enabled&&!validationMode)timer.Start();};host.Unloaded+=(_,_)=>timer.Stop();
        return Bleed(host);
    }
}
