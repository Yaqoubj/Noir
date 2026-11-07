using Noir;
using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

static class RemoteDownloadDeleteChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        var dir=Path.GetFullPath(Path.Combine("work","downloads-round","p5","remote-delete-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(dir);
        try{
            var store=new LibraryStore(Path.Combine(dir,"profile"));var root=Path.Combine(dir,"movies");Directory.CreateDirectory(root);store.State.Roots=[new(){Path=root,Movies=true}];store.State.OnlineMetadata=false;
            var path=Path.Combine(root,"Finished.mkv");var other=Path.Combine(root,"Other.mkv");File.WriteAllBytes(path,new byte[123]);File.WriteAllBytes(other,new byte[77]);File.WriteAllText(Path.ChangeExtension(path,".en.srt"),"subtitle");File.WriteAllText(Path.ChangeExtension(path,".nfo"),"metadata");
            var item=new VideoItem{Id="finished-item",Title="Finished title",IsMovie=true,Path=path,Available=true};var recycleItem=new VideoItem{Id="recycle-item",Title="Recycle title",IsMovie=true,Path=other,Available=true};store.State.Catalog[item.Id]=new(){Item=item,Paths=[path]};store.State.Catalog[recycleItem.Id]=new(){Item=recycleItem,Paths=[other]};store.Mark(item,true);store.Save();
            var jobs=new List<PcDownloadJob>{new(){Id="finished",Target=item,Destination=path,Root=root,Status="Completed",Size=123},new(){Id="recycle",Target=recycleItem,Destination=other,Root=root,Status="Completed",Size=77}};
            foreach(var state in new[]{"Queued","Paused","Failed","Cancelled"})jobs.Add(new(){Id=state,Status=state,Target=item,Destination=path,Root=root});
            File.WriteAllText(Path.Combine(store.DataDirectory,"pc-downloads.json"),JsonSerializer.Serialize(jobs));using var downloads=new PcDownloads(store,start:false);
            DownloadDeleteChoice? answer=null;DownloadDeleteChoice seen=DownloadDeleteChoice.Ask;int prompts=0;bool recycled=false;downloads.ManageDeleter=(file,recycle)=>{recycled|=recycle;File.Delete(file);};
            await using var server=new MobileSyncServer(store,store.MobileSnapshot,store.MergeMobile,downloads:downloads,ephemeralCertificate:true,confirmDownloadDelete:(file,saved,ct)=>{prompts++;seen=saved;check(file.Item.Title.Length>0&&file.Bytes>0,"PC delete confirmation receives the resolved title and actual bytes");return Task.FromResult(answer);});
            server.Config.Enabled=true;var context=new DefaultHttpContext();
            Task<IResult> Delete(string id)=>server.DeleteFinishedDownload(id,context);
            int Status(IResult result)=>(result as IStatusCodeHttpResult)?.StatusCode??200;
            check(Status(await Delete("finished"))==401&&prompts==0,"Unpaired phones cannot request deletion or trigger a PC prompt");
            var token=new string('D',64);server.Config.Peers.Add(new(){Name="p5 phone",Hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)))});context.Request.Headers.Authorization="Bearer "+token;
            var queue=JsonSerializer.SerializeToElement(server.DownloadQueue());check(queue.GetProperty("capabilities").GetProperty("deleteFinished").GetBoolean(),"PC queue advertises deleteFinished when the PC confirmer is installed");
            check(Status(await Delete("unknown"))==404,"Unknown finished job ID returns 404");
            foreach(var state in new[]{"Queued","Paused","Failed","Cancelled"})check(Status(await Delete(state))==409,"Remote delete refuses "+state+" jobs");
            check(Status(await Delete(path))==404&&prompts==0,"A phone-supplied path cannot resolve a download or trigger confirmation");
            check(Status(await Delete("finished"))==409&&File.Exists(path)&&File.Exists(Path.ChangeExtension(path,".en.srt"))&&prompts==1&&seen==DownloadDeleteChoice.Ask,"Declined PC confirmation returns 409 and keeps the video and subtitles");
            context.Request.Headers["X-Noir-Profile"]=Guid.NewGuid().ToString();check(Status(await Delete("finished"))==409&&prompts==1,"A different profile cannot request deletion");context.Request.Headers.Remove("X-Noir-Profile");
            downloads.SetDeleteChoice(DownloadDeleteChoice.Permanent);answer=DownloadDeleteChoice.Permanent;var approved=await Delete("finished");var result=JsonSerializer.SerializeToElement(((IValueHttpResult)approved).Value);
            check(Status(approved)==200&&result.GetProperty("ok").GetBoolean()&&result.GetProperty("files").GetInt32()==1&&result.GetProperty("bytes").GetInt64()==123&&seen==DownloadDeleteChoice.Permanent,"Approved remote delete honors the saved choice and returns exact ok/files/bytes");
            check(!File.Exists(path)&&!File.Exists(Path.ChangeExtension(path,".en.srt"))&&!File.Exists(Path.ChangeExtension(path,".nfo"))&&File.Exists(other)&&!store.Archive.Single(i=>i.Id==item.Id).Available&&store.EverWatched(item)&&downloads.Jobs().All(j=>j.Id!="finished"),"DeleteLocal removes only the resolved video bundle and availability while retaining history");
            check(Status(await Delete("finished"))==404,"A deleted receipt cannot be deleted again");
            downloads.SetDeleteChoice(DownloadDeleteChoice.Recycle);answer=DownloadDeleteChoice.Recycle;check(Status(await Delete("recycle"))==200&&seen==DownloadDeleteChoice.Recycle&&recycled&&!File.Exists(other),"Remote recycle approval reaches DeleteLocal's recycle deleter");
            await using var closed=new MobileSyncServer(store,store.MobileSnapshot,store.MergeMobile,downloads:downloads,ephemeralCertificate:true);closed.Config.Enabled=true;closed.Config.Peers.Add(new(){Name="p5 phone",Hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)))});
            var closedQueue=JsonSerializer.SerializeToElement(closed.DownloadQueue());check(!closedQueue.GetProperty("capabilities").GetProperty("deleteFinished").GetBoolean()&&Status(await closed.DeleteFinishedDownload("unknown",context))==409,"Missing PC confirmer hides the capability and fails closed");
        }finally{Directory.Delete(dir,true);}
    }
}
