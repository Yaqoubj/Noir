using System.Windows;
using System.Windows.Controls;
namespace Noir;
public partial class MainWindow
{
    void OpenOrganizer()
    {
        if(current!=null||scanning||metadataRefreshing) {Status.Text="Finish playback or the library refresh before organizing files.";return;}
        var dialog=new Window {Title="Organize your files",Width=820,Height=650,Owner=this,ShowActivated=!validationMode,Background=Brush("#0D0D0F"),Foreground=Brush("#F4F1EB"),WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var panel=new StackPanel {Margin=new(28)};dialog.Content=new ScrollViewer {Content=panel};
        var plan=store.PlanOrganization();
        panel.Children.Add(Text("A tidy library",26,weight:FontWeights.SemiBold));
        panel.Children.Add(Body("Shows: Title (Year) / Season 01 / Title (Year) S01E01\nMovies: Title (Year) / Title (Year)\nMatching subtitles follow the video. Watch history and saved times stay attached.",13));
        panel.Children.Add(Heading($"{plan.Moves.Count(m=>m.Video)} videos ready to organize"));
        var message=Body("Original paths are kept for Undo. Nothing is overwritten or deleted.",12);panel.Children.Add(message);
        var controls=new WrapPanel();panel.Children.Add(controls);
        async Task Run(Func<int> operation)
        {
            if(current!=null||scanning||metadataRefreshing) {message.Text="Wait for playback or refresh to finish.";return;}
            controls.IsEnabled=false;StopWatchingFolders();
            try {var count=await Task.Run(operation);await ScanLibrary(false);message.Text=$"Finished · {count} videos. History and saved times kept.";}
            catch(Exception e) {store.Log(e);await ScanLibrary(false);message.Text="Stopped safely: "+e.Message+" · You can undo completed changes.";}
            finally {controls.IsEnabled=true;WatchLibraryFolders();}
        }
        var apply=ActionButton("Organize these files",async()=>await Run(()=>store.Organize(plan)),true);apply.IsEnabled=plan.Moves.Any();controls.Children.Add(apply);
        var undo=ActionButton("Undo last organization",async()=>await Run(()=>store.Organize(store.LastOrganization()??new(),true)));undo.Margin=new(12,0,0,0);controls.Children.Add(undo);
        var archive=ActionButton("Archive changes",()=>{store.ArchiveOrganization();message.Text="Change record archived. Close and reopen this preview to organize more files.";});archive.Margin=new(12,0,0,0);controls.Children.Add(archive);
        foreach(var move in plan.Moves.Where(m=>m.Video)) {panel.Children.Add(Body(move.From,11));panel.Children.Add(Text("→ "+move.To,12,"#E4C084"));}
        if(plan.Skipped.Count>0) {panel.Children.Add(Heading("Left as they are"));foreach(var skipped in plan.Skipped) panel.Children.Add(Body(skipped,12));}
        dialog.Show();
    }
}
