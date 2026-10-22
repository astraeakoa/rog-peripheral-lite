using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace GHelper;

// Armoury Crate text GMAC subset, based on an exported user sample.
// The final "1" is preserved as an opaque marker, never interpreted as a repeat count.
internal static class GmacFile
{
    [DllImport("user32.dll", EntryPoint="MapVirtualKeyW")]
    static extern uint MapVirtualKey(uint code,uint type);

    static (string Name,int Scan,int Code) EncodeKey(Keys key)
    {
        if(key==Keys.LButton)return ("Left Click",301,301);
        if(key==Keys.RButton)return ("Right Click",302,302);
        // Other GMAC mouse encodings have not been established by the supplied sample.
        if(MacroStore.IsMouseButton(key))throw new FormatException(L.Text("GmacKeyUnsupported"));
        uint scan=MapVirtualKey((uint)key,4);
        // Extended scan encodings / media keys are not established by this sample either.
        if(scan==0 || scan>127)throw new FormatException(L.Text("GmacKeyUnsupported"));
        string label=key is >= Keys.D0 and <= Keys.D9 ? ((int)key-(int)Keys.D0).ToString(CultureInfo.InvariantCulture) : key.ToString();
        return (label,(int)scan,(int)key);
    }

    public static MacroDefinition Parse(string content,string name)
    {
        var lines=content.TrimStart('\uFEFF').Split('\n').Select(s=>s.Trim()).ToList();
        while(lines.Count>0&&lines[^1].Length==0)lines.RemoveAt(lines.Count-1);
        if(lines.Count<2||lines[^1]!="1")throw new FormatException(L.Text("GmacMarker"));
        var steps=new List<MacroStep>();
        for(int i=0;i<lines.Count-1;i++)
        {
            try
            {
                string[] fields=lines[i].Split(',').Select(s=>s.Trim()).ToArray();
                if(fields.Length==2&&fields[0]=="Delay")
                {
                    int ms=Number(fields[1]);
                    if(ms is <0 or >60000)throw new FormatException(L.Text("GmacRow"));
                    // Zero wait is valid in GMAC; it has no execution effect.
                    if(ms!=0)steps.Add(new("Delay","",ms));
                }
                else if(fields.Length==4&&fields[0] is "Press" or "Release")
                {
                    int scan=Number(fields[2]),code=Number(fields[3]);
                    Keys key;
                    if(code==301&&scan==301&&fields[1]=="Left Click")key=Keys.LButton;
                    else if(code==302&&scan==302&&fields[1]=="Right Click")key=Keys.RButton;
                    else
                    {
                        if(code<8||code>254)throw new FormatException(L.Text("GmacKeyUnsupported"));
                        key=MacroStore.ParseKey(((Keys)code).ToString());
                        var expected=EncodeKey(key);
                        if(scan!=expected.Scan || MacroFiles.NormalizeKey(fields[1])!=key.ToString())
                            throw new FormatException(L.Text("GmacKeyMismatch"));
                    }
                    steps.Add(new(fields[0]=="Press"?"Down":"Up",key.ToString()));
                }
                else throw new FormatException(L.Text("GmacRow"));
                if(steps.Count>500)throw new FormatException(L.Text("TooManySteps"));
            }
            catch(Exception e) when(e is FormatException or ArgumentException or OverflowException)
            {throw new FormatException(L.Format("GmacLine",i+1,e.Message),e);}
        }
        var macro=new MacroDefinition(name,steps);
        MacroStore.Validate(macro);
        return macro;
    }

    static int Number(string text) => int.Parse(text,NumberStyles.None,CultureInfo.InvariantCulture);

    public static string Export(MacroDefinition macro)
    {
        // GMAC only stores the sequence. Playback mode/repeats stay in the application.
        var result=new StringBuilder();
        int events=0;
        foreach(var step in macro.Steps)
        {
            if(step.Milliseconds>0) {result.Append("Delay,").Append(step.Milliseconds.ToString(CultureInfo.InvariantCulture)).Append("\r\n");events++;}
            if(step.Action=="Delay")continue;
            var key=EncodeKey(MacroStore.ParseKey(step.Key));
            string suffix=$",{key.Name},{key.Scan.ToString(CultureInfo.InvariantCulture)},{key.Code.ToString(CultureInfo.InvariantCulture)}\r\n";
            if(step.Action!="Up"){result.Append("Press").Append(suffix);events++;}
            if(step.Action!="Down"){result.Append("Release").Append(suffix);events++;}
        }
        if(events>500)throw new FormatException(L.Text("TooManySteps"));
        return result.Append("1\r\n").ToString();
    }
}
