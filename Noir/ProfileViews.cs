using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Noir;
// Profile switcher (from the rail's profile card) and Settings → Profile management.
public partial class MainWindow
{
    ProfileList? profileList;
    DateTime profileStamp;
    ProfileList ProfileData {get{var stamp=File.GetLastWriteTimeUtc(Path.Combine(Profiles.Root,"profiles.json"));if(profileList==null||stamp!=profileStamp){profileList=Profiles.Load();profileStamp=stamp;}return profileList;}}
    NoirProfile? CurrentProfile=>Profiles.ForDirectory(ProfileData,store.DataDirectory);
    string ProfileName=>CurrentProfile?.Name??(Environment.UserName.Length>0?Environment.UserName:"Noir");
    NoirProfile DisplayProfile=>CurrentProfile??new(){Id="",Name=ProfileName};
    void SaveProfiles(){try{Profiles.Save(profileList??ProfileData);}catch(IOException e){store.Log(e);Status.Text=T("Profiles couldn't be saved.");}}

    void ShowProfileSwitcher()
    {
        profileList=null;
        var body=SmartPopup();body.MinWidth=280;
        body.Children.Add(Text(T("Who's watching?"),15,weight:FontWeights.SemiBold));
        var list=new StackPanel{Margin=new(0,10,0,6)};body.Children.Add(list);
        foreach(var profile in ProfileData.Profiles)
        {
            var p=profile;var on=p.Id==CurrentProfile?.Id;
            var row=new DockPanel();var avatar=Profiles.Avatar(p,34);DockPanel.SetDock(avatar,Dock.Left);row.Children.Add(avatar);
            var lines=new StackPanel{Margin=new(12,0,0,0),VerticalAlignment=VerticalAlignment.Center};row.Children.Add(lines);
            lines.Children.Add(Text(p.Name+(p.PinHash.Length>0?"  🔒":""),14,on?"#E4C084":"#F4F1EB",on?FontWeights.SemiBold:FontWeights.Normal));
            if(on)lines.Children.Add(Text(T("Watching now"),11,"#8F8A83"));
            var button=new Button{Content=row,Padding=new(10,7,10,7),Margin=new(0,0,0,4),Background=Brush(on?"#1CE4C084":"Transparent"),HorizontalContentAlignment=HorizontalAlignment.Stretch,Template=NavTemplate(),IsEnabled=!on};
            System.Windows.Automation.AutomationProperties.SetName(button,T("Switch to")+" "+p.Name);button.Click+=(_,_)=>{CloseSmart();SwitchProfile(p);};list.Children.Add(button);
        }
        var row2=new WrapPanel();body.Children.Add(row2);
        SmartButton(row2,"+ Add profile",()=>{CloseSmart();settingsSection="Profile";Navigate("Settings");addingProfile=true;Render();},true);
        SmartButton(row2,"Manage",()=>{CloseSmart();settingsSection="Profile";Navigate("Settings");});
        SmartButton(row2,"Your stats",()=>{CloseSmart();historyType="All";trackerTab="Stats";Navigate("History");});
    }
    // Saves everything, then restarts into the other profile's folder (asking its PIN first).
    void SwitchProfile(NoirProfile target)
    {
        if(target.PinHash.Length>0)AskPin(target,()=>DoSwitch(target));else DoSwitch(target);
    }
    void DoSwitch(NoirProfile target)
    {
        var busy=downloads?.Jobs().Count(j=>j.Status is "Queued" or "Preparing" or "Downloading" or "Subtitles")??0;
        if(busy>0&&!switchConfirmed)
        {
            var body=SmartPopup();
            body.Children.Add(Text(T("Switch to")+" "+target.Name+"?",15,weight:FontWeights.SemiBold));
            body.Children.Add(Body($"{busy} {T("downloads pause until you come back to this profile.")}",12));
            var row=new WrapPanel{Margin=new(0,8,0,0)};body.Children.Add(row);
            SmartButton(row,"Switch",()=>{CloseSmart();switchConfirmed=true;DoSwitch(target);},true);SmartButton(row,"Cancel",CloseSmart);
            return;
        }
        switchConfirmed=false;
        var list=Profiles.Load();var chosen=list.Profiles.FirstOrDefault(p=>p.Id==target.Id);if(chosen==null)return;
        list.Current=chosen.Id;chosen.LastUsed=DateTimeOffset.UtcNow;
        try{Profiles.Save(list);}catch(IOException e){store.Log(e);Status.Text=T("Profiles couldn't be saved.");return;}
        if(validationMode){Status.Text="Validation: would switch to "+chosen.Name;return;}
        store.Save();
        var start=new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false};start.ArgumentList.Add("--restarted");
        System.Diagnostics.Process.Start(start);
        quitting=true;Close();
    }
    bool switchConfirmed,addingProfile;
    void AskPin(NoirProfile profile,Action unlocked)
    {
        var body=SmartPopup();
        body.Children.Add(Text(T("Enter the PIN for")+" "+profile.Name,15,weight:FontWeights.SemiBold));
        var box=new PasswordBox{Width=200,Padding=new(10,7,10,7),Margin=new(0,10,0,6),HorizontalAlignment=HorizontalAlignment.Left,Background=Brush("#1C1C1F"),Foreground=Brush("#F4F1EB"),BorderBrush=Brush("#343437")};
        System.Windows.Automation.AutomationProperties.SetName(box,"PIN");body.Children.Add(box);
        var note=Text("",11.5,"#E5707A");body.Children.Add(note);
        void Try(){if(Profiles.CheckPin(profile,box.Password)){CloseSmart();unlocked();}else{note.Text=T("That PIN isn't right.");box.Clear();}}
        box.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter)Try();};
        var row=new WrapPanel{Margin=new(0,6,0,0)};body.Children.Add(row);SmartButton(row,"Unlock",Try,true);SmartButton(row,"Cancel",CloseSmart);
        Dispatcher.BeginInvoke(()=>box.Focus(),System.Windows.Threading.DispatcherPriority.Input);
    }

    // ---- Settings → Profile ----
    void RenderProfiles()
    {
        profileList=null;var current=CurrentProfile;
        ContentPanel.Children.Add(Heading("Profiles","Each profile is completely separate: its own library folders, history, settings, downloads and connections. Only one runs at a time."));
        var grid=Cards();
        foreach(var profile in ProfileData.Profiles)
        {
            var p=profile;var on=p.Id==current?.Id;
            var folder=Profiles.Folder(p)??Profiles.Root;
            grid.Children.Add(ConnectionCard(p.Name,p.Name.Length>0?p.Name[..1].ToUpperInvariant():"?",p.Color,"#16120C",on,on?T("Watching now"):p.LastUsed>DateTimeOffset.MinValue?T("last used")+" "+Ago(p.LastUsed):T("Not used yet"),folder,panel=>
            {
                var buttons=Buttons(panel);
                if(!on)buttons.Add("Switch",()=>SwitchProfile(p),true);
                buttons.Add("Edit",()=>{editingProfile=editingProfile==p.Id?"":p.Id;RenderQuiet();});
                if(p.Id!="default"&&!on)buttons.Add("Delete…",()=>ConfirmDeleteProfile(p));
                if(editingProfile==p.Id)ProfileEditor(panel,p,on);
            }));
        }
        if(addingProfile)grid.Children.Add(ConnectionCard(T("New profile"),"+","#2A2A2E","#E4C084",false,T("Starts empty"),"A fresh Noir: add its own folders, or use it just to track. Your current profile is unchanged.",panel=>
        {
            panel.Children.Add(Text(T("Name"),11,"#8F8A83"));
            var name=new TextBox{Padding=new(8,6,8,6),Margin=new(0,2,0,8),Background=Brush("#1C1C1F"),Foreground=Brush("#F4F1EB"),BorderBrush=Brush("#343437")};System.Windows.Automation.AutomationProperties.SetName(name,"Profile name");panel.Children.Add(name);
            var color=Profiles.Colors[ProfileData.Profiles.Count%Profiles.Colors.Length];
            var swatches=ColorRow(color,c=>color=c);panel.Children.Add(swatches);
            var buttons=Buttons(panel);
            buttons.Add("Create",()=>
            {
                if(name.Text.Trim().Length==0){Status.Text=T("Give the profile a name.");return;}
                try{var created=Profiles.Create(ProfileData,name.Text,color);addingProfile=false;profileList=null;Status.Text=T("Profile created. Switch to it whenever you like.");RenderQuiet();}
                catch(IOException e){store.Log(e);Status.Text=T("Profiles couldn't be saved.");}
            },true);
            buttons.Add("Cancel",()=>{addingProfile=false;RenderQuiet();});
            Dispatcher.BeginInvoke(()=>name.Focus(),System.Windows.Threading.DispatcherPriority.Input);
        }));
        else{var add=ActionButton("+ Add profile",()=>{addingProfile=true;RenderQuiet();});add.Margin=new(0,0,0,8);ContentPanel.Children.Add(add);}
        var ask=new CheckBox{Content=T("Ask who's watching when Noir starts"),IsChecked=ProfileData.AskOnStart,Margin=new(0,8,0,4)};
        ask.Click+=(_,_)=>{ProfileData.AskOnStart=ask.IsChecked==true;SaveProfiles();};ContentPanel.Children.Add(ask);
        ContentPanel.Children.Add(Body("Profiles with a PIN always ask for it. Phones pair with the profile that's open on this PC. Starting with Windows opens the last profile you used.",12));
    }
    string editingProfile="";
    WrapPanel ColorRow(string selected,Action<string> pick)
    {
        var row=new WrapPanel{Margin=new(0,0,0,8)};
        foreach(var c in Profiles.Colors)
        {
            var color=c;var dot=new Button{Width=26,Height=26,Margin=new(0,0,8,0),Padding=new(0),Background=Brush(c),BorderBrush=Brush(c==selected?"#F4F1EB":"#00000000"),BorderThickness=new(2),ToolTip=c};
            System.Windows.Automation.AutomationProperties.SetName(dot,"Colour "+c);
            dot.Click+=(_,_)=>{pick(color);foreach(Button b in row.Children)b.BorderBrush=Brush(b==dot?"#F4F1EB":"#00000000");};row.Children.Add(dot);
        }
        return row;
    }
    void ProfileEditor(StackPanel panel,NoirProfile p,bool on)
    {
        panel.Children.Add(Text(T("Name"),11,"#8F8A83"));
        var name=new TextBox{Text=p.Name,Padding=new(8,6,8,6),Margin=new(0,2,0,8),Background=Brush("#1C1C1F"),Foreground=Brush("#F4F1EB"),BorderBrush=Brush("#343437")};System.Windows.Automation.AutomationProperties.SetName(name,"Profile name");panel.Children.Add(name);
        var color=p.Color;panel.Children.Add(ColorRow(color,c=>color=c));
        panel.Children.Add(Text(T(p.PinHash.Length>0?"New PIN (leave empty to keep it)":"PIN (optional, 4+ digits)"),11,"#8F8A83"));
        var pin=new PasswordBox{Padding=new(8,6,8,6),Margin=new(0,2,0,8),Width=180,HorizontalAlignment=HorizontalAlignment.Left,Background=Brush("#1C1C1F"),Foreground=Brush("#F4F1EB"),BorderBrush=Brush("#343437")};System.Windows.Automation.AutomationProperties.SetName(pin,"PIN");panel.Children.Add(pin);
        var lockNote=Body("A PIN keeps others out of this profile in Noir. It isn't encryption: anyone using your Windows account can still open its files.",11.5);lockNote.Margin=new(0,0,0,8);panel.Children.Add(lockNote);
        var buttons=Buttons(panel);
        void Save(bool removePin)
        {
            if(name.Text.Trim().Length==0){Status.Text=T("Give the profile a name.");return;}
            if(pin.Password.Length is >0 and <4){Status.Text=T("Use at least 4 digits for the PIN.");return;}
            var target=ProfileData.Profiles.First(x=>x.Id==p.Id);target.Name=name.Text.Trim();target.Color=color;
            if(removePin)target.PinHash="";else if(pin.Password.Length>0)target.PinHash=Profiles.HashPin(target,pin.Password);
            SaveProfiles();editingProfile="";BuildRailFooter();RenderQuiet();Status.Text=T("Profile saved.");
        }
        // Changing someone else's PIN or name needs their PIN first.
        void Guarded(Action act){if(!on&&p.PinHash.Length>0)AskPin(p,act);else act();}
        buttons.Add("Save",()=>Guarded(()=>Save(false)),true);
        if(p.PinHash.Length>0)buttons.Add("Remove PIN",()=>Guarded(()=>Save(true)));
    }
    void ConfirmDeleteProfile(NoirProfile p)
    {
        void Ask()
        {
            var body=SmartPopup();
            body.Children.Add(Text(T("Delete")+" "+p.Name+"?",15,weight:FontWeights.SemiBold));
            body.Children.Add(Body("Its history, settings and connections go to the Recycle Bin. Video files in its folders are not touched.",12));
            var row=new WrapPanel{Margin=new(0,8,0,0)};body.Children.Add(row);
            SmartButton(row,"Delete",()=>
            {
                CloseSmart();
                try
                {
                    var folder=Profiles.Folder(p)??throw new IOException("The main profile can't be deleted.");
                    if(Profiles.ForDirectory(ProfileData,store.DataDirectory)?.Id==p.Id||!string.Equals(Path.GetDirectoryName(Path.GetFullPath(folder)),Path.GetFullPath(Path.Combine(Profiles.Root,"profiles")),StringComparison.OrdinalIgnoreCase))throw new IOException("This profile can't be deleted from here.");
                    if(Directory.Exists(folder))Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(folder,Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    ProfileData.Profiles.RemoveAll(x=>x.Id==p.Id);SaveProfiles();Status.Text=p.Name+" "+T("was moved to the Recycle Bin.");
                }
                catch(Exception e){store.Log(e);Status.Text=T("The profile couldn't be deleted.");}
                RenderQuiet();
            },true);
            SmartButton(row,"Cancel",CloseSmart);
        }
        if(p.PinHash.Length>0)AskPin(p,Ask);else Ask();
    }
}
