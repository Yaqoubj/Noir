using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
#if !NOIR_HEADLESS
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
#endif

namespace Noir;
// Profiles are fully separate Noir data folders: library, settings, downloads, history and
// connections. The default profile is ~/.noir itself; others live in ~/.noir/profiles/<id>.
// Only one runs at a time; switching saves and restarts into the other folder.
public sealed class NoirProfile
{
    public string Id {get;set;}="";
    public string Name {get;set;}="";
    public string Color {get;set;}="#E4C084";
    public string PinHash {get;set;}="";
    public DateTimeOffset LastUsed {get;set;}
}
public sealed class ProfileList
{
    public List<NoirProfile> Profiles {get;set;}=[];
    public string Current {get;set;}="default";
    public bool AskOnStart {get;set;}=true;
    [System.Text.Json.Serialization.JsonIgnore] internal ProfileList? Baseline {get;set;}
    [System.Text.Json.Serialization.JsonIgnore] public string? ReadFailurePath {get;internal set;}
}
public static class Profiles
{
    public static readonly string[] Colors=["#E4C084","#7AA7FF","#E5707A","#7BD88F","#B58CFF","#F0A35E","#5FD0C9"];
    // Validation runs point this at their profile copy so they never touch the real ~/.noir.
    public static string? RootOverride {get;set;}
    public static string Root=>RootOverride??ProfileLocation.Shared;
    static readonly object Gate=new();
    static readonly JsonSerializerOptions Json=new(){WriteIndented=true,PropertyNameCaseInsensitive=true};
    public static ProfileList Load(string? root=null)
    {
        var filePath=Path.GetFullPath(Path.Combine(root??Root,"profiles.json"));ProfileList? list=null;bool failed=false;
        // A read can collide with Save's File.Move (sharing violation for a moment): retry briefly before calling the file unreadable,
        // because an unreadable list blocks saves and makes the phone sync refuse PIN-protected profiles.
        for(int attempt=0;;attempt++)
        {
            try{list=JsonSerializer.Deserialize<ProfileList>(File.ReadAllText(filePath),Json);if(list?.Profiles==null)throw new JsonException("The profile list is empty.");break;}
            catch(Exception e)when(e is FileNotFoundException or DirectoryNotFoundException){break;}
            catch(Exception e)when(e is IOException or UnauthorizedAccessException&&attempt<8){Thread.Sleep(25);}
            catch(Exception e)when(e is IOException or JsonException or UnauthorizedAccessException){list=null;failed=true;break;}
        }
        list??=new();
        if(failed){list=new(){ReadFailurePath=filePath};}
        // Ids name folders (and a deleted profile's folder goes to the Recycle Bin), so anything odd is dropped.
        list.Profiles=list.Profiles.Where(p=>ValidId(p.Id)).DistinctBy(p=>p.Id).ToList();
        if(list.Profiles.All(p=>p.Id!="default"))list.Profiles.Insert(0,new(){Id="default",Name=Environment.UserName.Length>0?Environment.UserName:"Noir"});
        if(list.Profiles.All(p=>p.Id!=list.Current))list.Current="default";
        list.Baseline=CopyList(list);return list;
    }
    static ProfileList CopyList(ProfileList list)=>new(){Current=list.Current,AskOnStart=list.AskOnStart,Profiles=list.Profiles.Select(p=>new NoirProfile{Id=p.Id,Name=p.Name,Color=p.Color,PinHash=p.PinHash,LastUsed=p.LastUsed}).ToList()};
    public static void Save(ProfileList list,string? root=null)
    {
        lock(Gate){if(list.ReadFailurePath!=null)throw new IOException("The profile list could not be read; it was left untouched at "+list.ReadFailurePath);var folder=root??Root;var filePath=Path.Combine(folder,"profiles.json");
        var latest=Load(folder);if(latest.ReadFailurePath!=null){list.ReadFailurePath=latest.ReadFailurePath;throw new IOException("The profile list could not be read; it was left untouched at "+latest.ReadFailurePath);}Directory.CreateDirectory(folder);
        if(list.Baseline is {} before&&File.Exists(filePath))
        {
            var old=before.Profiles.ToDictionary(p=>p.Id);var edited=list.Profiles.ToDictionary(p=>p.Id);
            latest.Profiles.RemoveAll(p=>old.ContainsKey(p.Id)&&!edited.ContainsKey(p.Id));
            foreach(var p in list.Profiles)
            {
                var target=latest.Profiles.FirstOrDefault(x=>x.Id==p.Id);if(!old.TryGetValue(p.Id,out var original)){if(target==null)latest.Profiles.Add(p);continue;}if(target==null)continue;
                if(p.Name!=original.Name)target.Name=p.Name;if(p.Color!=original.Color)target.Color=p.Color;if(p.PinHash!=original.PinHash)target.PinHash=p.PinHash;if(p.LastUsed!=original.LastUsed)target.LastUsed=p.LastUsed;
            }
            if(list.Current!=before.Current)latest.Current=list.Current;if(list.AskOnStart!=before.AskOnStart)latest.AskOnStart=list.AskOnStart;
            list.Profiles=latest.Profiles;list.Current=latest.Current;list.AskOnStart=latest.AskOnStart;
        }
        File.WriteAllText(filePath+".tmp",JsonSerializer.Serialize(list,Json));File.Move(filePath+".tmp",filePath,true);list.Baseline=CopyList(list);}
    }
    // null means the default profile, which keeps ProfileLocation.Prepare's one-time legacy import.
    public static string? Folder(NoirProfile profile)=>profile.Id=="default"?null:ValidId(profile.Id)?Path.Combine(Root,"profiles",profile.Id):throw new IOException("This profile's folder name isn't valid.");
    public static bool ValidId(string id)=>id=="default"||System.Text.RegularExpressions.Regex.IsMatch(id,"^[a-z0-9]{1,32}$");
    public static bool IsProfileFolder(string directory)
    {
        var full=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        return string.Equals(full,Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)
            ||string.Equals(Path.GetDirectoryName(full),Path.Combine(Path.GetFullPath(Root),"profiles"),StringComparison.OrdinalIgnoreCase);
    }
    public static NoirProfile? ForDirectory(ProfileList list,string directory)=>list.Profiles.FirstOrDefault(p=>string.Equals(Path.GetFullPath(Folder(p)??Root).TrimEnd(Path.DirectorySeparatorChar),Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase));
    public static NoirProfile Create(ProfileList list,string name,string color,string? root=null)
    {
        var profile=new NoirProfile{Id=Guid.NewGuid().ToString("N")[..10],Name=name.Trim(),Color=color};
        Directory.CreateDirectory(Path.Combine(root??Root,"profiles",profile.Id));list.Profiles.Add(profile);Save(list,root);return profile;
    }
    // A convenience lock, not encryption: the profile's files stay readable to anyone using this Windows
    // account. PBKDF2 only slows guessing a short PIN from profiles.json; plain SHA-256 hashes from
    // earlier builds still unlock.
    public static string HashPin(NoirProfile profile,string pin)=>"pbkdf2:"+Convert.ToHexString(Rfc2898DeriveBytes.Pbkdf2(pin,Encoding.UTF8.GetBytes("noir-profile:"+profile.Id),200_000,HashAlgorithmName.SHA256,32));
    static string LegacyHash(NoirProfile profile,string pin)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("noir-profile:"+profile.Id+":"+pin)));
    public static bool CheckPin(NoirProfile profile,string pin)=>profile.PinHash.Length==0||CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(profile.PinHash.StartsWith("pbkdf2:")?HashPin(profile,pin):LegacyHash(profile,pin)),Encoding.ASCII.GetBytes(profile.PinHash));
    #if !NOIR_HEADLESS
    public static FrameworkElement Avatar(NoirProfile profile,double size)
    {
        var color=(Color)ColorConverter.ConvertFromString(profile.Color);
        var light=Color.FromArgb(255,(byte)Math.Min(255,color.R+40),(byte)Math.Min(255,color.G+40),(byte)Math.Min(255,color.B+40));
        var dark=Color.FromArgb(255,(byte)(color.R*.6),(byte)(color.G*.6),(byte)(color.B*.6));
        return new Border{Width=size,Height=size,CornerRadius=new(size/2),Background=new LinearGradientBrush(light,dark,45),
            Child=new TextBlock{Text=profile.Name.Length>0?profile.Name[..1].ToUpperInvariant():"?",FontSize=size*.42,FontWeight=FontWeights.Bold,Foreground=MainWindow.Brush("#16120C"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
    }
    // "Who's watching?" before the main window, when two or more profiles exist.
    public static NoirProfile? Pick(ProfileList list)
    {
        NoirProfile? chosen=null;
        var window=new Window{Title="Noir",Width=820,Height=520,WindowStartupLocation=WindowStartupLocation.CenterScreen,Background=MainWindow.Brush("#0B0B0D"),ResizeMode=ResizeMode.CanMinimize};
        var arabic=list.Profiles.Count>0&&ReadLanguage(list.Current)=="ar";
        var stack=new StackPanel{VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,FlowDirection=arabic?FlowDirection.RightToLeft:FlowDirection.LeftToRight};window.Content=stack;
        var title=MainWindow.Text(arabic?"من يشاهد؟":"Who's watching?",34,weight:FontWeights.Bold);title.HorizontalAlignment=HorizontalAlignment.Center;title.Margin=new(0,0,0,30);stack.Children.Add(title);
        var row=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Center};stack.Children.Add(row);
        var pinBox=new PasswordBox{Width=180,Padding=new(10,7,10,7),HorizontalAlignment=HorizontalAlignment.Center,Visibility=Visibility.Collapsed,Margin=new(0,24,0,0),Background=MainWindow.Brush("#1C1C1F"),Foreground=MainWindow.Brush("#F4F1EB"),BorderBrush=MainWindow.Brush("#343437")};
        var note=MainWindow.Text("",12.5,"#AAA6A0");note.HorizontalAlignment=HorizontalAlignment.Center;note.Margin=new(0,8,0,0);
        NoirProfile? waiting=null;
        void Choose(NoirProfile p)
        {
            if(p.PinHash.Length==0){chosen=p;window.Close();return;}
            waiting=p;pinBox.Visibility=Visibility.Visible;pinBox.Clear();pinBox.Focus();note.Text=(arabic?"أدخل رمز ":"Enter the PIN for ")+p.Name;
        }
        pinBox.KeyDown+=(_,e)=>{if(e.Key!=System.Windows.Input.Key.Enter||waiting==null)return;if(CheckPin(waiting,pinBox.Password)){chosen=waiting;window.Close();}else{note.Text=arabic?"الرمز غير صحيح":"That PIN isn't right.";pinBox.Clear();}};
        foreach(var profile in list.Profiles.OrderByDescending(p=>p.Id==list.Current).ThenByDescending(p=>p.LastUsed))
        {
            var p=profile;var tile=new StackPanel{Width=130};
            var avatar=Avatar(p,96);avatar.HorizontalAlignment=HorizontalAlignment.Center;tile.Children.Add(avatar);
            var name=MainWindow.Text(p.Name+(p.PinHash.Length>0?"  🔒":""),15,"#D9D4CC");name.HorizontalAlignment=HorizontalAlignment.Center;name.Margin=new(0,10,0,0);name.TextTrimming=TextTrimming.CharacterEllipsis;name.TextWrapping=TextWrapping.NoWrap;tile.Children.Add(name);
            var button=new Button{Content=tile,Padding=new(8),Margin=new(10),Background=Brushes.Transparent,BorderThickness=new(0),Cursor=System.Windows.Input.Cursors.Hand};
            System.Windows.Automation.AutomationProperties.SetName(button,p.Name);button.Click+=(_,_)=>Choose(p);row.Children.Add(button);
        }
        stack.Children.Add(pinBox);stack.Children.Add(note);
        window.ShowDialog();
        return chosen;
    }
    internal static string ReadLanguage(string id)
    {
        try
        {
            var list=Load();var profile=list.Profiles.FirstOrDefault(p=>p.Id==id);var folder=profile==null?Root:Folder(profile)??Root;
            using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"library.json")));
            return doc.RootElement.TryGetProperty("Language",out var language)?language.GetString()??"en":"en";
        }
        catch(Exception e)when(e is IOException or JsonException or UnauthorizedAccessException){return "en";}
    }
    #endif
}
