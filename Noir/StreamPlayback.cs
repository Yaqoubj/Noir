using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Noir;
// Stream buttons and the stream picker; StreamSources finds and ranks the streams.
public partial class MainWindow
{
    StreamOption? streaming;List<StreamOption> streamChoices=[];
    int streamRequest;
    CancellationTokenSource? streamSearch;
    public void CancelStreamSearch(){streamRequest++;var previous=streamSearch;streamSearch=null;previous?.Cancel();}
    bool CanStream=>downloads!=null&&StreamSources.Manifest(downloads.Config).Length>0;
    // Stream sits beside every download option; without an AIOStreams link it explains where to add one.
    bool StreamOffered=>downloads!=null;
    bool StreamSetUp()
    {
        if(downloads==null){Status.Text="Downloads are starting. Try again shortly.";return false;}
        if(CanStream)return true;
        Status.Text="Add your AIOStreams link in Settings → Downloads to stream.";settingsSection="Downloads";Navigate("Settings");return false;
    }
    // Finds AIOStreams' streams for this movie or episode and plays the best ready one.
    async void StreamItem(VideoItem item,bool choose=false)
    {
        if(!StreamSetUp())return;
        CancelStreamSearch();using var search=new CancellationTokenSource();streamSearch=search;var request=streamRequest;
        Status.Text="Finding a stream for "+item.Label+"…";
        try
        {
            using var http=DownloadSecurity.Client(TimeSpan.FromSeconds(40));
            var options=StreamSources.Rank(await StreamSources.Find(downloads!.Config,item,http,search.Token),downloads.Config.PreferredResolution,item.IsMovie);
            if(search.IsCancellationRequested||request!=streamRequest||closing)return;
            SmartLists.RememberStream(store.DataDirectory,item.Id,options.Any(o=>o.Cached));
            if(options.Count==0){Status.Text="AIOStreams has no playable stream for "+item.Label+" right now.";return;}
            streamChoices=options;
            if(choose||!options[0].Cached){ChooseStream(item,options,request);return;}
            await PlayStream(item,options[0]);
        }
        catch(OperationCanceledException)when(search.IsCancellationRequested){}
        catch(Exception e)when(e is IOException or HttpRequestException or JsonException or OperationCanceledException){if(search.IsCancellationRequested||request!=streamRequest||closing)return;store.Log(e);Status.Text=e is IOException?e.Message:"AIOStreams couldn't be reached. Try again.";}
        finally{if(ReferenceEquals(streamSearch,search))streamSearch=null;}
    }
    void ChooseStream(VideoItem item,List<StreamOption> options,int request=-1)
    {
        var body=SmartPopup();body.MaxWidth=560;
        body.Children.Add(Text(T("Choose a stream")+" · "+item.Label,15,weight:FontWeights.SemiBold));
        if(!options[0].Cached)body.Children.Add(Body("None of these are ready on your service yet; an unready one may take a while to start.",12));
        var list=new StackPanel{Margin=new(0,8,0,0)};body.Children.Add(new ScrollViewer{Content=list,MaxHeight=360,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        foreach(var o in options.Take(15))
        {
            var option=o;var b=new Button{HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new(12,8,12,8),Margin=new(0,0,0,6),Background=Brush("#1C1C1F")};
            var lines=new StackPanel();b.Content=lines;
            lines.Children.Add(Text((o.Cached?"⚡ ":"⏳ ")+(o.Resolution.Length>0?o.Resolution+" · ":"")+(o.Size>0?DownloadNaming.Size(o.Size)+" · ":"")+(o.Service.Length>0?o.Service:""),12.5,o.Cached?"#E4C084":"#AAA6A0",FontWeights.SemiBold));
            var name=Text(o.Name,12,"#D9D4CC");name.TextTrimming=TextTrimming.CharacterEllipsis;name.TextWrapping=TextWrapping.NoWrap;lines.Children.Add(name);
            System.Windows.Automation.AutomationProperties.SetName(b,"Stream "+o.Name);b.Click+=(_,_)=>{CloseSmart();if(request>=0&&(request!=streamRequest||closing))return;_=PlayStream(item,option);};list.Children.Add(b);
        }
    }
    async Task PlayStream(VideoItem item,StreamOption option)
    {
        if(openingVideo||closing)return;
        streaming=option;Status.Text="Streaming "+item.Label+(option.Resolution.Length>0?" · "+option.Resolution:"")+" through AIOStreams…";
        await PlayVideo(item);
    }
    // Adds stream-only options to the VLC media: a bigger network buffer and the headers the link needs.
    void StreamOptions(LibVLCSharp.Shared.Media media,StreamOption option)
    {
        media.AddOption(":network-caching=3000");
        if(option.UserAgent.Length>0)media.AddOption(":http-user-agent="+option.UserAgent);
        if(option.Referrer.Length>0)media.AddOption(":http-referrer="+option.Referrer);
    }
}
