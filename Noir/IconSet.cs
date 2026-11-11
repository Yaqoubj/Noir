using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace Noir;
// The drawn icon set for buttons (U58). A label may start or end with an icon token such as "[[play]]  Stream" or
// "Full page  [[chevron]]"; the token becomes a drawn icon next to the words, in the button's own text colour.
// Content, menu headers and accessible names are converted when they are set, so the accessible name stays the plain words.
public sealed class IconLabel : StackPanel
{
    public string Source{get;init;}="";
}
public static class IconSet
{
    // 24 x 24 grid, stroke 1.7, round joins and caps (the same grid as Icon(geometry,size,color) in Shell.cs).
    static readonly Dictionary<string,string> Paths=new()
    {
        ["play"]="M7,4.5 L19.5,12 L7,19.5 Z",
        ["download"]="M12,4 L12,16 M6.5,10.5 L12,16 L17.5,10.5 M5,20 L19,20",
        ["back"]="M20,12 L5,12 M11,6 L5,12 L11,18",
        ["forward"]="M4,12 L19,12 M13,6 L19,12 L13,18",
        ["plus"]="M12,5 L12,19 M5,12 L19,12",
        ["check"]="M5,12.5 L10,17.5 L19,7",
        ["close"]="M6,6 L18,18 M18,6 L6,18",
        ["heart"]="M12,20 C5,15 3,11.5 3,8.5 C3,6 5,4 7.5,4 C9.5,4 11,5 12,6.5 C13,5 14.5,4 16.5,4 C19,4 21,6 21,8.5 C21,11.5 19,15 12,20 Z",
        ["star"]="M12,3 L14.6,9 L21,9.6 L16.2,13.9 L17.6,20.3 L12,17 L6.4,20.3 L7.8,13.9 L3,9.6 L9.4,9 Z",
        ["sync"]="M20,12 A8,8 0 1 1 17.7,6.3 M20,4 L20,9 L15,9",
        ["pencil"]="M4,20 L4,16 L16,4 L20,8 L8,20 Z M13,7 L17,11",
        ["more"]="M6,12 m-1.7,0 a1.7,1.7 0 1,0 3.4,0 a1.7,1.7 0 1,0 -3.4,0 M12,12 m-1.7,0 a1.7,1.7 0 1,0 3.4,0 a1.7,1.7 0 1,0 -3.4,0 M18,12 m-1.7,0 a1.7,1.7 0 1,0 3.4,0 a1.7,1.7 0 1,0 -3.4,0",
        ["info"]="M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M12,11 L12,17 M12,7.4 L12,7.6",
        ["contrast"]="M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M12,3 L12,21 A9,9 0 0 0 12,3 Z",
        ["swap"]="M8,4 L8,20 M4,8 L8,4 L12,8 M16,20 L16,4 M12,16 L16,20 L20,16",
        ["chevron"]="M9,5 L16,12 L9,19",
        ["chevron-down"]="M5,9 L12,16 L19,9",
        ["sparkle"]="M12,3 L14,10 L21,12 L14,14 L12,21 L10,14 L3,12 L10,10 Z",
        ["external"]="M7,17 L17,7 M9,7 L17,7 L17,15",
        ["smile"]="M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M9,9.5 L9,9.6 M15,9.5 L15,9.6 M8,14 Q12,18.5 16,14",
        ["frown"]="M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M9,9.5 L9,9.6 M15,9.5 L15,9.6 M8,16.5 Q12,12 16,16.5",
        ["meh"]="M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M9,9.5 L9,9.6 M15,9.5 L15,9.6 M8.5,15.5 L15.5,15.5",
        ["list"]="M8,6 L20,6 M8,12 L20,12 M8,18 L20,18 M4,6 L4.1,6 M4,12 L4.1,12 M4,18 L4.1,18",
    };
    // These are drawn solid (the stroke and a fill in the same colour); the others are outlines.
    static readonly HashSet<string> Solid=["play","more","star-filled","heart-filled"];
    static readonly Dictionary<string,Geometry> Cache=[];
    static readonly Regex Token=new(@"\[\[([a-z-]+)\]\]",RegexOptions.Compiled);
    static bool registered;
    sealed class Scale(double factor):IValueConverter
    {
        public object Convert(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture)=>value is double d&&d>0?Math.Round(d*factor,1):16d;
        public object ConvertBack(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture)=>Binding.DoNothing;
    }
    static readonly Scale Size=new(1.12);

    public static bool Has(string? text)=>text!=null&&text.Contains("[[");
    // The words only: what the accessible name and tooltips say.
    public static string Plain(string? text)=>text==null?"":Token.Replace(text,"").Trim();
    // The token form of any content (for lookups): the original text of an icon label, or the string itself.
    public static string Src(object? content)=>content switch{IconLabel l=>l.Source,string s=>s,_=>""};
    public static bool IsIcon(object? content,string name)=>content is IconLabel{Source:var s}&&s.Trim()==$"[[{name}]]";

