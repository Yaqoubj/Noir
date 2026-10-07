using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Noir;

// IMDb's public personal-use episode dataset supplies identifiers, not scraped reviews.
public static class EpisodeReviewIndex
{
    static readonly SemaphoreSlim gate=new(1,1);
    public static async Task<Dictionary<string,string>> Resolve(IEnumerable<VideoItem> videos,string directory,CancellationToken token=default)
    {
        var wanted=videos.Where(v=>!v.IsMovie && Regex.IsMatch(v.ImdbId,@"^tt\d+$") && v.EpisodeImdbId.Length==0)
            .GroupBy(v=>$"{v.ImdbId}:{v.Season}:{v.Episode}").ToDictionary(g=>g.Key,g=>g.Select(v=>v.Id).ToList());
        if(wanted.Count==0) return new();
        await gate.WaitAsync(token);
        try
        {
            var path=Path.Combine(directory,"imdb-episodes.tsv.gz");
            if(!File.Exists(path) || File.GetLastWriteTimeUtc(path)<DateTime.UtcNow.AddDays(-30))
            {
                using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(60)};
                using var response=await http.GetAsync("https://datasets.imdbws.com/title.episode.tsv.gz",HttpCompletionOption.ResponseHeadersRead,token);
                response.EnsureSuccessStatusCode();
                var temp=path+".tmp";
                try
                {
                    await using(var output=File.Create(temp)) await response.Content.CopyToAsync(output,token);
                    File.Move(temp,path,true);
                }
                finally {if(File.Exists(temp)) File.Delete(temp);}
            }
            return await Task.Run(()=>Read(path,wanted,token),token);
        }
        finally {gate.Release();}
    }
    static Dictionary<string,string> Read(string path,Dictionary<string,List<string>> wanted,CancellationToken token)
    {
        using var file=File.OpenRead(path);
        using var gzip=new GZipStream(file,CompressionMode.Decompress);
        using var reader=new StreamReader(gzip);
        var result=new Dictionary<string,string>();
        if(reader.ReadLine()!="tconst\tparentTconst\tseasonNumber\tepisodeNumber") throw new InvalidDataException("Unexpected IMDb episode index format.");
        string? line;
        while(wanted.Count>0 && (line=reader.ReadLine())!=null)
        {
            token.ThrowIfCancellationRequested();
            var parts=line.Split('\t');if(parts.Length!=4) continue;
            var key=$"{parts[1]}:{parts[2]}:{parts[3]}";
            if(wanted.Remove(key,out var ids) && Regex.IsMatch(parts[0],@"^tt\d+$")) foreach(var id in ids) result[id]=parts[0];
        }
        return result;
    }
}
