using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using QRCoder;
namespace Noir;
// History → Add history: Nuvio connection, where your history came from (with undo), and full backups.
public partial class MainWindow
{
    NuvioSync? nuvioSync;long nuvioPendingCursor;
    // A reviewed Nuvio import moves the cursor when it's applied, never when it's only previewed.
    void CommitNuvioCursor(){if(nuvioPendingCursor>0&&NuvioSessionNow() is {} s){s.Cursor=nuvioPendingCursor;NuvioStorage.Save(store.DataDirectory,s);}nuvioPendingCursor=0;}
    NuvioSession? NuvioSessionNow()=>NuvioStorage.Load(store.DataDirectory);
    FrameworkElement NuvioTile()
    {
        var session=NuvioSessionNow();var connected=session is {Profile:>0}&&store.State.NuvioConnected;
        var waiting=store.State.WatchSync.Values.Count(j=>j.Provider=="Nuvio"&&j.SyncedAt==null)+store.State.NuvioDeletes.Count;
        var state=connected?$"Connected · {(session!.ProfileName.Length>0?session.ProfileName:"profile "+session.Profile)} · "+(store.State.NuvioImportedAt is {} at?"checked "+Ago(at):"not checked yet")+(waiting>0?$" · {waiting} waiting to send":""):"Not connected";
        return SyncTile("Nuvio","Nv","#6C4CF1","#FFFFFF",state,connected?"Two-way: Nuvio's watches come into Noir every few hours, and what you finish or check off in Noir is marked watched in Nuvio.":"Link with a code — no password. Watches flow both ways, like Stremio but two-way.",panel=>
        {
            if(!connected){panel.Children.Add(ActionButton("Connect Nuvio",ConnectNuvio,true));return;}
            var auto=new CheckBox{Content="Check Nuvio automatically",IsChecked=store.State.NuvioAutoImport,Margin=new(0,4,0,2)};auto.Click+=(_,_)=>{store.State.NuvioAutoImport=auto.IsChecked==true;store.Save();};panel.Children.Add(auto);
            var send=new CheckBox{Content="Mark my Noir watches in Nuvio",IsChecked=store.State.SyncNuvio,Margin=new(0,2,0,8)};send.Click+=(_,_)=>{store.State.SyncNuvio=send.IsChecked==true;store.Save();nuvioSync?.Wake();};panel.Children.Add(send);
            var row=new WrapPanel();panel.Children.Add(row);
            void Add(string text,Action act,bool primary=false){var b=ActionButton(text,act,primary);b.Margin=new(0,0,8,8);b.IsEnabled=!importBusy;row.Children.Add(b);}
            Add("Import now",()=>_=ImportNuvio(false),true);
            Add("Send earlier history",()=>{foreach(var e in store.State.Catalog.Values.Where(e=>store.EverWatched(e.Item)))CommunityQueue.Enqueue(store.State,e.Item,store.ViewingHistory(e.Item).FirstOrDefault()?.WatchedAt??e.Progress?.UpdatedAt??DateTimeOffset.UtcNow);store.Save();nuvioSync?.Wake();RenderQuiet();});
            Add("Switch profile",()=>_=PickNuvioProfile(session!));
            Add("Disconnect",()=>{NuvioStorage.Forget(store.DataDirectory);store.State.NuvioConnected=false;store.Save();RenderQuiet();});
            if(nuvioSync?.Message is {Length:>0} m)panel.Children.Add(Body(m,11));
        });
    }
    // The TV-style link: show a code and QR, poll until it's approved at nuvio.tv/link.
    void ConnectNuvio()
    {
        var window=OpenSheet("Connect Nuvio",null,SheetKind.Dialog,440);var body=new StackPanel();window.Add(body);
        var note=Body("Starting…",13);body.Children.Add(note);
        var code=Text("",34,"#E4C084",FontWeights.Bold);code.HorizontalAlignment=HorizontalAlignment.Center;code.Margin=new(0,12,0,8);code.FlowDirection=FlowDirection.LeftToRight;body.Children.Add(code);
        var picture=new Image{Width=180,Height=180,Margin=new(0,0,0,6),HorizontalAlignment=HorizontalAlignment.Center};body.Children.Add(picture);
        var cancel=new CancellationTokenSource();window.Closed+=_=>cancel.Cancel();
        window.Action("Cancel",()=>window.Close(false));var open=window.Action("Open nuvio.tv/link",()=>{},true);open.Visibility=Visibility.Collapsed;
        async Task Link()
        {
            using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(20)};
            NuvioSession? session=null;var client=new NuvioClient(http,null,s=>session=s);
            try
            {
                var link=await client.StartLink("Noir on "+Environment.MachineName,cancel.Token);
                var url=link.Url.Length>0?link.Url:NuvioConfig.LinkPage;
                code.Text=link.Code;note.Text="On your phone or computer, open nuvio.tv/link (or scan the code), sign in to Nuvio and approve this code. Noir never sees your password.";
                using(var data=QRCodeGenerator.GenerateQrCode(url,QRCodeGenerator.ECCLevel.Q))using(var qr=new PngByteQRCode(data)){var image=new BitmapImage();image.BeginInit();image.StreamSource=new MemoryStream(qr.GetGraphic(6));image.CacheOption=BitmapCacheOption.OnLoad;image.EndInit();picture.Source=image;}
                open.Visibility=Visibility.Visible;open.Click+=(_,_)=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url){UseShellExecute=true});
                for(var i=0;i<200;i++)
                {
                    await Task.Delay(link.Interval*1000,cancel.Token);
                    string status;try{status=await client.PollLink(link,cancel.Token);}catch(HttpRequestException){continue;}
                    if(status=="pending")continue;
                    if(status!="approved")throw new IOException("The code expired. Close this window and try again.");
                    note.Text="Approved. Signing in…";await client.Exchange(link,cancel.Token);break;
                }
                if(session==null)throw new IOException("The code expired. Close this window and try again.");
                NuvioStorage.Save(store.DataDirectory,session);window.Close(true);
                await PickNuvioProfile(session);
            }
            catch(OperationCanceledException){}
            catch(Exception e){store.Log(e);note.Text=e is IOException?e.Message:"Nuvio couldn't be reached. Check your connection and try again.";code.Text="";picture.Source=null;}
        }
        _=Link();
    }
    async Task PickNuvioProfile(NuvioSession session)
    {
        List<NuvioProfile> profiles;
        try{using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(20)};profiles=await new NuvioClient(http,session,s=>NuvioStorage.Save(store.DataDirectory,s)).Profiles(CancellationToken.None);}
        catch(Exception e){store.Log(e);Status.Text=e is NuvioExpiredException?e.Message:"Couldn't read your Nuvio profiles. Try again.";return;}
        if(profiles.Count==0)profiles=[new(1,"Main")];
        async Task Use(NuvioProfile p)
        {
            var changed=session.Profile!=p.Index;
            session.Profile=p.Index;session.ProfileName=p.Name;if(changed)session.Cursor=0;NuvioStorage.Save(store.DataDirectory,session);
            // Removals queued for the previous profile must never reach the new one; pending watches are additive and stay.
            store.State.NuvioConnected=true;if(changed){store.State.NuvioKnown.Clear();store.State.NuvioDeletes.Clear();}store.Save();nuvioSync?.Wake();
            Status.Text="Nuvio connected. Reading your watched list…";await ImportNuvio(false);
        }
        if(profiles.Count==1){await Use(profiles[0]);return;}
        var body=SmartPopup();body.Children.Add(Text("Which Nuvio profile is yours?",15,weight:FontWeights.SemiBold));
        var row=new WrapPanel{Margin=new(0,10,0,0)};body.Children.Add(row);
        foreach(var p in profiles){var pick=p;var b=ActionButton(p.Name.Length>0?p.Name:"Profile "+p.Index,()=>{CloseSmart();_=Use(pick);},p.Index==session.Profile);b.Margin=new(0,0,8,8);row.Children.Add(b);}
    }
    // First import and "Import now" show the review; the timer applies new watches silently.
    async Task ImportNuvio(bool silent)
    {
        if(importBusy||NuvioSessionNow() is not {Profile:>0} session||!store.State.NuvioConnected)return;
        var token=CancellationToken.None;
        try
        {
            if(!silent){importBusy=true;importMessage="Reading your Nuvio history…";importProgress=(0,0);importCancel=new();token=importCancel.Token;if(page=="History")Render();}
            using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(30)};using var discovery=DiscoverClient();
            var client=new NuvioClient(http,session,s=>NuvioStorage.Save(store.DataDirectory,s));
            List<ImportRow> rows;int removed=0;long cursor;
            if(silent&&session.Cursor>0)
            {
                // Only each item's latest event counts: watched then un-checked stays un-checked.
                var events=(await client.PullDelta(session.Profile,session.Cursor,token)).GroupBy(e=>NuvioKeys.Key(e.Item.ContentId,e.Item.Season,e.Item.Episode)).Select(g=>g.MaxBy(e=>e.Id)!).ToList();
                removed=store.RemoveNuvioViewings(events.Where(e=>e.Deleted).Select(e=>e.Item));
                rows=events.Where(e=>!e.Deleted).Select(e=>NuvioClient.Row(e.Item)).ToList();
                cursor=events.Count>0?events.Max(e=>e.Id):session.Cursor;
            }
            else
            {
                cursor=await client.Cursor(session.Profile,token);
                rows=(await client.PullWatched(session.Profile,token)).Select(NuvioClient.Row).ToList();
                if(!silent)_=ImportNuvioLibrary(client,session.Profile);
            }
            store.State.NuvioImportedAt=DateTimeOffset.UtcNow;store.Save();
            if(silent)
            {
                var importer=new HistoryImporter(store,discovery);var plan=await importer.Resolve("Nuvio",rows,null,CancellationToken.None);
                var added=importer.Apply(plan);MarkNuvio(plan);
                // The cursor moves only once the changes are kept; a lookup failure retries them next time.
                if(!plan.LookupFailed){session.Cursor=cursor;NuvioStorage.Save(store.DataDirectory,session);}
                if(added+removed>0){Status.Text=$"Nuvio: {added} new viewing{(added==1?"":"s")}{(removed>0?$", {removed} removed":"")}.";if(page=="History"&&!logging)Render();}
                return;
            }
            nuvioPendingCursor=cursor;
            importBusy=false;await ResolveImport("Nuvio",rows);
        }
        catch(OperationCanceledException)when(importCancel?.IsCancellationRequested==true){importBusy=false;importMessage="";Status.Text="Import cancelled.";if(page=="History")Render();}
        catch(Exception e)
        {
            store.Log(e);importBusy=false;importMessage="";
            if(e is NuvioExpiredException){store.State.NuvioConnected=false;store.Save();NuvioStorage.Forget(store.DataDirectory);}
            if(!silent||e is NuvioExpiredException)Status.Text=e is NuvioExpiredException?e.Message:"Nuvio couldn't be reached. Try again later.";
            if(page=="History"&&!silent)Render();
        }
    }
    // Everything that came from Nuvio is "known" there, so un-checking it in Noir removes it in Nuvio too.
    void MarkNuvio(ImportPlan plan)
    {
        var ids=new List<string>();
        foreach(var title in plan.Titles)
        {
            var group=store.FindDiscovered(title.Title);if(group==null)continue;
            var episodes=store.EpisodesFor(group);
            ids.AddRange(title.Views.Select(v=>episodes.FirstOrDefault(i=>title.Title.IsMovie?i.IsMovie:i.Season==v.Season&&i.Episode==v.Episode)?.Id).OfType<string>());
        }
        if(ids.Count>0)store.MarkNuvioKnown(ids);
    }
    // Nuvio's library (its saved titles) joins Noir's library; nothing is downloaded.
    async Task ImportNuvioLibrary(NuvioClient client,int profile)
    {
        try
        {
            var items=await client.PullLibrary(profile,CancellationToken.None);using var discovery=DiscoverClient();int added=0;
            foreach(var i in items.Where(i=>System.Text.RegularExpressions.Regex.IsMatch(i.ContentId,@"^tt\d+$")))
            {
                var title=new DiscoverTitle{Id=i.ContentId,Type=i.Type=="movie"?"movie":"series",Name=i.Name};
                if(store.FindDiscovered(title) is {} known){if(!store.LibraryGroups().Contains(known)){store.SetInLibrary(known,true);added++;}continue;}
                try{var details=await discovery.Details(title,CancellationToken.None);var group=store.AddDiscovered(details,false).FirstOrDefault()?.GroupKey;if(group!=null){store.SetInLibrary(group,true);added++;}}catch(Exception e)when(e is HttpRequestException or IOException or TaskCanceledException){}
            }
            if(added>0)Status.Text=$"Added {added} title{(added==1?"":"s")} from your Nuvio library.";
        }
        catch(Exception e){store.Log(e);}
    }
    void StartNuvio()
    {
        nuvioSync=new NuvioSync(store,()=>{if(page=="History"&&trackerTab=="Add history"&&!logging)RenderQuiet();});
        var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMinutes(2)};
        timer.Tick+=(_,_)=>{timer.Interval=TimeSpan.FromHours(4);if(store.State.NuvioAutoImport&&store.State.NuvioConnected&&!importBusy)_=ImportNuvio(true);};timer.Start();
        // Weekly automatic backup, checked shortly after start and then every few hours.
        var backups=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMinutes(3)};
        backups.Tick+=async(_,_)=>{backups.Interval=TimeSpan.FromHours(6);if(store.AutoBackupDue)try{await Task.Run(store.RunAutoBackup);}catch(Exception e){store.Log(e);}};backups.Start();
    }

    // ---- Where your history came from, recent imports, backups ----
    void RenderSources()
    {
        var sources=store.Sources();var batches=store.State.ImportBatches.AsEnumerable().Reverse().Take(8).ToList();
        if(sources.Count==0&&batches.Count==0)return;
        ContentPanel.Children.Add(Heading("Where your history came from","Undo a recent import, or remove everything one source added. Removals reach your phone on its next sync."));
        if(batches.Count>0)
        {
            ContentPanel.Children.Add(Text("RECENT IMPORTS",10.5,"#E4C084",FontWeights.SemiBold));
            foreach(var b in batches)
            {
                var row=new DockPanel{Margin=new(0,6,0,0),MaxWidth=760,HorizontalAlignment=HorizontalAlignment.Left};var id=b.Id;
                var undo=ActionButton("Undo",()=>{var n=store.UndoImport(id);if(lastImport==id)lastImport="";Status.Text=$"Removed {n:N0} viewing{(n==1?"":"s")} from that import.";RenderQuiet();});undo.Padding=new(14,6,14,6);DockPanel.SetDock(undo,Dock.Right);row.Children.Add(undo);
                row.Children.Add(Text($"{b.Service} · {b.Added:N0} viewing{(b.Added==1?"":"s")}{(b.RatedItemIds.Count>0?$" · {b.RatedItemIds.Count} ratings":"")} · {Ago(b.At)}",13.5,"#D9D4CC"));((FrameworkElement)row.Children[^1]).Width=560;
                ContentPanel.Children.Add(row);
            }
        }
        ContentPanel.Children.Add(Text("BY SOURCE",10.5,"#E4C084",FontWeights.SemiBold));((FrameworkElement)ContentPanel.Children[^1]).Margin=new(0,16,0,0);
        foreach(var s in sources)
        {
            var row=new DockPanel{Margin=new(0,6,0,0),MaxWidth=760,HorizontalAlignment=HorizontalAlignment.Left};var source=s.Source;
            var remove=ActionButton("Remove all…",()=>ConfirmRemoveSource(source,s.Viewings));remove.Padding=new(14,6,14,6);remove.Background=Brush("#26FFFFFF");DockPanel.SetDock(remove,Dock.Right);row.Children.Add(remove);
            row.Children.Add(Text($"{SourceLabel(source)} · {s.Viewings:N0} viewing{(s.Viewings==1?"":"s")} of {s.Titles:N0} item{(s.Titles==1?"":"s")}",13.5,"#D9D4CC"));
            ContentPanel.Children.Add(row);
        }
    }
    void ConfirmRemoveSource(string source,int count)
    {
        var body=SmartPopup();
        body.Children.Add(Text($"Remove {count:N0} viewing{(count==1?"":"s")} from “{SourceLabel(source)}”?",15,weight:FontWeights.SemiBold));
        body.Children.Add(Body("Titles stay in your library. A safety backup is saved first, and you can undo right after.",12));
        var row=new WrapPanel{Margin=new(0,10,0,0)};body.Children.Add(row);
        var go=ActionButton("Remove",()=>
        {
            CloseSmart();
            try{store.SafetyBackup("before-remove");}catch(Exception e){store.Log(e);Status.Text="Couldn't save a safety backup, so nothing was removed.";return;}
            var undo=store.RemoveSource(source);RenderQuiet();
            ShowUndo($"Removed {count:N0} viewings from {SourceLabel(source)}.",()=>{store.Restore(undo);RenderQuiet();});
        },true);go.Margin=new(0,0,8,0);row.Children.Add(go);
        row.Children.Add(ActionButton("Cancel",CloseSmart));
    }
    // A short-lived Undo button in the status bar area.
    void ShowUndo(string message,Action undo)
    {
        var body=SmartPopup();body.Children.Add(Text(message,14,weight:FontWeights.SemiBold));
        var b=ActionButton("Undo",()=>{CloseSmart();undo();},true);b.Margin=new(0,10,0,0);b.HorizontalAlignment=HorizontalAlignment.Left;body.Children.Add(b);
    }
    void RenderBackups()
    {
        ContentPanel.Children.Add(Heading("Back up & restore","Everything: history, ratings, notes, lists, library adds, settings and artwork. No passwords or sign-ins."));
        var row=new WrapPanel();ContentPanel.Children.Add(row);
        var save=ActionButton("⇩  Back up everything",async()=>
        {
            var dialog=new Microsoft.Win32.SaveFileDialog{Title="Save Noir backup",Filter="Noir backup|*"+LibraryStore.BackupExtension,FileName="Noir-"+DateTime.Now.ToString("yyyy-MM-dd")+LibraryStore.BackupExtension};
            if(dialog.ShowDialog(this)!=true)return;
            try{await Task.Run(()=>store.ExportBackup(dialog.FileName));Status.Text="Backup saved to "+Path.GetFileName(dialog.FileName)+".";}
            catch(Exception e){store.Log(e);Status.Text="Could not save the backup: "+e.Message;}
        },true);save.Padding=new(18,11,20,11);save.Margin=new(0,0,10,10);row.Children.Add(save);
        var restore=ActionButton("Restore from backup…",()=>
        {
            var dialog=new Microsoft.Win32.OpenFileDialog{Title="Restore a Noir backup",Filter="Noir backups (*.noirbackup;*.zip)|*.noirbackup;*.zip",InitialDirectory=store.AutoBackupFolder};
            if(dialog.ShowDialog(this)==true)_=PreviewRestore(dialog.FileName);
        });restore.Padding=new(18,11,20,11);restore.Margin=new(0,0,10,10);row.Children.Add(restore);
        var auto=new CheckBox{Content="Back up automatically every week (keeps the last 8)",IsChecked=store.State.AutoBackup,Margin=new(0,4,0,4)};auto.Click+=(_,_)=>{store.State.AutoBackup=auto.IsChecked==true;store.Save();};ContentPanel.Children.Add(auto);
        var folder=new WrapPanel();ContentPanel.Children.Add(folder);
        folder.Children.Add(Body((store.State.LastBackupAt is {} at?"Last automatic backup "+Ago(at):"No automatic backup yet")+" · "+store.AutoBackupFolder,12));
        var change=ActionButton("Change folder",()=>{var d=new Microsoft.Win32.OpenFolderDialog{Title="Where should automatic backups go?"};if(d.ShowDialog(this)==true){store.State.BackupFolder=d.FolderName;store.Save();RenderQuiet();}});change.Margin=new(10,0,0,0);change.Padding=new(12,4,12,4);folder.Children.Add(change);
    }
    async Task PreviewRestore(string path)
    {
        BackupPreview preview;
        try{preview=await Task.Run(()=>LibraryStore.ReadBackup(path));}
        catch(Exception e){store.Log(e);Status.Text=e is IOException?e.Message:"That backup couldn't be read.";return;}
        var body=SmartPopup();
        body.Children.Add(Text("Restore this backup?",17,weight:FontWeights.Bold));
        body.Children.Add(Body(preview.Summary,13));
        body.Children.Add(Body("Merge adds what's missing and keeps everything you have now; you can undo it. Replace makes Noir exactly like the backup and restarts (a safety backup is saved first). A paired phone gets the result on its next sync.",12));
        var row=new WrapPanel{Margin=new(0,12,0,0)};body.Children.Add(row);
        var merge=ActionButton("Merge",async()=>
        {
            CloseSmart();Status.Text="Merging backup…";
            try{await Task.Yield();var added=store.MergeBackup(preview);Status.Text=added==0?"Everything in that backup was already here.":$"Restored {added:N0} viewings from the backup.";trackerTab="Add history";Render();}
            catch(Exception e){store.Log(e);Status.Text="The backup couldn't be merged. Your history is unchanged.";}
        },true);merge.Margin=new(0,0,8,0);row.Children.Add(merge);
        var replace=ActionButton("Replace & restart",()=>
        {
            CloseSmart();
            try{store.StageReplace(preview);}catch(Exception e){store.Log(e);Status.Text="The backup couldn't be prepared. Nothing changed.";return;}
            var start=new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false};
            foreach(var arg in Environment.GetCommandLineArgs().Skip(1).Where(a=>a is not ("--minimized" or "--restarted")))start.ArgumentList.Add(arg);
            start.ArgumentList.Add("--restarted");System.Diagnostics.Process.Start(start);Application.Current.Shutdown();
        });replace.Margin=new(0,0,8,0);row.Children.Add(replace);
        row.Children.Add(ActionButton("Cancel",CloseSmart));
    }
}
