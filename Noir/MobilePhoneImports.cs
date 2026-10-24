using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Noir;
public sealed partial class LibraryStore
{
    // Called under the library gate. Identity-only imports never accept paths or account settings.
    VideoItem? PhoneDiscoveredItem(MobileOperation op)
    {
        if(!Regex.IsMatch(op.Imdb,@"\Att\d{1,16}\z")||op.Title.Trim().Length is <1 or >250||op.Year is <0 or >2200||op.Season is <0 or >1000||op.Episode is <0 or >10000)return null;
        var type=op.Movie?"movie":"series";var group=(op.Movie?"movie:":"show:")+op.Imdb;
        var placeholder="episode-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{group}:{op.Season}:{op.Episode}")));
        if(op.ItemId!="discover-"+type+"-"+op.Imdb&&(op.Movie||op.ItemId!=placeholder)||!op.Movie&&(op.Season<1||op.Episode<1))return null;
        var known=Archive.FirstOrDefault(i=>i.ImdbId==op.Imdb&&i.IsMovie==op.Movie);
        var same=known==null?null:EpisodesFor(known.GroupKey).FirstOrDefault(i=>i.IsMovie==op.Movie&&(op.Movie||i.Season==op.Season&&i.Episode==op.Episode));
        if(same!=null)return same;
        var id=State.Catalog.ContainsKey(op.ItemId)?placeholder:op.ItemId;
        return Remember(new(){Id=id,CollectionKey=known?.GroupKey??group,Title=known?.Title??op.Title.Trim(),Year=known?.Year??op.Year,ImdbId=op.Imdb,IsMovie=op.Movie,Season=op.Movie?0:op.Season,Episode=op.Movie?0:op.Episode,Available=false}).Item;
    }
}
