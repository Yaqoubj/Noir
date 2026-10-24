using System.Windows;
using System.Windows.Controls;

namespace Noir;

public partial class MainWindow
{
    void ShowSeasonPlanner(VideoItem item)
    {
        if(downloads==null)return;
        var manager=downloads;
        var window=new Window{Owner=this,ShowActivated=!validationMode,Title="Season planner · "+item.Title,Width=980,Height=850,MinWidth=760,MinHeight=600,Background=Brush("#101012"),Foreground=Brush("#F4F1EB"),WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var layout=new DockPanel{Margin=new(28),Background=Brush("#101012")};window.Content=layout;
        var footer=new StackPanel{Margin=new(0,14,0,0)};DockPanel.SetDock(footer,Dock.Bottom);layout.Children.Add(footer);
        var summary=Text("Choose your scope. Noir will compare packs and episode links.",15,"#E4C084",FontWeights.SemiBold);footer.Children.Add(summary);
        var feedback=Body("",12);footer.Children.Add(feedback);
        var actions=new WrapPanel();footer.Children.Add(actions);
        DownloadPlan? plan=null;var picks=new Dictionary<PlannedEpisode,EpisodeDownloadOption?>();var excluded=new HashSet<PlannedEpisode>();
        var confirmId=Guid.NewGuid().ToString();
        var download=ActionButton("Download episodes",()=>{
            if(plan==null)return;
            try{
                var ids=manager.EnqueuePlan(new(){PlanId=plan.Id,RequestId=confirmId,FileIds=picks.Where(p=>p.Value!=null&&!excluded.Contains(p.Key)).Select(p=>p.Value!.File.Id).ToList()});
                Status.Text=$"Queued {ids.Count} episodes for this PC.";window.Close();ShowDownloads();
            }catch(Exception e){feedback.Text=e is System.IO.IOException?e.Message:"Couldn't queue this plan. Refresh sources and try again.";}
        },true);download.IsEnabled=false;actions.Children.Add(download);
        var refresh=ActionButton("Refresh plan",()=>{});refresh.Margin=new(12,0,0,0);actions.Children.Add(refresh);
        var content=new StackPanel();layout.Children.Add(new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        content.Children.Add(Heading(item.Title,"Season planner · choose what to get, then review every episode"));
        var controls=new StackPanel();content.Children.Add(controls);
        var progressText=Body("",12);content.Children.Add(progressText);
        var rows=new StackPanel();content.Children.Add(rows);
        var sources=new StackPanel();content.Children.Add(new Expander{Header="Source availability",Content=sources,Margin=new(0,12,0,4)});
        var request=new DownloadPlanRequest();var preference=manager.SeasonPreferences(item.Id);
        var episodes=store.EpisodesFor(item.GroupKey);var seasons=episodes.Where(e=>e.Season>0).Select(e=>e.Season).Distinct().Order().ToList();
        var chosenSeasons=seasons.ToHashSet();var specific=new HashSet<SeasonEpisode>();
        string from="1",next="3";
        var lifetime=new CancellationTokenSource();CancellationTokenSource? search=null;int revision=0;
        refresh.Click+=(_,_)=>_=Build();
        window.Closed+=(_,_)=>{lifetime.Cancel();search?.Cancel();lifetime.Dispose();};
        void SavePreference(){try{manager.SaveSeasonPreferences(item.Id,preference);}catch(System.IO.IOException e){feedback.Text=e.Message;}}
        void Invalidate(bool cancelSearch=true){if(cancelSearch){revision++;search?.Cancel();refresh.IsEnabled=true;}plan=null;download.IsEnabled=false;summary.Text="Update your plan to review these choices.";rows.Children.Clear();}
        void OptionsChanged(){Invalidate();RenderControls();_=Build();}
        FrameworkElement CompactChips(string[] options,string selected,Action<string> choose)
        {
            var chips=(WrapPanel)Chips(options,selected,choose);chips.Margin=new(0,5,0,6);
            foreach(var button in chips.Children.OfType<Button>()){
                button.FontSize=12;button.Padding=new(11,6,11,6);button.Margin=new(0,0,5,6);
                button.Background=Brush((string)button.Content==selected?"#E4C084":"#242427");
            }
            return chips;
        }
        void RenderControls()
        {
            controls.Children.Clear();controls.Children.Add(Body("WHAT TO GET",11));
            controls.Children.Add(CompactChips(["Missing episodes","Unwatched only","From episode N","Next N episodes","Specific episodes","Several seasons / whole show"],request.Scope,value=>{request.Scope=value;OptionsChanged();}));
            if(request.Scope is "From episode N" or "Next N episodes"){
                var row=new WrapPanel();row.Children.Add(Body(request.Scope=="From episode N"?$"Season {item.Season} · start at episode":"After your furthest watched episode · how many (1–25)",12));
                var number=new TextBox{Text=request.Scope=="From episode N"?from:next,Width=65,Margin=new(12,0,12,10),Padding=new(10,6,10,6)};
                number.TextChanged+=(_,_)=>{if(request.Scope=="From episode N")from=number.Text;else next=number.Text;Invalidate();};row.Children.Add(number);
                row.Children.Add(ActionButton("Update plan",()=>_=Build()));controls.Children.Add(row);
            }
            if(request.Scope=="Several seasons / whole show"){
                var row=new WrapPanel();row.Children.Add(ActionButton("Whole show",()=>{chosenSeasons=seasons.ToHashSet();OptionsChanged();}));
                foreach(var season in seasons){var check=new CheckBox{Content="Season "+season,IsChecked=chosenSeasons.Contains(season),Margin=new(14,8,0,8)};check.Checked+=(_,_)=>{chosenSeasons.Add(season);Invalidate();};check.Unchecked+=(_,_)=>{chosenSeasons.Remove(season);Invalidate();};row.Children.Add(check);}controls.Children.Add(row);
                controls.Children.Add(Body("Each selected season is planned separately, then combined below. Refresh plan to apply season changes.",12));
            }
            if(request.Scope=="Specific episodes"){
                var list=new WrapPanel();foreach(var episode in episodes.Where(e=>e.Season>0&&e.Episode>0)){
                    var key=new SeasonEpisode(episode.Season,episode.Episode);var check=new CheckBox{Content=$"S{episode.Season:00} E{episode.Episode:00}",ToolTip=episode.EpisodeTitle,IsChecked=specific.Contains(key),Margin=new(0,6,16,6)};
                    check.Checked+=(_,_)=>{specific.Add(key);Invalidate();};check.Unchecked+=(_,_)=>{specific.Remove(key);Invalidate();};list.Children.Add(check);
                }
                controls.Children.Add(new Expander{Header="Choose episodes · then Refresh plan",IsExpanded=true,Content=new ScrollViewer{Content=list,MaxHeight=170,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
            }
            var preferenceGrid=new Grid{Margin=new(0,6,0,0)};controls.Children.Add(preferenceGrid);
            for(int column=0;column<3;column++)preferenceGrid.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
            StackPanel PreferenceGroup(string label,int column){var panel=new StackPanel{Margin=new(0,0,12,0)};var caption=Body(label,11);caption.Margin=new(0,4,0,2);panel.Children.Add(caption);Grid.SetColumn(panel,column);preferenceGrid.Children.Add(panel);return panel;}
            var qualityGroup=PreferenceGroup("PREFERRED QUALITY",0);qualityGroup.Children.Add(CompactChips(["Any","720p","1080p","2160p"],preference.Resolution,value=>{preference.Resolution=value;SavePreference();OptionsChanged();}));
            var languageGroup=PreferenceGroup("AUDIO LANGUAGE",1);
            var languages=new[]{"All","English","Arabic","Japanese","Korean","Hindi","Spanish","French"}.Append(preference.Language).Distinct().ToArray();
            languageGroup.Children.Add(CompactChips(languages,preference.Language,value=>{preference.Language=value;SavePreference();OptionsChanged();}));
            var sizes=new Dictionary<string,long>{{"Any size",0},{"Under 500 MB",500L*1024*1024},{"Under 1 GB",1024L*1024*1024},{"Under 2 GB",2L*1024*1024*1024},{"Under 4 GB",4L*1024*1024*1024}};
            var sizeLabel=sizes.FirstOrDefault(p=>p.Value==preference.MaxEpisodeBytes).Key??DownloadNaming.Size(preference.MaxEpisodeBytes)+" max";sizes[sizeLabel]=preference.MaxEpisodeBytes;
            var sizeGroup=PreferenceGroup("MAXIMUM PER EPISODE",2);sizeGroup.Children.Add(CompactChips(sizes.Keys.ToArray(),sizeLabel,value=>{preference.MaxEpisodeBytes=sizes[value];SavePreference();OptionsChanged();}));
            var auto=new CheckBox{Content="Auto-download new episodes",IsChecked=preference.AutoDownload,Margin=new(0,12,0,4),Foreground=Brush("#E4C084")};
            auto.Checked+=(_,_)=>{preference.AutoDownload=true;SavePreference();};auto.Unchecked+=(_,_)=>{preference.AutoDownload=false;SavePreference();};controls.Children.Add(auto);
            controls.Children.Add(Body("Preferences are remembered for this show. Automatic downloads use ready files; other new episodes still send an alert.",12));
        }
        void UpdateSummary()
        {
            if(plan==null)return;
            var selected=picks.Where(p=>p.Value!=null&&!excluded.Contains(p.Key)).Select(p=>p.Value!).ToList();var bytes=selected.Sum(o=>o.File.Size);
            var missing=plan.Episodes.Count(e=>e.State=="Not found");var unknown=selected.Count(o=>o.File.Size<=0);var fits=bytes+64L*1024*1024<=plan.FreeBytes;
            summary.Text=$"{selected.Count} episodes · {DownloadNaming.Size(bytes)}{(unknown>0?$" + {unknown} unknown sizes":"")} · {DownloadNaming.Size(plan.FreeBytes)} free · {missing} not found";
            progressText.Text=$"{plan.ExistingEpisodes} on PC · {plan.QueuedEpisodes} queued · {plan.Episodes.Count(e=>e.State=="Unaired")} not aired";
            feedback.Text=(!fits?"Not enough space. Exclude episodes or choose smaller files. ":"")+"Save to: "+plan.Folder+(plan.ScheduleKnown?"":" · Schedule unavailable; known episodes only");
            download.Content=$"Download {selected.Count} episodes";download.IsEnabled=selected.Count>0&&fits;
        }
        void RenderRows()
        {
            rows.Children.Clear();if(plan==null)return;
            foreach(var season in plan.Episodes.GroupBy(e=>e.Season)){
                rows.Children.Add(Heading("Season "+season.Key));
                foreach(var episode in season){
                    var chosen=picks.GetValueOrDefault(episode);var card=new StackPanel{Margin=new(16)};
                    var header=new DockPanel();var change=ActionButton("Change",()=>{});DockPanel.SetDock(change,Dock.Right);header.Children.Add(change);
                    var check=new CheckBox{Content=Text($"E{episode.Episode:00}  {(episode.Title.Length>0?episode.Title:"Episode "+episode.Episode)}",15,weight:FontWeights.SemiBold),IsChecked=chosen!=null&&!excluded.Contains(episode),IsEnabled=chosen!=null,VerticalContentAlignment=VerticalAlignment.Center,Margin=new(0)};
                    check.Checked+=(_,_)=>{excluded.Remove(episode);UpdateSummary();};check.Unchecked+=(_,_)=>{excluded.Add(episode);UpdateSummary();};header.Children.Add(check);card.Children.Add(header);
                    var state=chosen?.CacheState??episode.State;
                    card.Children.Add(new Border{Background=Brush(state is "Ready" or "On your server"?"#30291E":"#252529"),CornerRadius=new(5),Padding=new(8,3,8,3),HorizontalAlignment=HorizontalAlignment.Left,Margin=new(24,8,0,6),Child=Text(state,11,state is "Ready" or "On your server"?"#E4C084":"#AAA6A0")});
                    if(chosen!=null){
                        card.Children.Add(Body($"{chosen.Source} · {chosen.Resolution} · {chosen.Language} · {DownloadNaming.Size(chosen.File.Size)}",12));
                        var release=Body(chosen.Release,12);release.TextTrimming=TextTrimming.CharacterEllipsis;release.MaxHeight=38;release.ToolTip=chosen.Release;card.Children.Add(release);
                    }
                    card.Children.Add(Body(episode.Chosen?.File.Id==chosen?.File.Id?episode.Reason:"Your choice · "+(chosen?.CacheState??episode.State),12));
                    change.IsEnabled=episode.Alternatives.Count>0;change.ToolTip=$"{episode.Alternatives.Count} available options";
                    change.Click+=(_,_)=>{
                        var menu=new ContextMenu();foreach(var option in episode.Alternatives){
                            var label=new StackPanel{MaxWidth=620};label.Children.Add(Text($"{option.Source} · {option.Resolution} · {option.Language} · {DownloadNaming.Size(option.File.Size)} · {option.CacheState}",12));label.Children.Add(Body(option.Release,11));
                            var entry=new MenuItem{Header=label,IsCheckable=true,IsChecked=picks.GetValueOrDefault(episode)?.File.Id==option.File.Id};
                            entry.Click+=(_,_)=>{picks[episode]=option;excluded.Remove(episode);RenderRows();UpdateSummary();};menu.Items.Add(entry);
                        }
                        change.ContextMenu=menu;menu.PlacementTarget=change;menu.IsOpen=true;
                    };
                    rows.Children.Add(new Border{Background=Brush("#18181B"),BorderBrush=Brush("#2B2B2F"),BorderThickness=new(1),CornerRadius=new(12),Child=card,Margin=new(0,0,0,10)});
                }
            }
            if(plan.Episodes.Count==0)rows.Children.Add(Body("No episodes in this scope. Choose episodes or another season above."));
        }
        async Task Build()
        {
            search?.Cancel();search=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var active=search;var version=++revision;
            Invalidate(false);feedback.Text="";progressText.Text="Comparing packs, episode links and your servers…";refresh.IsEnabled=false;sources.Children.Clear();
            try{
                request.Preferences=preference;
                if(request.Scope=="From episode N"){if(!int.TryParse(from,out var n))throw new System.IO.IOException("Enter a starting episode number.");request.FromEpisode=n;}
                if(request.Scope=="Next N episodes"){if(!int.TryParse(next,out var n))throw new System.IO.IOException("Enter how many episodes (1–25).");request.NextCount=n;}
                if(request.Scope=="Several seasons / whole show"&&chosenSeasons.Count==0)throw new System.IO.IOException("Choose at least one season.");
                request.Seasons=request.Scope=="Several seasons / whole show"?chosenSeasons.Order().ToList():[];request.SpecificEpisodes=specific.OrderBy(e=>e.Season).ThenBy(e=>e.Episode).ToList();
                var progress=new Progress<DownloadSourceStatus>(s=>{if(version==revision&&!active.IsCancellationRequested)progressText.Text=s.Source+" · "+s.State+" · "+s.Message;});
                var result=await manager.PlanSeason(item.Id,request,progress,active.Token);
                if(active.IsCancellationRequested||version!=revision)return;
                plan=result;confirmId=Guid.NewGuid().ToString();picks.Clear();excluded.Clear();foreach(var episode in plan.Episodes)picks[episode]=episode.Chosen;
                foreach(var status in plan.Sources.DistinctBy(s=>(s.Source,s.State,s.Message)))sources.Children.Add(Body(status.Source+" · "+status.State+" · "+status.Message,12));
                progressText.Text=plan.Message;RenderRows();UpdateSummary();
            }catch(OperationCanceledException)when(active.IsCancellationRequested){}
            catch(Exception e){if(version==revision){progressText.Text="Couldn't build this plan.";feedback.Text=e is System.IO.IOException?e.Message:"Check source availability and try Refresh plan.";}}
            finally{if(ReferenceEquals(search,active))search=null;active.Dispose();if(version==revision)refresh.IsEnabled=true;}
        }
        RenderControls();window.Show();_=Build();
    }
}