    static (string Key,string Geometry,bool Solid) Resolve(string name)
    {
        var key=name switch{"heart-filled"=>"heart","star-filled"=>"star",_=>name};
        return (key,Paths.TryGetValue(key,out var p)?p:Paths["more"],Solid.Contains(name));
    }
    // One drawn icon. Colour follows the owning control's Foreground (set size to fix it; 0 follows the font size).
    public static FrameworkElement Draw(string name,double size=0)
    {
        var (key,data,solid)=Resolve(name);
        if(!Cache.TryGetValue(key,out var geometry)){geometry=Geometry.Parse(data);geometry.Freeze();Cache[key]=geometry;}
        var path=new System.Windows.Shapes.Path{Data=geometry,StrokeThickness=1.7,StrokeLineJoin=PenLineJoin.Round,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,Stretch=Stretch.Uniform,VerticalAlignment=VerticalAlignment.Center,SnapsToDevicePixels=false};
        var owner=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(Control),1);
        path.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,new Binding("Foreground"){RelativeSource=owner});
        if(solid)path.SetBinding(System.Windows.Shapes.Shape.FillProperty,new Binding("Foreground"){RelativeSource=owner});
        if(key is not ("back" or "forward" or "chevron"))path.FlowDirection=FlowDirection.LeftToRight;/* A1-8: only arrows mirror in Arabic; play, tick, heart and the rest keep their shape */
        if(size>0){path.Width=path.Height=size;}
        else{path.SetBinding(FrameworkElement.WidthProperty,new Binding("FontSize"){RelativeSource=owner,Converter=Size});path.SetBinding(FrameworkElement.HeightProperty,new Binding("FontSize"){RelativeSource=owner,Converter=Size});}
        return path;
    }
    // The words and drawn icons of a label. Plain text without tokens stays a string.
    public static object Content(string? text)
    {
        if(!Has(text))return text??"";
        var panel=new IconLabel{Orientation=Orientation.Horizontal,Source=text!,VerticalAlignment=VerticalAlignment.Center};
        panel.SetBinding(FrameworkElement.HorizontalAlignmentProperty,new Binding("HorizontalContentAlignment"){RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(Control),1),FallbackValue=HorizontalAlignment.Center});
        var parts=Token.Split(text!);// text, name, text, name ...
        var items=new List<(bool Icon,string Value)>();
        for(var i=0;i<parts.Length;i++)
        {
            if(i%2==1)items.Add((true,parts[i]));
            else if(parts[i].Trim().Length>0)items.Add((false,parts[i].Trim()));
        }
        for(var i=0;i<items.Count;i++)
        {
            var (icon,value)=items[i];
            FrameworkElement element=icon?Draw(value):new TextBlock{Text=value,VerticalAlignment=VerticalAlignment.Center};
            var left=i>0&&!(icon&&items[i-1].Icon)?8:i>0?6:0;
            element.Margin=new Thickness(left,0,0,0);
            panel.Children.Add(element);
        }
        return panel;
    }

    static object CoerceContent(DependencyObject d,object value)
    {
        if(value is string s&&Has(s))
        {
            var plain=Plain(s);
            if(plain.Length>0&&string.IsNullOrEmpty(AutomationProperties.GetName(d)))AutomationProperties.SetName(d,plain);
            return Content(s);
        }
        return value;
    }
    static object CoerceName(DependencyObject d,object value)=>value is string s&&Has(s)?Plain(s):value;
    // Called once at startup, before any window exists. Each hook is tried on its own, and a Loaded fallback converts
    // anything the metadata hooks missed, so a label never shows its raw token.
    public static void Register()
    {
        if(registered)return;registered=true;
        foreach(var type in new[]{typeof(Button),typeof(ToggleButton)})
        {
            try{ContentControl.ContentProperty.OverrideMetadata(type,new FrameworkPropertyMetadata(null,null,CoerceContent));}catch(ArgumentException){}
            try{AutomationProperties.NameProperty.OverrideMetadata(type,new PropertyMetadata(string.Empty,null,CoerceName));}catch(ArgumentException){}
        }
        // MenuItem gets no metadata hook: WPF's own static constructor overrides Header for MenuItem, and a second
        // override from here makes that constructor throw, which breaks every menu. Its Loaded handler below converts
        // the header before the menu is drawn.
        EventManager.RegisterClassHandler(typeof(ButtonBase),FrameworkElement.LoadedEvent,new RoutedEventHandler((s,_)=>{if(s is ContentControl c){if(c.Content is string text&&Has(text))c.Content=CoerceContent(c,text);NameIconOnly(c);}}));
        EventManager.RegisterClassHandler(typeof(MenuItem),FrameworkElement.LoadedEvent,new RoutedEventHandler((s,_)=>{if(s is MenuItem m&&m.Header is string text&&Has(text))m.Header=CoerceContent(m,text);}));
        // Loaded is not raised again for an item whose menu was built and opened before; convert every header each time a menu or submenu opens.
        EventManager.RegisterClassHandler(typeof(ContextMenu),ContextMenu.OpenedEvent,new RoutedEventHandler((s,_)=>{if(s is ItemsControl c)ConvertHeaders(c);}));
        EventManager.RegisterClassHandler(typeof(MenuItem),MenuItem.SubmenuOpenedEvent,new RoutedEventHandler((s,_)=>{if(s is ItemsControl c)ConvertHeaders(c);}));
    }
    static void ConvertHeaders(ItemsControl menu)
    {
        foreach(var m in menu.Items.OfType<MenuItem>()){if(m.Header is string text&&Has(text))m.Header=CoerceContent(m,text);if(m.HasItems)ConvertHeaders(m);}
    }
    // An icon-only button gets its tooltip as its accessible name when nothing named it.
    static void NameIconOnly(ContentControl c)
    {
        if(c.Content is IconLabel l&&Plain(l.Source).Length==0&&string.IsNullOrWhiteSpace(AutomationProperties.GetName(c))&&c.ToolTip is string tip&&tip.Length>0)AutomationProperties.SetName(c,tip);
    }
}
