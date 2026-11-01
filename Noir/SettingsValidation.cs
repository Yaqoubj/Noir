#if NOIR_VALIDATE
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Noir;
public partial class MainWindow
{
    // --settings-only: every Settings section in the two-pane layout, Connections with a seeded
    // Trakt queue, Arabic mirroring, the narrow fallback and the Nuvio watch option.
    async Task ValidateSettings()
    {
        var results=new List<string>();
        void Check(bool ok,string label){if(!ok)throw new Exception(label);results.Add("PASS: "+label);}
        async Task Settle(int ms=450){UpdateLayout();await Task.Delay(ms);UpdateLayout();}
        void Shot(string name)=>CaptureLibrary(Path.Combine(store.DataDirectory,"settings-"+name+".png"));
        string Name(FrameworkElement e)=>System.Windows.Automation.AutomationProperties.GetName(e);
        try
        {
            WindowState=WindowState.Normal;Width=1320;Height=900;
            foreach(var (section,_) in SettingsSections)
            {
                settingsSection=section;Navigate("Settings");await Settle();
                var buttons=Descendants(ContentPanel).OfType<Button>().ToList();
                Check(SettingsSections.All(s=>buttons.Any(b=>Name(b)==s.Name)),$"{section}: the section list names all ten sections");
                Check(Descendants(ContentPanel).OfType<TextBlock>().Any(t=>t.Text==section&&t.FontSize==28),$"{section}: the right pane opens with its title");
                Check(Descendants(ContentPanel).OfType<FrameworkElement>().Count()>12,$"{section}: the section has content");
                Shot(section.ToLowerInvariant().Replace(" & ","-").Replace(' ','-'));
            }
            settingsSection="History";Navigate("Settings");await Settle();
            var afterWatching=ContentPanel;var ask=Descendants(afterWatching).OfType<ComboBox>().Single(c=>Name(c)=="After watching, ask how it was");var savedAsk=HistoryPrefs.AfterWatching;
            Check(ask.Items.Count==3&&Descendants(afterWatching).OfType<TextBlock>().Any(t=>t.Text=="After watching, ask how it was"),"Settings → History contains the after-watching row with all three choices");
            foreach(var (index,value) in new[]{(2,"Never"),(1,"Finales"),(0,"Always")}){ask.SelectedIndex=index;Check(HistorySettings.Load(store.DataDirectory).AfterWatching==value,"After-watching setting saves "+value+" per profile");}SetAfterWatching(savedAsk);
            settingsSection="Downloads";Navigate("Settings");await Settle();
            Check(!Descendants(ContentPanel).OfType<Expander>().Any(e=>e.Header as string=="Add Jellyfin / Emby"),"Downloads no longer carries the media-server form");
            // A waiting Trakt queue shows as one folded line with a count, and on the Connections badge.
            var episodes=store.Archive.Where(i=>!i.IsMovie&&i.Id.Length>0).Take(3).ToList();
            store.State.SyncTrakt=true;
            foreach(var i in episodes)store.State.WatchSync["Trakt:"+i.Id]=new(){Provider="Trakt",ItemId=i.Id,WatchedAt=DateTimeOffset.UtcNow,RetryAt=DateTimeOffset.MaxValue,Message="Waiting to sync"};
            var waiting=store.State.WatchSync.Values.Count(j=>j.Provider=="Trakt"&&j.SyncedAt==null);
            settingsSection="Connections";Navigate("Settings");await Settle();
            var names=Descendants(ContentPanel).OfType<Border>().Select(Name).Where(n=>n.Length>0).ToHashSet();
            foreach(var card in new[]{"Trakt","Serializd","Nuvio","Stremio","Kodi","IMDb","Netflix","Letterboxd","TV Time","TorBox","Real-Debrid","AIOStreams"})Check(names.Contains(card),$"Connections has a {card} card");
            var queue=Descendants(ContentPanel).OfType<Expander>().FirstOrDefault(e=>e.Header is TextBlock t&&t.Text.StartsWith($"{waiting} waiting"));
            Check(queue!=null&&!queue.IsExpanded,"The Trakt queue is one folded line with its count");
            Check(Descendants(ContentPanel).OfType<TextBlock>().Any(t=>t.Text==PendingSync().ToString()),"The Connections entry shows the waiting count");
            Shot("connections-queue-folded");
            double Top(string card){var b=Descendants(ContentPanel).OfType<Border>().First(x=>Name(x)==card);return b.TransformToAncestor(ContentPanel).Transform(new Point()).Y;}
            Check(Math.Abs(Top("Trakt")-Top("Serializd"))<1,"Cards sit two to a row at 1320 px");
            Width=1920;Height=1080;Render();await Settle();var wrap=Descendants(ContentPanel).OfType<WrapPanel>().First(w=>w.Children.OfType<Border>().Any(b=>Name(b)=="Trakt"));var perRow=wrap.Children.OfType<FrameworkElement>().Count(c=>Math.Abs(c.TransformToAncestor(wrap).Transform(new Point()).Y)<1);
            Check(perRow==Math.Max(1,(int)(wrap.ActualWidth/346)),$"A wide window fits {perRow} cards per row in {wrap.ActualWidth:0} px");Shot("connections-1920");Width=1320;Height=900;Render();await Settle();
            queue=Descendants(ContentPanel).OfType<Expander>().First(e=>e.Header is TextBlock t&&t.Text.StartsWith($"{waiting} waiting"));
            queue.IsExpanded=true;await Settle();
            var listed=Descendants((DependencyObject)queue.Content).OfType<TextBlock>().Select(t=>t.Text).ToList();
            Check(listed.Any(t=>t.StartsWith(episodes[0].Label)),"Opening the queue lists each waiting episode · looked for "+episodes[0].Label+" in: "+string.Join(" | ",listed.Take(12)));
            ContentScroll.ScrollToVerticalOffset(0);await Settle();Shot("connections-queue-open");
            var skip=Descendants(ContentPanel).OfType<Button>().First(b=>b.Content as string=="✕");skip.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));await Settle();
            Check(store.State.WatchSync.Values.Count(j=>j.Provider=="Trakt"&&j.SyncedAt==null)==waiting-1,"✕ drops one waiting watch");
            Check(Descendants(ContentPanel).OfType<Expander>().Any(e=>e.IsExpanded&&e.Header is TextBlock t&&t.Text.StartsWith($"{waiting-1} waiting")),"The queue stays open after a change");
            ContentScroll.ScrollToVerticalOffset(900);await Settle();Shot("connections-servers");
            ContentScroll.ScrollToVerticalOffset(1800);await Settle();Shot("connections-imports");
            // Arabic mirrors the two panes.
            SetLanguage("ar");settingsSection="Connections";Navigate("Settings");await Settle();
            Check(LibraryRoot.FlowDirection==FlowDirection.RightToLeft,"Arabic mirrors Settings");
            Check(Descendants(ContentPanel).OfType<TextBlock>().Any(t=>t.Text=="الاتصالات"&&t.FontSize==28),"Arabic Settings shows the translated section title");
            Shot("connections-ar");
            settingsSection="Appearance & language";Navigate("Settings");await Settle();Shot("appearance-ar");
            SetLanguage("en");
            // Narrow windows fall back to a chip row.
            Width=900;Height=760;settingsSection="Connections";Navigate("Settings");await Settle(700);Render();await Settle();
            Check(ContentScroll.ActualWidth<820&&Descendants(ContentPanel).OfType<Button>().Any(b=>b.Content as string=="Profile"),"Narrow windows list sections as chips");Shot("connections-900");
            Width=1320;Height=900;
            // Side rail preference is saved and applied.
            store.State.RailMode="Icons";ApplyRailWidth();Check(railCompact,"Icons only folds the rail");store.State.RailMode="Auto";ApplyRailWidth();
            // Nuvio watch option beside Stremio.
            var watch=WatchOptions("tt0944947",false,"Game of Thrones");
            Check(Descendants(watch).OfType<Button>().Any(b=>b.Content as string=="▶  Stream in Nuvio"),"Where to watch offers Stream in Nuvio");
            using(var handler=Microsoft.Win32.Registry.ClassesRoot.OpenSubKey("nuvio"))
                if(handler==null){OpenInNuvio("tt0944947",false,"Game of Thrones");await Settle();Check(smartPopup?.IsOpen==true,"Without Nuvio Desktop, Stream in Nuvio shows a QR code for the phone");CloseSmart();}
                else results.Add("SKIP: Nuvio Desktop is installed; not opening it during validation");
            foreach(var i in episodes)store.State.WatchSync.Remove("Trakt:"+i.Id);
            // Profiles: stored beside the profile copy, a new one gets its own folder, PINs gate it.
            Check(Profiles.Root==Path.GetFullPath(store.DataDirectory),"Validation keeps profiles inside the profile copy");
            Check(CurrentProfile?.Id=="default","The open folder is the default profile");
            settingsSection="Profile";Navigate("Settings");addingProfile=true;Render();await Settle();
            var nameBox=Descendants(ContentPanel).OfType<TextBox>().First(b=>Name(b)=="Profile name");nameBox.Text="Guest";
            Descendants(ContentPanel).OfType<Button>().First(b=>b.Content as string=="Create").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));await Settle();
            var guest=Profiles.Load().Profiles.FirstOrDefault(p=>p.Name=="Guest");
            Check(guest!=null&&Directory.Exists(Profiles.Folder(guest)!),"Add profile creates a separate data folder");
            Check(Descendants(ContentPanel).OfType<Border>().Any(b=>Name(b)=="Guest"),"The new profile is listed");Shot("profiles");
            guest!.PinHash=Profiles.HashPin(guest,"1234");var saved=Profiles.Load();saved.Profiles.First(p=>p.Id==guest.Id).PinHash=guest.PinHash;Profiles.Save(saved);
            Check(Profiles.CheckPin(guest,"1234")&&!Profiles.CheckPin(guest,"9999"),"PINs are checked");
            DoSwitch(guest);Check(Profiles.Load().Current==guest.Id,"Switching records the profile to open next");
            var reset=Profiles.Load();reset.Current="default";Profiles.Save(reset);
            ShowProfileSwitcher();await Settle();Check(smartPopup?.IsOpen==true&&Descendants(((Border)smartPopup.Child).Child).OfType<Button>().Any(b=>Name(b)=="Switch to Guest"),"The rail's profile card opens the switcher");
            Shot("profile-switcher");CloseSmart();
        }
        catch(Exception error){results.Add("FAIL: "+error);}
        File.WriteAllText(Path.Combine(store.DataDirectory,"settings-checks.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
    }
}
#endif
