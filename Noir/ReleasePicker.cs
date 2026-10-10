using System.Windows;
using System.Windows.Controls;
namespace Noir;
public partial class MainWindow
{
    FrameworkElement ReleasePicker(List<DownloadChoice> choices,DownloadCredentials config,Action<DownloadChoice> choose)
    {
        foreach(var choice in choices)ReleasePresentation.Enrich(choice);
        var root=new StackPanel();var filters=new WrapPanel{Margin=new(0,12,0,8)};
        ComboBox Select(IEnumerable<string> options,string selected,string name)
        {
            var box=new ComboBox{ItemsSource=options.ToList(),SelectedItem=selected,MinWidth=145,Margin=new(0,0,10,8),Padding=new(10,7,10,7)};
            if(box.SelectedIndex<0)box.SelectedIndex=0;System.Windows.Automation.AutomationProperties.SetName(box,name);box.ToolTip=name;filters.Children.Add(box);return box;
        }
        var quality=Select(new[]{"All"}.Concat(choices.Select(c=>c.Resolution).Where(q=>q.Length>0).Distinct()),config.BrowseQuality,"Quality");
        var language=Select(new[]{"All"}.Concat(choices.SelectMany(c=>c.Languages.Split(new[]{" / "," + ",", "},StringSplitOptions.RemoveEmptyEntries)).Distinct()),config.BrowseLanguage,"Audio language");
        var ready=new CheckBox{Content="Ready on TorBox",IsChecked=config.BrowseReadyOnly,Margin=new(0,8,14,8)};filters.Children.Add(ready);
        var size=new CheckBox{Content="Within size preference",IsChecked=config.BrowseSizeLimit,IsEnabled=config.MaxEpisodeBytes>0,Margin=new(0,8,0,8)};filters.Children.Add(size);
        var reset=ActionButton("Reset filters",()=>{quality.SelectedItem="All";language.SelectedItem="All";ready.IsChecked=false;size.IsChecked=false;});reset.Margin=new(12,0,0,8);filters.Children.Add(reset);
        root.Children.Add(new Expander{Header="Filter releases",Content=filters,Margin=new(0,8,0,8)});
        var count=Body("",12);root.Children.Add(count);var list=new StackPanel();root.Children.Add(list);
        var expanded=new HashSet<string>();
        FrameworkElement Row(DownloadChoice release)
        {
            var content=new StackPanel{Margin=new(15)};
            var name=Text(release.Name,14,weight:FontWeights.SemiBold);name.TextTrimming=TextTrimming.CharacterEllipsis;name.MaxHeight=42;content.Children.Add(name);
            content.Children.Add(Body(ReleasePresentation.Summary(release),12));content.Children.Add(Body(release.ReleaseKind,11));
            var actions=new WrapPanel{Margin=new(0,10,0,0)};actions.Children.Add(ActionButton("Choose files",()=>choose(release),true));
            if(release.Recommendation.Length>0){var note=Body(release.Recommendation,11);note.Margin=new(12,10,0,0);actions.Children.Add(note);}content.Children.Add(actions);
            var detail=new StackPanel();detail.Children.Add(Body(string.Join(" · ",release.Sources),12));if(release.Detail.Length>0)detail.Children.Add(Body(release.Detail,12));if(release.Warning.Length>0)detail.Children.Add(Body(release.Warning,12));
            content.Children.Add(new Expander{Header=$"{Math.Max(1,release.SourceEntries)} source entries · details",Content=detail,Margin=new(0,6,0,0)});
            return new Border{Background=Brush("#18181B"),CornerRadius=new(10),BorderBrush=Brush("#2B2B2F"),BorderThickness=new(1),Child=content,Margin=new(0,0,0,10)};
        }
        void RenderChoices()
        {
            list.Children.Clear();var q=quality.SelectedItem as string??"All";var l=language.SelectedItem as string??"All";
            var visible=choices.Where(c=>ReleasePresentation.Matches(c,q,l,size.IsChecked==true?config.MaxEpisodeBytes:0,ready.IsChecked==true)).ToList();
            count.Text=$"{visible.Count} of {choices.Count} distinct releases · {choices.Sum(c=>Math.Max(1,c.SourceEntries))} source entries";
            if(visible.Count==0){list.Children.Add(Body("No choices match these filters. Reset filters to see every release."));return;}
            list.Children.Add(Heading("Suggested choices","Based on your quality and size preferences. Verify the files before downloading."));
            var suggestions=ReleasePresentation.Recommend(visible,config.PreferredResolution,config.MaxEpisodeBytes);var recommended=new Grid();
            for(int i=0;i<suggestions.Count;i++){recommended.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});var card=Row(suggestions[i]);card.Margin=new(0,0,i+1<suggestions.Count?10:0,10);Grid.SetColumn(card,i);recommended.Children.Add(card);}list.Children.Add(recommended);
            list.Children.Add(Heading("All releases","Expand a quality group to compare every version."));
            foreach(var group in ReleasePresentation.Group(visible,config.PreferredResolution))
            {
                var entries=new StackPanel();var expander=new Expander{Header=group.Label,Content=entries,IsExpanded=expanded.Contains(group.Quality),Margin=new(0,6,0,8)};list.Children.Add(expander);
                int shown=0;bool loaded=false;
                void More(){if(entries.Children.OfType<Button>().LastOrDefault() is {} old)entries.Children.Remove(old);foreach(var c in group.Choices.Skip(shown).Take(20))entries.Children.Add(Row(c));shown=Math.Min(shown+20,group.Choices.Count);if(shown<group.Choices.Count)entries.Children.Add(ActionButton($"Show 20 more · {group.Choices.Count-shown} remaining",More));}
                expander.Expanded+=(_,_)=>{expanded.Add(group.Quality);if(!loaded){loaded=true;More();}};expander.Collapsed+=(_,_)=>expanded.Remove(group.Quality);
                if(expander.IsExpanded){loaded=true;More();}
            }
        }
        void Change(){downloads?.SaveReleaseFilters(quality.SelectedItem as string??"All",language.SelectedItem as string??"All",ready.IsChecked==true,size.IsChecked==true);RenderChoices();}
        quality.SelectionChanged+=(_,_)=>Change();language.SelectionChanged+=(_,_)=>Change();ready.Checked+=(_,_)=>Change();ready.Unchecked+=(_,_)=>Change();size.Checked+=(_,_)=>Change();size.Unchecked+=(_,_)=>Change();RenderChoices();return root;
    }
}
