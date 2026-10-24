using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Noir;
// Removing a title: hide it from the library (history stays), optionally forget its history too,
// and optionally move its files to the Recycle Bin so a removal is never a permanent delete.
public partial class MainWindow
{
    void RemoveTitleDialog(VideoItem item)
    {
        var group=store.EpisodesFor(item.GroupKey);var title=item.Title;var files=store.FilesOf(item.GroupKey);
        var watched=group.Count(store.EverWatched);var size=files.Sum(f=>{try{return new FileInfo(f).Length;}catch(IOException){return 0L;}});
        var w=new Window{Title="Remove "+title,Owner=this,Width=520,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,Background=Brush("#0D0D0F"),Foreground=Brush("#F4F1EB"),WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var p=new StackPanel{Margin=new(28)};w.Content=p;
        p.Children.Add(Text("Remove “"+title+"”?",22,weight:FontWeights.SemiBold));
        p.Children.Add(Body($"It disappears from Home, {(item.IsMovie?"Movies":"Shows")} and My List. "+(watched>0?$"Your history ({watched} watched) stays in History unless you also forget it below.":"You can find it again in Discover."),13));
        var forget=new CheckBox{Content=watched>0?$"Also forget its watch history, ratings and progress":"Also forget saved progress and ratings",Margin=new(0,8,0,4)};p.Children.Add(forget);
        var recycle=new CheckBox{Content=$"Move its {files.Count(f=>!SubtitleDownloads.TextSubtitle(f))} video file{(files.Count==1?"":"s")} ({size/1073741824.0:0.#} GB) to the Recycle Bin",IsEnabled=files.Count>0,Margin=new(0,4,0,4)};p.Children.Add(recycle);
        if(files.Count==0)p.Children.Add(Body("No files for this title are on this PC.",11));
        else p.Children.Add(Body("Files go to the Recycle Bin, so you can still restore them from there.",11));
        var error=Body("",12);p.Children.Add(error);
        var row=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,10,0,0)};p.Children.Add(row);
        var cancel=ActionButton("Cancel",()=>w.Close());row.Children.Add(cancel);
        var remove=ActionButton("Remove",()=>
        {
            if(current!=null&&current.GroupKey==item.GroupKey){error.Text="Close the video before removing this title.";return;}
            var failed=0;
            if(recycle.IsChecked==true)foreach(var f in files)
                try{Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(f,Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);}
                catch(Exception e)when(e is IOException or UnauthorizedAccessException or OperationCanceledException){failed++;store.Log(e);}
            if(forget.IsChecked==true)store.ForgetTitle(item.GroupKey);else store.SetHidden(item.GroupKey,true);
            w.Close();detailKey=null;Render();
            Status.Text=$"Removed {title}"+(recycle.IsChecked==true?failed>0?$" · {failed} file{(failed==1?"":"s")} couldn't be moved to the Recycle Bin":" · files moved to the Recycle Bin":"")+(forget.IsChecked!=true&&watched>0?" · history kept":"");
        },true);
        remove.Margin=new(10,0,0,0);remove.Background=Brush("#C9483B");remove.Foreground=Brush("#FFFFFF");row.Children.Add(remove);
        w.ShowDialog();
    }
}
