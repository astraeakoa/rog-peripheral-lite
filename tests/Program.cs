using GHelper;
using GHelper.Peripherals.Mouse.Models;
using System.Diagnostics;
using System.Windows.Forms;

Environment.SetEnvironmentVariable("ROG_PERIPHERAL_CONFIG_DIR",Path.Combine(Path.GetTempPath(),"ROGPeripheral-tests-"+Guid.NewGuid()));
int passed=0;
void Check(bool condition,string name){if(!condition)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);passed++;}
void Invalid(MacroDefinition m,string name){try{MacroStore.Validate(m);}catch(ArgumentException){Check(true,name);return;}throw new Exception("Accepted invalid macro: "+name);}
MacroDefinition Def(params MacroStep[] steps)=>new("test",steps.ToList());
Invalid(new MacroDefinition("null-step",[null!]),"Null step rejected");
Invalid(Def(new MacroStep("Up","A")),"Unmatched release rejected");
Invalid(Def(new MacroStep("Down","A")),"Unreleased key rejected");
Invalid(Def(new MacroStep("Down","A"),new MacroStep("Down","A"),new MacroStep("Up","A")),"Duplicate down rejected");
Invalid(Def(new MacroStep("Press","F13")),"Recursive macro trigger rejected");
Invalid(Def(new MacroStep("Press","Cancel")),"Unsupported low virtual key rejected");
Invalid(Def(new MacroStep("Delay","",-1)),"Negative delay rejected");
Invalid(Def(new MacroStep("Press","NotAKey")),"Unknown key rejected");
Invalid(new("test",[new MacroStep("Delay","",60000)],100),"Excessive total duration rejected");
var output=new Output();var runner=new MacroRunner(output);
await runner.Run(new("copy",[new MacroStep("Down","LControlKey"),new MacroStep("Press","C"),new MacroStep("Up","LControlKey")],2));
Check(output.Events.SequenceEqual(new[]{"LControlKey:False","C:False","C:True","LControlKey:True","LControlKey:False","C:False","C:True","LControlKey:True"}),"Sequence and repeat order");
output.Events.Clear();
var running=runner.Run(Def(new MacroStep("Down","A"),new MacroStep("Delay","",60000),new MacroStep("Up","A")));
await Task.Delay(30); await runner.Run(Def(new MacroStep("Press","B")));runner.Stop();await running;
Check(output.Events.SequenceEqual(new[]{"A:False","A:True"}),"Cancel releases held key; overlapping run ignored");
output.Events.Clear();output.FailOn=Keys.B;
try{await runner.Run(Def(new MacroStep("Down","A"),new MacroStep("Press","B"),new MacroStep("Up","A")));}catch(IOException){}
Check(output.Events.Contains("A:True"),"Input failure releases previously held keys");
output.FailOn=null;output.Events.Clear();await runner.Run(Def(new MacroStep("Press","C")));
Check(output.Events.SequenceEqual(new[]{"C:False","C:True"}),"Runner recovers after failure");
var mouse=new TestMouse();mouse.SetButtonBinding(3,0x68);
Check(mouse.Packets[0].Skip(1).SequenceEqual(new byte[]{0x51,0x21,0,0,0xE4,1,0x68,0}),"Keris II Ace side-back mapping packet");
Check(mouse.Packets[1][1]==0x50&&mouse.Packets[1][2]==3,"Binding saved to profile");
mouse.Fail=true;bool failed=false;try{mouse.SetButtonBinding(3,0x69);}catch(IOException){failed=true;}
Check(failed&&mouse.ButtonBindings[3]==0x68,"Missing device acknowledgement leaves cache unchanged");
MacroStore.Save(0,Def(new MacroStep("Press","A")));
Check(MacroStore.Get(0)?.Steps[0].Key=="A","Macro persistence round trip");
AppConfig.Set("macro_1","broken json");Check(MacroStore.Get(1)==null,"Corrupt macro does not prevent startup");
AppConfig.Set("string-value","abc");Check(AppConfig.Get("string-value",42)==42,"Wrong config type uses fallback");
mouse.Fail=false;mouse.FailSave=true;failed=false;try{mouse.SetButtonBinding(3,0x69);}catch(IOException){failed=true;}
Check(failed&&mouse.ButtonBindings[3]==0x68,"Missing save acknowledgement leaves cache unchanged");
// File formats preserve actions and metadata without evaluating imported code.
var portable=new MacroDefinition("中文宏",[new("Down","LControlKey"),new("Press","C"),new("Up","LControlKey"),new("Delay","",123)],3,MacroTrigger.Hold,75);
foreach(var extension in new[]{".json",".csv",".ahk"})
{
    var roundtrip=MacroFiles.Parse(MacroFiles.Export(portable,extension),extension);
    Check(roundtrip.Name==portable.Name && roundtrip.Repeat==3 && roundtrip.Trigger==MacroTrigger.Hold && roundtrip.Interval==75 && roundtrip.Steps.SequenceEqual(portable.Steps),extension+" round trip preserves steps and trigger settings");
}
var legacy=MacroFiles.Parse("{\"Name\":\"old\",\"Steps\":[{\"Action\":\"Press\",\"Key\":\"A\"}],\"Repeat\":2}",".json");
Check(legacy.Trigger==MacroTrigger.Once&&legacy.Interval==50,"Legacy JSON migration defaults");
var ahk=MacroFiles.Parse("F6::\n{\nSend \"^c\"\nSleep(120)\nSend \"{Enter}\"\n}",".ahk");
Check(ahk.Steps.Count==5&&ahk.Steps[0].Key=="LControlKey"&&ahk.Steps[3].Milliseconds==120,"AHK v2 hotkey and modifier sequence");
Check(MacroFiles.Parse("Send, {Ctrl down}c{Ctrl up}\nSleep, 20\nreturn",".ahk").Steps.Count==4,"AHK v1 linear sequence");
foreach(var text in new[]{"Run \"calc.exe\"","Loop 5\n{\nSend \"a\"\n}","Send \"a\"\nF2::","Send \"{a down}\"","Send \"{Blind}a\"", "Sleep(100", "Sleep 100)"})
{
    bool rejected=false;try{MacroFiles.Parse(text,".ahk");}catch(Exception e) when(e is ArgumentException or FormatException){rejected=true;}
    Check(rejected,"Unsupported/unbalanced AHK rejected: "+text.Split('\n')[0]);
}
Check(MacroFiles.Parse("Action,Key,Milliseconds\nPress,\"A\",0\nDelay,,20",".csv").Steps.Count==2,"Quoted CSV fields accepted");
bool badCsv=false;try{MacroFiles.Parse("Action,Key,Milliseconds\nPress,A,foo",".csv");}catch(FormatException){badCsv=true;}Check(badCsv,"Bad CSV rejected with row error");
// Edge-trigger behavior is tested independently of physical input/hook delivery.
var events=new Output();using var triggers=new MacroTriggers(new MacroRunner(events));
var once=Def(new MacroStep("Press","A"));
triggers.Down(0,once);await triggers.Completion;triggers.Down(0,once);await triggers.Completion;
Check(events.Events.Count==2,"Held key auto-repeat does not re-run single macro");
triggers.Up(0);triggers.Down(0,once);await triggers.Completion;triggers.Up(0);
Check(events.Events.Count==4,"New press re-runs single macro");
events.Events.Clear();
var toggle=once with {Trigger=MacroTrigger.Toggle,Interval=10};
triggers.Down(0,toggle);triggers.Up(0);await Task.Delay(100);triggers.Down(0,toggle);await triggers.Completion;triggers.Up(0);
Check(events.Events.Count>=4&&events.Events.Count%2==0,"Toggle repeats until second press and releases keys");
events.Events.Clear();
var hold=Def(new MacroStep("Down","A"),new MacroStep("Delay","",60000),new MacroStep("Up","A")) with {Trigger=MacroTrigger.Hold};
triggers.Down(1,hold);await Task.Delay(30);triggers.Up(2);Check(!triggers.Completion.IsCompleted,"Unrelated release does not stop held macro");
triggers.Up(1);await triggers.Completion;
Check(events.Events.SequenceEqual(new[]{"A:False","A:True"}),"Hold release interrupts long delay and releases key");
events.Events.Clear();triggers.Down(1,toggle);await Task.Delay(30);triggers.Stop();await triggers.Completion;triggers.Down(1,toggle);
Check(triggers.Completion.IsCompleted,"Emergency stop does not restart from key auto-repeat");triggers.Up(1);
// GMAC and mouse verification never calls SendInput.
foreach(var key in new[]{Keys.LButton,Keys.RButton})
{
    uint downFlag=key==Keys.LButton?2u:8u,upFlag=key==Keys.LButton?4u:16u;
    var down=WindowsKeyOutput.BuildInput(key,false);var up=WindowsKeyOutput.BuildInput(key,true);
    Check(down.type==0&&up.type==0&&down.data.mouse.flags==downFlag&&up.data.mouse.flags==upFlag&&down.data.mouse.data==0&&down.data.mouse.x==0&&down.data.mouse.y==0,"Native mouse packet (not keyboard/movement): "+key);
}
Check(WindowsKeyOutput.BuildInput(Keys.S,false).type==1,"Keyboard output stays keyboard input");
foreach(var key in new[]{"MButton","XButton1","XButton2"})Invalid(Def(new MacroStep("Press",key)),"Unrequested mouse button rejected: "+key);
var mixed=Def(new("Down","LButton"),new("Down","RButton"),new("Press","S"),new("Up","RButton"),new("Up","LButton"));
var mouseOutput=new Output();var mouseRunner=new MacroRunner(mouseOutput);
await mouseRunner.Run(mixed);
Check(mouseOutput.Events.SequenceEqual(new[]{"LButton:False","RButton:False","S:False","S:True","RButton:True","LButton:True"}),"Mixed mouse/keyboard execution order");
mouseOutput.Events.Clear();
var mouseHeld=Def(new("Down","LButton"),new("Down","RButton"),new("Delay","",60000),new("Up","RButton"),new("Up","LButton"));
var mouseTask=mouseRunner.Run(mouseHeld);await Task.Delay(30);mouseRunner.Stop();await mouseTask;
Check(mouseOutput.Events.Contains("LButton:True")&&mouseOutput.Events.Contains("RButton:True"),"Stop releases both held mouse buttons");
mouseOutput.Events.Clear();mouseOutput.FailOn=Keys.S;
try{await mouseRunner.Run(mixed);}catch(IOException){}
Check(mouseOutput.Events.Contains("LButton:True")&&mouseOutput.Events.Contains("RButton:True"),"Input failure releases held mouse buttons");
using(var holdMouse=new MacroTriggers(new MacroRunner(new Output())))
{
    holdMouse.Down(0,mouseHeld with {Trigger=MacroTrigger.Hold});await Task.Delay(30);holdMouse.Up(0);await holdMouse.Completion;
    Check(holdMouse.Completion.IsCompleted,"Hold trigger release cancels mouse macro");
}
const string gmac="Press,Left Click,301,301\r\nDelay,40\r\nPress,Right Click,302,302\r\nPress,S,31,83\r\nRelease,S,31,83\r\nRelease,Right Click,302,302\r\nRelease,Left Click,301,301\r\n1\r\n";
var importedGmac=MacroFiles.Parse(gmac,".GMAC","test");
Check(importedGmac.Steps[0]==new MacroStep("Down","LButton")&&importedGmac.Steps.Count==7&&importedGmac.Repeat==1&&importedGmac.Trigger==MacroTrigger.Once,"GMAC Press is Down; marker is not an action/repeat setting");
Check(MacroFiles.Export(importedGmac,".gmac")==gmac,"GMAC sequence and terminal marker round trip");
Check(MacroFiles.Parse("\uFEFF"+gmac.Replace("\r\n","\n")+"\n",".gmac").Steps.SequenceEqual(importedGmac.Steps),"GMAC BOM/LF/trailing newline accepted");
var expanded=MacroFiles.Parse(MacroFiles.Export(Def(new MacroStep("Press","LButton")),".gmac"),".gmac");
Check(expanded.Steps.SequenceEqual(new[]{new MacroStep("Down","LButton"),new MacroStep("Up","LButton")}),"GMAC tap expands to Press and Release");
Check(MacroFiles.Parse(gmac.Replace("Delay,40","Delay,0"),".gmac").Steps.Count==6,"GMAC zero delay is a no-op");
foreach(var bad in new[]{gmac.Replace("1\r\n","2\r\n"),gmac[..^3],gmac+"Delay,5\n",gmac.Replace("Delay,40","Move,10,20"),gmac.Replace("301,301","301,302"),gmac.Replace("31,83","31,65"),gmac.Replace("Delay,40","Delay,-1"),gmac.Replace("Delay,40","Delay,60001"),"Press,Middle Click,303,303\nRelease,Middle Click,303,303\n1", "Press,Left Click,301,301\n1"})
{
    bool rejected=false;try{MacroFiles.Parse(bad,".gmac");}catch(Exception e)when(e is ArgumentException or FormatException){rejected=true;}
    Check(rejected,"Malformed/unsupported GMAC rejected");
}
foreach(var extension in new[]{".json",".csv",".ahk"})
    Check(MacroFiles.Parse(MacroFiles.Export(mixed,extension),extension).Steps.SequenceEqual(mixed.Steps),"Mouse macro round trip: "+extension);
