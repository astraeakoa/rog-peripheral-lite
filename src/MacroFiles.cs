using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;
namespace GHelper;

// Parsers convert data only. Imported scripts are never executed.
public static class MacroFiles
{
    public static string Filter => L.Text("FileFilter");
    static readonly JsonSerializerOptions Json = new() { WriteIndented=true, PropertyNameCaseInsensitive=true, Converters={new JsonStringEnumConverter()} };
    record Envelope(int Version, MacroDefinition Macro);
    record Metadata(string Name, int Repeat, MacroTrigger Trigger, int Interval);
    public static MacroDefinition Read(string path)
    {
        if(new FileInfo(path).Length>1_048_576) throw new FormatException(L.Text("FileTooLarge"));
        return Parse(File.ReadAllText(path),Path.GetExtension(path),Path.GetFileNameWithoutExtension(path));
    }
    public static MacroDefinition Parse(string content,string extension,string? name=null)
    {
        if(content.Length>1_048_576) throw new FormatException(L.Text("FileTooLarge"));
        name ??= L.Text("ImportedName");
        MacroDefinition macro;
        if(extension.Equals(".json",StringComparison.OrdinalIgnoreCase))
        {
            using var doc=JsonDocument.Parse(content);
            if(doc.RootElement.EnumerateObject().Any(p=>p.Name.Equals("Version",StringComparison.OrdinalIgnoreCase)))
            {
                var envelope=JsonSerializer.Deserialize<Envelope>(content,Json) ?? throw new FormatException(L.Text("EmptyJson"));
                if(envelope.Version!=1) throw new FormatException(L.Text("JsonVersion"));
                macro=envelope.Macro;
            }
            else macro=JsonSerializer.Deserialize<MacroDefinition>(content,Json) ?? throw new FormatException(L.Text("EmptyJson"));
        }
        else if(extension.Equals(".csv",StringComparison.OrdinalIgnoreCase)) macro=ParseCsv(content,name);
        else if(extension.Equals(".ahk",StringComparison.OrdinalIgnoreCase)) macro=ParseAhk(content,name);
        else if(extension.Equals(".gmac",StringComparison.OrdinalIgnoreCase)) macro=GmacFile.Parse(content,name);
        else throw new FormatException(L.Text("FileTypes"));
        if(macro is null) throw new FormatException(L.Text("MissingMacro"));
        MacroStore.Validate(macro);return macro;
    }
    static Metadata GetMetadata(string content,string prefix,string name)
    {
        var line=content.Split('\n').FirstOrDefault(l=>l.TrimStart().StartsWith(prefix,StringComparison.Ordinal));
        return line==null?new(name,1,MacroTrigger.Once,50):JsonSerializer.Deserialize<Metadata>(line.Trim()[prefix.Length..],Json) ?? throw new FormatException(L.Text("InvalidMetadata"));
    }
    static MacroDefinition ParseCsv(string text,string name)
    {
        var meta=GetMetadata(text,"# ROGPeripheral ",name);
        using var parser=new TextFieldParser(new StringReader(text)) { TextFieldType=FieldType.Delimited, HasFieldsEnclosedInQuotes=true, CommentTokens=["#"] };
        parser.SetDelimiters(",");
        var header=parser.ReadFields();
        if(header==null||!header.Select(x=>x.Trim().ToLowerInvariant()).SequenceEqual(new[]{"action","key","milliseconds"})) throw new FormatException(L.Text("CsvHeader"));
        var steps=new List<MacroStep>();
        while(!parser.EndOfData)
        {
            long line=parser.LineNumber;var fields=parser.ReadFields();
            if(fields==null)break;
            if(fields.Length!=3||!int.TryParse(fields[2],out int delay)) throw new FormatException(L.Format("CsvRow", line));
            string action=fields[0].Trim().ToLowerInvariant() switch {"press"=>"Press","down"=>"Down","up"=>"Up","delay"=>"Delay",_=>throw new FormatException(L.Format("CsvAction", line))};
            steps.Add(new(action,action=="Delay"?"":NormalizeKey(fields[1]),delay));
        }
        return new(meta.Name,steps,meta.Repeat,meta.Trigger,meta.Interval);
    }
    static MacroDefinition ParseAhk(string text,string name)
    {
        var meta=GetMetadata(text,"; ROGPeripheral ",name);var steps=new List<MacroStep>();
        bool hotkey=false,block=false,closed=false;int number=0;
        foreach(var raw in text.Split('\n'))
        {
            number++;string line=raw.Trim();
            if(line.Length==0||line.StartsWith(';'))continue;
            if(line.StartsWith("#Requires AutoHotkey",StringComparison.OrdinalIgnoreCase))continue;
            if(Regex.IsMatch(line,@"^#SingleInstance\s+(Force|Ignore|Off)$",RegexOptions.IgnoreCase))continue;
            try
            {
                if(line.Contains("::"))
                {
                    if(hotkey||steps.Count>0)throw new FormatException(L.Text("OneHotkey"));
                    if(!Regex.IsMatch(line,@"^[\w!^+#<>*~$]+::\s*\{?$"))throw new FormatException(L.Text("HotkeyLine"));
                    hotkey=true;block=line.EndsWith('{');continue;
                }
                if(line=="{"&&hotkey&&!block&&!closed){block=true;continue;}
                if(line=="}"&&block&&!closed){closed=true;continue;}
                if(line.Equals("return",StringComparison.OrdinalIgnoreCase)&&!block&&!closed){closed=true;continue;}
                if(closed)throw new FormatException(L.Text("AfterEnd"));
                var sleep=Regex.Match(line,@"^Sleep(?:\s*,\s*(\d+)|\s*\(\s*(\d+)\s*\)|\s+(\d+))\s*(?:;.*)?$",RegexOptions.IgnoreCase);
                if(sleep.Success){steps.Add(new("Delay","",int.Parse(sleep.Groups.Cast<Group>().Skip(1).First(g=>g.Success).Value)));continue;}
                var send=Regex.Match(line,@"^(SendInput|SendEvent|Send)\b\s*(.*)$",RegexOptions.IgnoreCase);
                if(!send.Success)throw new FormatException(L.Text("AhkSubset"));
                string payload=send.Groups[2].Value.Trim();
                if(payload.StartsWith(','))payload=payload[1..].Trim();
                else
                {
                    if(payload.StartsWith('(')&&payload.EndsWith(')'))payload=payload[1..^1].Trim();
                    if(payload.Length<2||payload[0] is not ('\'' or '"')||payload[^1]!=payload[0])throw new FormatException(L.Text("AhkString"));
                    payload=payload[1..^1];
                }
                ParseSend(payload,steps);
                if(steps.Count>500)throw new FormatException(L.Text("TooManySteps"));
            }
            catch(Exception e) when(e is ArgumentException or FormatException or OverflowException){throw new FormatException(L.Format("AhkLine", number, e.Message),e);}
        }
        if(block&&!closed)throw new FormatException(L.Text("AhkBrace"));
        return new(meta.Name,steps,meta.Repeat,meta.Trigger,meta.Interval);
    }
    static void ParseSend(string text,List<MacroStep> steps)
    {
        int i=0;
        while(i<text.Length)
        {
            var mods=new List<string>();
            while(i<text.Length && "^!+#".Contains(text[i]))mods.Add(text[i++] switch {'^'=>"LControlKey",'!'=>"LMenu",'+'=>"LShiftKey",_=>"LWin"});
            if(i==text.Length)throw new FormatException(L.Text("MissingKey"));
            string key,action="Press";int count=1;
            if(text[i]=='{')
            {
                int end=text.IndexOf('}',i);if(end<0)throw new FormatException(L.Text("KeyBrace"));
                var parts=text[(i+1)..end].Split(' ',StringSplitOptions.RemoveEmptyEntries);i=end+1;
                if(parts.Length is <1 or >2)throw new FormatException(L.Text("KeyExpression"));
                key=NormalizeKey(parts[0]);
                if(parts.Length==2)
                {
                    if(parts[1].Equals("down",StringComparison.OrdinalIgnoreCase))action="Down";
                    else if(parts[1].Equals("up",StringComparison.OrdinalIgnoreCase))action="Up";
                    else if(!int.TryParse(parts[1],out count)||count is <1 or >100)throw new FormatException(L.Text("KeyOption"));
                }
            }
            else
            {
                char c=text[i++];
                if(c is >= 'a' and <= 'z')key=char.ToUpperInvariant(c).ToString();
                else if(c is >= 'A' and <= 'Z'){key=c.ToString();if(!mods.Contains("LShiftKey"))mods.Add("LShiftKey");}
                else if(c is >= '0' and <= '9')key="D"+c;
                else if(c==' ')key="Space";
                else throw new FormatException(L.Text("PlainText"));
            }
            if(action!="Press"&&mods.Count>0)throw new FormatException(L.Text("Modifiers"));
            foreach(var mod in mods)steps.Add(new("Down",mod));
            for(int n=0;n<count;n++)steps.Add(new(action,key));
            foreach(var mod in mods.AsEnumerable().Reverse())steps.Add(new("Up",mod));
        }
    }
    public static string NormalizeKey(string name)
    {
        if(Regex.IsMatch(name,@"^vk[0-9a-fA-F]{2}$")) return MacroStore.ParseKey(((Keys)Convert.ToInt32(name[2..],16)).ToString()).ToString();
        string normalized=name.Trim().ToLowerInvariant() switch
        {
            "ctrl" or "control" or "lctrl"=>"LControlKey", "rctrl"=>"RControlKey", "alt" or "lalt"=>"LMenu", "ralt"=>"RMenu", "shift" or "lshift"=>"LShiftKey", "rshift"=>"RShiftKey",
            "esc"=>"Escape", "return"=>"Enter", "del"=>"Delete", "ins"=>"Insert", "pgup"=>"PageUp", "pgdn"=>"PageDown", "bs" or "backspace"=>"Back", _=>name.Trim()
        };
        if(normalized.Length==1&&char.IsAsciiDigit(normalized[0]))normalized="D"+normalized;
        return MacroStore.ParseKey(normalized).ToString();
    }
    public static string Export(MacroDefinition macro,string extension)
    {
        MacroStore.Validate(macro);
        if(extension.Equals(".gmac",StringComparison.OrdinalIgnoreCase))return GmacFile.Export(macro);
        if(extension.Equals(".json",StringComparison.OrdinalIgnoreCase))return JsonSerializer.Serialize(new Envelope(1,macro),Json);
        string metadata=JsonSerializer.Serialize(new Metadata(macro.Name,macro.Repeat,macro.Trigger,macro.Interval),new JsonSerializerOptions(Json){WriteIndented=false});
        var result=new StringBuilder();
        if(extension.Equals(".csv",StringComparison.OrdinalIgnoreCase))
        {
            result.AppendLine("# ROGPeripheral "+metadata).AppendLine("Action,Key,Milliseconds");
            foreach(var s in macro.Steps)result.AppendLine($"{s.Action},{s.Key},{s.Milliseconds}");
        }
        else if(extension.Equals(".ahk",StringComparison.OrdinalIgnoreCase))
        {
            result.AppendLine("; ROGPeripheral "+metadata).AppendLine("; AHK v2 step sequence only. Repeat/trigger/interval are metadata for ROG Peripheral.").AppendLine("#Requires AutoHotkey v2.0");
            foreach(var s in macro.Steps)
            {
                if(s.Milliseconds>0)result.AppendLine($"Sleep {s.Milliseconds}");
                if(s.Action=="Delay")continue;
                var key=MacroStore.ParseKey(s.Key);string ahk=MacroStore.IsMouseButton(key)?key.ToString():$"vk{(int)key:X2}";
                result.AppendLine($"Send \"{{{ahk}{(s.Action=="Press"?"":" "+s.Action.ToLowerInvariant())}}}\"");
            }
        }
        else throw new FormatException(L.Text("ExportFormat"));
        return result.ToString();
    }
}
