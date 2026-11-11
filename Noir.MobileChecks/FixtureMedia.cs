using System.Diagnostics;
using System.Security.Cryptography;
static class FixtureMedia
{
    public static string Ensure(string root,string fixture)
    {
        var recipe=Path.Combine(Directory.GetCurrentDirectory(),"tools","test-media","generate_media.py");
        if(!File.Exists(recipe))throw new Exception("Run Noir.MobileChecks from the repository root; missing tools/test-media/generate_media.py");
        var hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(recipe))).ToLowerInvariant();
        var cache=Path.Combine(root,"subtitle-fixture");var names=new[]{"Subtitle surface fixture.mkv","Bright pattern fixture.mp4"};
        // Check ffmpeg even on cache hits: a missing prerequisite must never silently remove a test.
        Run("ffmpeg",["-version"],false);
        if(!File.Exists(Path.Combine(cache,"recipe.sha256"))||File.ReadAllText(Path.Combine(cache,"recipe.sha256")).Trim()!=hash||names.Any(n=>!File.Exists(Path.Combine(cache,n))))
        {Console.WriteLine("Generating subtitle and bright pattern fixtures: "+cache);Run("python",[recipe,cache],true);}
        else Console.WriteLine("Reusing generated media; recipe SHA256 "+hash);
        var target=Path.Combine(fixture,"generated-media");Directory.CreateDirectory(target);
        foreach(var name in names){File.Copy(Path.Combine(cache,name),Path.Combine(target,name));Console.WriteLine("Registered fixture media: "+Path.GetFileNameWithoutExtension(name)+" ("+new FileInfo(Path.Combine(target,name)).Length+" bytes)");}
        return target;
    }
    static void Run(string exe,string[] args,bool output)
    {
        var info=new ProcessStartInfo(exe){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};foreach(var arg in args)info.ArgumentList.Add(arg);
        try{using var p=Process.Start(info)??throw new Exception("Could not start "+exe);var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();p.WaitForExit();Task.WaitAll(stdout,stderr);if(p.ExitCode!=0)throw new Exception(stderr.Result+stdout.Result);if(output)Console.Write(stdout.Result);}
        catch(Exception e){throw new Exception("Required test-media tool "+exe+" failed. Install Python 3 and ffmpeg (libx264/libx265/aac/eac3/srt/ass) on PATH. "+e.Message,e);}
    }
}