Check(MacroFiles.Export(mixed,".ahk").Contains("{LButton down}"),"AHK uses mouse names instead of keyboard vk codes");
var samplePath=Environment.GetEnvironmentVariable("ROG_PERIPHERAL_GMAC_SAMPLE");
if(!string.IsNullOrEmpty(samplePath))
{
    var sampleText=File.ReadAllText(samplePath);var sample=MacroFiles.Read(samplePath);
    Check(sample.Steps.Count==128&&sample.Steps.Where(s=>s.Action=="Delay").Sum(s=>s.Milliseconds)==14340,"User sample: 128 steps, 14.34 seconds delay");
    Check(MacroFiles.Export(sample,".gmac").TrimEnd()==sampleText.TrimStart('\uFEFF').TrimEnd(),"User sample: GMAC export matches original event rows");
    var sampleOutput=new Output();var sampleRunner=new MacroRunner(sampleOutput);await sampleRunner.Run(sample);
    var expected=sample.Steps.Where(s=>s.Action!="Delay").Select(s=>$"{s.Key}:{s.Action=="Up"}");
    Check(sampleOutput.Events.SequenceEqual(expected)&&sampleOutput.Events.Count==62,"User sample: all 62 input edges replayed through fake output");
}
var resourceManager=new System.Resources.ResourceManager("GHelper.Properties.AppStrings",typeof(L).Assembly);
var baseResources=resourceManager.GetResourceSet(System.Globalization.CultureInfo.InvariantCulture,true,false)!;
var oldCulture=System.Globalization.CultureInfo.CurrentUICulture;
foreach(var locale in new[]{"en","zh-CN","zh-TW"})
{
    AppConfig.Set("language",locale);L.Initialize();
    var culture=System.Globalization.CultureInfo.CurrentUICulture;
    Check(culture.Name==locale,"Configured UI culture: "+locale);
    var translated=resourceManager.GetResourceSet(locale=="en"?System.Globalization.CultureInfo.InvariantCulture:culture,true,false)!;
    bool complete=true;
    foreach(System.Collections.DictionaryEntry entry in baseResources)
    {
        string key=(string)entry.Key;
        string? value=translated.GetString(key);
        complete &= !string.IsNullOrWhiteSpace(value);
        if(key!="PlainText")
        {
            var expected=System.Text.RegularExpressions.Regex.Matches((string)entry.Value!,@"\{\d+\}").Select(m=>m.Value).Order().ToArray();
            var actual=System.Text.RegularExpressions.Regex.Matches(value??"",@"\{\d+\}").Select(m=>m.Value).Order().ToArray();
            complete &= expected.SequenceEqual(actual);
            _=string.Format(value??"",1,2,3);
        }
    }
    Check(complete,"All translated resources and format placeholders: "+locale);
    Check(L.Format("Slot",1,13).Contains("F13"),"Localized slot keeps trigger key: "+locale);
    var roundTrip=MacroFiles.Parse(MacroFiles.Export(portable,".csv"),".csv");
    Check(roundTrip.Name==portable.Name&&roundTrip.Steps.SequenceEqual(portable.Steps),"Language does not change macro file data: "+locale);
}
AppConfig.Set("language","de");L.Initialize();
var upstreamResources=new System.Resources.ResourceManager("GHelper.Properties.Strings",typeof(L).Assembly);
Check(L.Text("Macros")=="Macros"&&upstreamResources.GetString("Lighting")=="Beleuchtung","New text falls back to English while upstream text stays German");
AppConfig.Set("language","invalid_!language");L.Initialize();
Check(System.Globalization.CultureInfo.CurrentUICulture.Name=="de","Invalid language preserves current UI culture");
AppConfig.Set("language","");System.Globalization.CultureInfo.CurrentUICulture=System.Globalization.CultureInfo.GetCultureInfo("zh-TW");L.Initialize();
Check(L.Text("Macros")=="巨集按鍵","Unset language follows current UI culture");
System.Globalization.CultureInfo.CurrentUICulture=oldCulture;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture=oldCulture;
var beforeWait=Def(new("Down","LButton",80),new("Up","LButton",90),new("Delay","",20));
var timed=new TimedOutput();var timedRunner=new MacroRunner(timed);
await timedRunner.Run(beforeWait);
Check(timed.Events.Count==2 && timed.Events[0]>=70 && timed.Events[1]-timed.Events[0]>=80,"Down and Up each wait before sending input");
var cancelOutput=new Output();var cancelRunner=new MacroRunner(cancelOutput);
var pending=cancelRunner.Run(Def(new("Down","A",60000),new("Up","A")));cancelRunner.Stop();await pending;
Check(cancelOutput.Events.Count==0,"Cancel during initial wait sends no key");
pending=cancelRunner.Run(Def(new("Down","A"),new("Up","A",60000)));await Task.Delay(20);cancelRunner.Stop();await pending;
Check(cancelOutput.Events.SequenceEqual(new[]{"A:False","A:True"}),"Cancel during Up wait releases held key");
foreach(var ext in new[]{".json",".csv",".ahk",".gmac"})
{
    var restored=MacroFiles.Parse(MacroFiles.Export(beforeWait,ext),ext);
    Check(MacroStore.EditorSteps(restored.Steps).SequenceEqual(beforeWait.Steps),"Action waits survive export/import: "+ext);
}
Check(MacroStore.EditorSteps([new("Delay","",40),new("Press","A",10),new("Delay","",20)]).SequenceEqual(new MacroStep[]{new("Down","A",50),new("Up","A"),new("Delay","",20)}),"Editor folds preceding delay and expands legacy tap without losing trailing delay");
Invalid(Def(new("Down","A",-1),new("Up","A")),"Negative action wait rejected");
Invalid(Def(new("Down","A",60001),new("Up","A")),"Excessive action wait rejected");
Invalid(new("long",[new("Down","A",60000),new("Up","A",60000)],6),"Action waits count toward total duration limit");
foreach(var value in new[]{"-1","+1","1.5","1e3","abc","１２","60001","99999999999999999","1 0"})
{
    bool rejected=false;try{MacroEditor.ParseDelay(value,false);}catch(ArgumentException){rejected=true;}
    Check(rejected,"Editor rejects invalid or pasted delay: "+value);
}
Check(MacroEditor.ParseDelay("",false)==0 && MacroEditor.ParseDelay("60000",false)==60000 && MacroEditor.ParseDelay("1",true)==1,"Editor accepts action wait boundaries");
foreach(var value in new[]{"","0"})
{
    bool rejected=false;try{MacroEditor.ParseDelay(value,true);}catch(ArgumentException){rejected=true;}
    Check(rejected,"Standalone Delay requires positive time: "+value);
}
Console.WriteLine($"{passed} checks passed. No real HID writes or keyboard injection performed.");
class TimedOutput:IKeyOutput
{
    readonly Stopwatch clock=Stopwatch.StartNew();
    public List<long> Events=new();
    public void Send(Keys key,bool up)=>Events.Add(clock.ElapsedMilliseconds);
}
class Output:IKeyOutput
{
    public List<string> Events=new();public Keys? FailOn;
    public void Send(Keys key,bool up){if(key==FailOn)throw new IOException("injected failure");Events.Add($"{key}:{up}");}
}
class TestMouse:KerisIIAceWired
{
    public List<byte[]> Packets=new();public bool Fail;public bool FailSave;
    protected override byte[]? WriteForResponse(byte[] packet,int matchLength=3){Packets.Add(packet.ToArray());return Fail||(FailSave&&packet[1]==0x50)?null:packet;}
}
