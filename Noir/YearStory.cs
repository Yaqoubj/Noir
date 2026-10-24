using System.Globalization;
#if !NOIR_HEADLESS
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
#endif

namespace Noir;
public static class YearStoryLogic
{
    public static string IdentityLabel(IEnumerable<DateTime> source,int movies=0,int episodes=0)
    {
        var times=source.ToList();if(times.Count==0)return "A new chapter";
        if(times.Count(t=>t.Hour>=22||t.Hour<5)*5>=times.Count*2)return "Night-owl binger";
        if(times.Count(t=>t.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)*2>=times.Count)return "Weekend cinephile";
        if(movies>episodes)return "Cinema explorer";if(episodes>=movies*2&&episodes>0)return "Series storyteller";return "Curious cinephile";
    }
    public static int AnnualPace(int completed,DateTime today)=>(int)Math.Round(Math.Max(0,completed)*(DateTime.IsLeapYear(today.Year)?366d:365d)/Math.Max(1,today.DayOfYear),MidpointRounding.AwayFromZero);
    public static string PacingText(int completed,DateTime today,bool weekly=false,bool paused=false)=>paused?"Goal paused":weekly?$"{Math.Max(0,completed)} this week":$"On pace for {AnnualPace(completed,today)} this year";
}
#if !NOIR_HEADLESS
public partial class MainWindow
{
    sealed record StorySlide(string Label,string Value,string Detail,List<VideoItem> Posters);
    List<StorySlide> StorySlides(int year)
    {
        var views=AllViewings().Where(v=>!v.DateUnknown&&v.At.LocalDateTime.Year==year).ToList();var precise=views.Where(v=>v.Precise).ToList();var collage=views.GroupBy(v=>v.Item.GroupKey).OrderByDescending(g=>g.Sum(v=>v.Seconds)).Take(4).Select(g=>g.First().Item).ToList();
        var slides=new List<StorySlide>();void Add(string label,string value,string detail,List<VideoItem>? posters=null)=>slides.Add(new(label,value,detail,posters??collage));
        Add(T("Time well spent","وقتٌ مع الحكايات"),T("{0:0.#} hours","{0:0.#} ساعة",views.Sum(v=>v.Seconds)/3600),T("Every story left a little something with you.","تركت كل حكاية شيئاً صغيراً معك."));
        foreach(var movie in new[]{false,true}){var top=views.Where(v=>v.Item.IsMovie==movie).GroupBy(v=>v.Item.GroupKey).OrderByDescending(g=>g.Sum(v=>v.Seconds)).FirstOrDefault();Add(movie?T("Your top movie","فيلمك الأبرز"):T("Your top show","مسلسلك الأبرز"),top?.First().Item.Title??T("A story still to come","حكاية لم تأتِ بعد"),top==null?T("Your next favourite is out there.","عملك المفضل القادم ينتظرك."):T("{0:0.#} hours in this world","{0:0.#} ساعة في هذا العالم",top.Sum(v=>v.Seconds)/3600),top==null?collage:new(){top.First().Item});}
        var busiest=views.GroupBy(v=>v.At.LocalDateTime.Month).OrderByDescending(g=>g.Sum(v=>v.Seconds)).FirstOrDefault();Add(T("Your busiest month","شهرك الأكثر مشاهدة"),busiest==null?"—":new DateTime(year,busiest.Key,1).ToString("MMMM",Loc.Culture),busiest==null?T("The year is yours to fill.","السنة لك لتملأها بالحكايات."):T("{0} viewings worth remembering","{0} مشاهدة تستحق التذكر",busiest.Count()));
        var longest=HistoryRewards.Streaks(precise.Select(v=>new RewardViewing(v.At.LocalDateTime)),new DateTime(year,12,31)).Longest;Add(T("You kept the story going","واصلت الحكاية"),T("{0} days","{0} يوم",longest),T("Your longest viewing streak.","أطول سلسلة أيام مشاهدة لك."));
        var genre=views.SelectMany(v=>v.Item.Genres.Split(new[]{'·',',','|'},StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(g=>(Genre:g,v.Seconds))).GroupBy(x=>x.Genre).OrderByDescending(g=>g.Sum(x=>x.Seconds)).FirstOrDefault();Add(T("Your favourite genre","نوعك المفضل"),genre?.Key??T("A little of everything","قليلٌ من كل شيء"),T("The worlds you returned to most.","العوالم التي عدت إليها أكثر."));
        var rated=views.Select(v=>v.Item).DistinctBy(i=>i.Id).Select(i=>(Item:i,Review:store.State.PersonalReviews.GetValueOrDefault(i.Id))).Where(x=>x.Review!=null&&(x.Review.Rating10>0||x.Review.Rating>0)).OrderByDescending(x=>x.Review!.Rating10>0?x.Review.Rating10:x.Review.Rating*2).FirstOrDefault();Add(T("Your highest rated","أعلى تقييماتك"),rated.Item?.Title??T("Trust your own taste","ثق بذوقك"),rated.Review==null?T("Ratings are always your choice.","التقييم دائماً اختيارك."):T("{0:0.#} / 5 stars","{0:0.#} / 5 نجوم",(rated.Review.Rating10>0?rated.Review.Rating10:rated.Review.Rating*2)/2d),rated.Item==null?collage:new(){rated.Item});
        var identity=YearStoryLogic.IdentityLabel(precise.Select(v=>v.At.LocalDateTime),views.Count(v=>v.Item.IsMovie),views.Count(v=>!v.Item.IsMovie));var identityAr=identity switch{"A new chapter"=>"فصل جديد","Night-owl binger"=>"رفيق حكايات الليل","Weekend cinephile"=>"عاشق سينما العطلة","Cinema explorer"=>"مستكشف السينما","Series storyteller"=>"رفيق المسلسلات",_=>"عاشق الحكايات الفضولي"};Add(T("Your viewing personality","شخصيتك في المشاهدة"),T(identity,identityAr),T("A little snapshot of your rhythm.","لمحة صغيرة عن إيقاعك."));
        Add(T("Until the next story","إلى الحكاية القادمة"),T("That was your {0}.","هذه كانت حكايتك في {0}.",year),T("Keep the moments. Discover something wonderful.","احتفظ باللحظات. واكتشف حكاية جميلة."));return slides;
    }
    FrameworkElement StoryCard(StorySlide slide,int year,int index,int count)
    {
        var card=new Grid{Width=450,Height=800,Background=new LinearGradientBrush(Brush("#251E14").Color,Brush("#08090B").Color,70),ClipToBounds=true};var body=new Grid{Margin=new(30,26,30,30)};card.Children.Add(body);body.RowDefinitions.Add(new(){Height=GridLength.Auto});body.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});body.RowDefinitions.Add(new(){Height=GridLength.Auto});body.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var mast=new DockPanel();var page=Text($"{index+1:00} / {count:00}",11,"#A28C6C");DockPanel.SetDock(page,Dock.Right);mast.Children.Add(page);mast.Children.Add(Text(T("NOIR · YOUR {0}","NOIR · حكايتك في {0}",year),12,"#E4C084",FontWeights.SemiBold));body.Children.Add(mast);
        var words=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new(0,20,0,20)};Grid.SetRow(words,1);var label=Text(slide.Label,15,"#CFB58D",FontWeights.SemiBold);words.Children.Add(label);var value=Text(slide.Value,slide.Value.Length>35?34:slide.Value.Length>20?41:52,"#F4E8D0",FontWeights.SemiBold);value.LineHeight=double.NaN;value.Margin=new(0,14,0,12);words.Children.Add(value);var detail=Text(slide.Detail,14,"#ACA69D");detail.MaxWidth=340;detail.HorizontalAlignment=HorizontalAlignment.Left;words.Children.Add(detail);body.Children.Add(words);
        var art=new Grid{Height=252,Margin=new(0,0,0,30)};Grid.SetRow(art,2);var posters=slide.Posters.Take(4).ToList();for(int i=0;i<posters.Count;i++){var item=posters[i];var cover=new Border{Width=posters.Count==1?158:126,Height=posters.Count==1?237:189,CornerRadius=new(9),BorderThickness=new(1),BorderBrush=Brush("#706047"),ClipToBounds=true,Child=HistoryArt(item,posters.Count==1?237:189),HorizontalAlignment=posters.Count==1?HorizontalAlignment.Center:HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,Margin=posters.Count==1?new(0):new(i*83,i%2==0?0:34,0,0),RenderTransformOrigin=new(.5,.5),RenderTransform=new RotateTransform(posters.Count==1?-3:(i-1.5)*3)};art.Children.Add(cover);}if(posters.Count==0){var empty=Text("✦",100,"#7D6846");empty.HorizontalAlignment=HorizontalAlignment.Center;empty.VerticalAlignment=VerticalAlignment.Center;art.Children.Add(empty);}body.Children.Add(art);
        var footer=new DockPanel();Grid.SetRow(footer,3);var mark=Text("NOIR",13,"#E4C084",FontWeights.Bold);DockPanel.SetDock(mark,Dock.Right);footer.Children.Add(mark);footer.Children.Add(Text(T("A year in stories","سنة من الحكايات"),11,"#817568"));body.Children.Add(footer);return card;
    }
    void OpenYearStory(int year)
    {
        var slides=StorySlides(year);var position=0;var window=new Window{Owner=this,Title=T("Your {0} story","حكايتك في {0}",year),Width=508,Height=960,MinWidth=320,MinHeight=560,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brush("#09090B"),Foreground=Brush("#F4F1EB")};var shell=new Grid{Margin=new(16)};shell.RowDefinitions.Add(new(){Height=GridLength.Auto});shell.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});shell.RowDefinitions.Add(new(){Height=GridLength.Auto});var progress=new UniformGrid{Rows=1,Margin=new(4,0,4,10)};shell.Children.Add(progress);var view=new Viewbox{Stretch=Stretch.Uniform};Grid.SetRow(view,1);shell.Children.Add(view);var controls=new DockPanel{Margin=new(0,12,0,0)};Grid.SetRow(controls,2);shell.Children.Add(controls);shell.RowDefinitions.Add(new(){Height=GridLength.Auto});var saveStatus=Text("",11,"#C9B28C");saveStatus.HorizontalAlignment=HorizontalAlignment.Center;saveStatus.Margin=new(0,8,0,0);Grid.SetRow(saveStatus,3);shell.Children.Add(saveStatus);
        void Show(int value){position=Math.Clamp(value,0,slides.Count-1);progress.Children.Clear();for(int i=0;i<slides.Count;i++)progress.Children.Add(new Border{Height=3,Margin=new(2,0,2,0),CornerRadius=new(2),Background=Brush(i<=position?"#E4C084":"#39332A")});view.Child=StoryCard(slides[position],year,position,slides.Count);Motion.FadeIn(view);}
        var previous=ActionButton("‹",()=>Show(position-1));previous.ToolTip=T("Previous story","الحكاية السابقة");previous.FontSize=24;previous.Width=42;DockPanel.SetDock(previous,Dock.Left);controls.Children.Add(previous);var next=ActionButton("›",()=>Show(position+1));next.ToolTip=T("Next story","الحكاية التالية");next.FontSize=24;next.Width=42;DockPanel.SetDock(next,Dock.Right);controls.Children.Add(next);var save=ActionButton(T("Save this card","حفظ هذه البطاقة"),()=>{try{var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),"Noir");Directory.CreateDirectory(folder);var file=Path.Combine(folder,$"Noir {year} story {position+1:00} {DateTime.Now:yyyyMMdd-HHmmss}.png");SaveStoryCard(slides[position],year,position,slides.Count,file,true);saveStatus.Text=T("Saved · copied to clipboard","تم الحفظ · نُسخت إلى الحافظة");}catch(Exception e){store.Log(e);saveStatus.Text=T("Couldn't save this card.","تعذّر حفظ البطاقة.");}});save.HorizontalAlignment=HorizontalAlignment.Center;controls.Children.Add(save);window.Content=shell;
        Point? down=null;view.PreviewMouseLeftButtonDown+=(_,e)=>down=e.GetPosition(view);view.PreviewMouseLeftButtonUp+=(_,e)=>{if(down is not {} start)return;var end=e.GetPosition(view);down=null;Show(position+(Math.Abs(end.X-start.X)>35?(end.X<start.X?1:-1):end.X<view.ActualWidth*.35?-1:1));};window.KeyDown+=(_,e)=>{if(e.Key==Key.Escape)window.Close();else if(e.Key is Key.Left or Key.Up){Show(position-1);e.Handled=true;}else if(e.Key is Key.Right or Key.Down or Key.Space){Show(position+1);e.Handled=true;}};Show(0);window.Show();
    }
    void SaveStoryCard(StorySlide slide,int year,int index,int count,string file,bool clipboard=false)
    {
        var card=StoryCard(slide,year,index,count);card.Measure(new Size(450,800));card.Arrange(new Rect(0,0,450,800));card.UpdateLayout();var bitmap=new RenderTargetBitmap(1080,1920,230.4,230.4,PixelFormats.Pbgra32);bitmap.Render(card);bitmap.Freeze();var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(file))encoder.Save(output);if(clipboard)try{Clipboard.SetImage(bitmap);}catch{}
    }
}
#endif
