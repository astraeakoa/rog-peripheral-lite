using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using GHelper.Peripherals;
using GHelper.Peripherals.Mouse;
using GHelper.Peripherals.Keyboard;
namespace GHelper;

public record MacroStep(string Action, string Key = "", int Milliseconds = 0);
public enum MacroTrigger { Once, Toggle, Hold }
public record MacroDefinition(string Name, List<MacroStep> Steps, int Repeat = 1, MacroTrigger Trigger = MacroTrigger.Once, int Interval = 50);
public static class MacroStore
{
    public static MacroDefinition? Get(int slot)
    {
        try { var macro=JsonSerializer.Deserialize<MacroDefinition>(AppConfig.GetString($"macro_{slot}") ?? "null"); if(macro!=null)Validate(macro);return macro; }
        catch(Exception e) when(e is JsonException or ArgumentException) { Logger.WriteLine(L.Format("InvalidMacroConfig", slot+1, e.Message));return null; }
    }
    public static void Save(int slot, MacroDefinition macro) { Validate(macro); AppConfig.Set($"macro_{slot}", JsonSerializer.Serialize(macro)); }
    public static Keys ParseKey(string key)
    {
        if (!Enum.TryParse<Keys>(key, true, out var parsed) || !Enum.IsDefined(parsed) || (int)parsed < 8 || (int)parsed > 254 || (parsed >= Keys.F13 && parsed <= Keys.F24))
            throw new ArgumentException(L.Format("InvalidKey", key));
        return parsed;
    }
    public static void Validate(MacroDefinition macro)
    {
        if (string.IsNullOrWhiteSpace(macro.Name) || macro.Steps is null || macro.Steps.Count is < 1 or > 500 || macro.Repeat is < 1 or > 100) throw new ArgumentException(L.Text("MacroLimits"));
        if (!Enum.IsDefined(macro.Trigger) || macro.Interval is < 10 or > 60000) throw new ArgumentException(L.Text("TriggerLimits"));
        var held=new HashSet<Keys>(); long duration=0;
        foreach (var step in macro.Steps)
        {
            if (step is null) throw new ArgumentException(L.Text("NullStep"));
            if (step.Action == "Delay") { if (step.Milliseconds is < 1 or > 60000) throw new ArgumentException(L.Text("DelayLimits")); duration+=step.Milliseconds; continue; }
            if (step.Action is not ("Down" or "Up" or "Press")) throw new ArgumentException(L.Text("ActionOptions"));
            var key=ParseKey(step.Key);
            if (step.Action=="Down" && !held.Add(key)) throw new ArgumentException(L.Format("DuplicateDown", key));
            if (step.Action=="Up" && !held.Remove(key)) throw new ArgumentException(L.Format("NotHeld", key));
            if (step.Action=="Press" && held.Contains(key)) throw new ArgumentException(L.Format("AlreadyHeld", key));
        }
        if (held.Count>0) throw new ArgumentException(L.Text("UnbalancedKeys"));
        if (duration*macro.Repeat>600000) throw new ArgumentException(L.Text("DurationLimit"));
    }
}
public interface IKeyOutput { void Send(Keys key, bool up); }
public sealed class WindowsKeyOutput : IKeyOutput
{
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public UNION data; }
    [StructLayout(LayoutKind.Explicit)] struct UNION { [FieldOffset(0)] public KEYBOARD keyboard; [FieldOffset(0)] public MOUSE mouse; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBOARD { public ushort vk, scan; public uint flags, time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSE { public int x,y; public uint data,flags,time; public UIntPtr extra; }
    [DllImport("user32.dll", SetLastError=true)] static extern uint SendInput(uint count, INPUT[] input, int size);
    public void Send(Keys key, bool up)
    {
        bool extended=key is Keys.RControlKey or Keys.RMenu or Keys.Insert or Keys.Delete or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown or Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.LWin or Keys.RWin or Keys.Divide;
        var input=new INPUT { type=1, data=new UNION { keyboard=new KEYBOARD { vk=(ushort)key,flags=(up?2u:0u)|(extended?1u:0u) } } };
        if (SendInput(1,[input],Marshal.SizeOf<INPUT>())!=1) throw new Win32Exception(Marshal.GetLastWin32Error(),L.Text("InputFailed"));
    }
}
public sealed class MacroRunner(IKeyOutput output)
{
    readonly SemaphoreSlim gate=new(1,1);
    CancellationTokenSource? active;
    readonly object sync=new();
    public void Stop() { lock(sync) active?.Cancel(); }
    public void WaitForStop() { Stop(); gate.Wait(); gate.Release(); }
    public async Task Run(MacroDefinition macro, CancellationToken cancellation = default, bool loop = false)
    {
        MacroStore.Validate(macro);
        if (!await gate.WaitAsync(0).ConfigureAwait(false)) return;
        using var cts=CancellationTokenSource.CreateLinkedTokenSource(cancellation); lock(sync) active=cts;
        var held=new HashSet<Keys>();
        try
        {
            do {
            for(int repeat=0;repeat<macro.Repeat;repeat++) foreach(var step in macro.Steps)
            {
                cts.Token.ThrowIfCancellationRequested();
                if(step.Action=="Delay") { await Task.Delay(step.Milliseconds,cts.Token).ConfigureAwait(false); continue; }
                var key=MacroStore.ParseKey(step.Key);
                if(step.Action!="Up") { output.Send(key,false); held.Add(key); }
                if(step.Action!="Down") { output.Send(key,true); held.Remove(key); }
                // Yield to the UI so Stop remains usable even for sequences without delays.
                await Task.Delay(1,cts.Token).ConfigureAwait(false);
            }
            if(loop) await Task.Delay(macro.Interval,cts.Token).ConfigureAwait(false);
            } while(loop);
        }
        catch(OperationCanceledException) {}
        finally { foreach(var key in held.Reverse()) try { output.Send(key,true); } catch(Exception e) { Logger.WriteLine(e.Message); } lock(sync) active=null; gate.Release(); }
    }
}

// The controller sees edges, never OS auto-repeat; only one macro owns the runner.
public sealed class MacroTriggers(MacroRunner runner) : IDisposable
{
    readonly HashSet<int> pressed = new();
    CancellationTokenSource? cancellation;
    int activeSlot = -1;
    MacroTrigger activeMode;
    public Task Completion { get; private set; } = Task.CompletedTask;
    public event Action<Exception>? Failed;
    public void Down(int slot, MacroDefinition macro)
    {
        if (!pressed.Add(slot)) return;
        if (!Completion.IsCompleted)
        {
            if (activeSlot == slot && activeMode == MacroTrigger.Toggle) Stop();
            return;
        }
        MacroStore.Validate(macro);
        cancellation?.Dispose(); cancellation = new();
        activeSlot = slot; activeMode = macro.Trigger;
        Completion = Run(macro, cancellation.Token);
    }
    async Task Run(MacroDefinition macro, CancellationToken token)
    {
        try { await runner.Run(macro, token, macro.Trigger != MacroTrigger.Once).ConfigureAwait(false); }
        catch (Exception e) { Failed?.Invoke(e); }
    }
    public void Up(int slot) { pressed.Remove(slot); if (slot == activeSlot && activeMode == MacroTrigger.Hold) Stop(); }
    public void Stop() { cancellation?.Cancel(); runner.Stop(); }
    public void Dispose() { Stop(); runner.WaitForStop(); cancellation?.Dispose(); }
}
public sealed class InputDispatcher : NativeWindow, IDisposable
{
    const int TriggerMessage = 0x8001;
    [DllImport("user32.dll",SetLastError=true)] static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window,int id);
    delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int type, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
    [StructLayout(LayoutKind.Sequential)] struct HookData { public uint Key, Scan, Flags, Time; public UIntPtr Extra; }
    readonly HashSet<int> registered=new();
    readonly bool[] enabled = new bool[12], captured = new bool[12];
    readonly HookProc callback;
    readonly IntPtr hook;
    public List<string> Errors {get;}=new();
    public readonly MacroRunner Runner=new(new WindowsKeyOutput());
    public readonly MacroTriggers Triggers;
    readonly bool listening;
    public InputDispatcher(bool listen = true)
    {
        CreateHandle(new CreateParams()); Triggers=new(Runner);
        Triggers.Failed += e => { Logger.WriteLine(e.ToString()); PostMessage(Handle,TriggerMessage,(IntPtr)200,IntPtr.Zero); };
        listening=listen;
        callback=KeyboardHook;
        hook=listen ? SetWindowsHookEx(13,callback,GetModuleHandle(null),0) : IntPtr.Zero;
        RegisterKeys();
        Microsoft.Win32.SystemEvents.SessionSwitch += SessionChanged;
        Microsoft.Win32.SystemEvents.PowerModeChanged += PowerChanged;
    }
    // Keep the hook fast: no config I/O, no playback, only capture and queue edges.
    IntPtr KeyboardHook(int code,IntPtr message,IntPtr data)
    {
        if(code>=0)
        {
            var key=Marshal.PtrToStructure<HookData>(data);
            int slot=(int)key.Key-(int)Keys.F13;
            bool up=message.ToInt32() is 0x101 or 0x105;
            bool down=message.ToInt32() is 0x100 or 0x104;
            if((key.Flags & 0x10)==0 && slot>=0 && slot<12 && (enabled[slot]||captured[slot]) && (up||down))
            {
                if(down && captured[slot]) return (IntPtr)1;
                captured[slot]=!up;
                PostMessage(Handle,TriggerMessage,(IntPtr)slot,up?(IntPtr)1:IntPtr.Zero);
                return (IntPtr)1;
            }
        }
        return CallNextHookEx(hook,code,message,data);
    }
    void SessionChanged(object? sender, Microsoft.Win32.SessionSwitchEventArgs e) => PostMessage(Handle,TriggerMessage,(IntPtr)200,IntPtr.Zero);
    void PowerChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if(e.Mode==Microsoft.Win32.PowerModes.Suspend)PostMessage(Handle,TriggerMessage,(IntPtr)200,IntPtr.Zero);
    }
    public void Stop() => Triggers.Stop();
    public void RegisterKeys()
    {
        if(!listening)return;
        Triggers.Stop();
        foreach(var id in registered) UnregisterHotKey(Handle,id); registered.Clear(); Errors.Clear();
        if(hook==IntPtr.Zero) Errors.Add(L.Text("HookFailed"));
        var active=PeripheralsProvider.SnapshotMice().Where(m=>m.HasButtonBindings()).SelectMany(m=>m.ButtonBindings ?? []).ToHashSet();
        for(int slot=0;slot<12;slot++) enabled[slot]=MacroStore.Get(slot)!=null || active.Contains((ushort)(0x68+slot));
        for(int slot=0;slot<4;slot++) if(AsusKeyboard.LaunchCommand(slot).Length>0) Register(100+slot,0x4007,(uint)(Keys.F1+slot));
        Register(200,0x4003,(uint)Keys.F12);
    }
    void Register(int id,uint modifiers,uint key) { if(RegisterHotKey(Handle,id,modifiers,key)) registered.Add(id); else Errors.Add(L.Format("HotkeyFailed", (Keys)key)); }
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==TriggerMessage)
        {
            int slot=m.WParam.ToInt32();
            if(slot==200) Stop();
            else if(m.LParam!=IntPtr.Zero) Triggers.Up(slot);
            else if(enabled[slot]) Execute(slot);
        }
        if(m.Msg==0x312) { int id=m.WParam.ToInt32(); if(id==200) Stop(); else Execute(id); }
        base.WndProc(ref m);
    }
    void Execute(int id)
    {
        try
        {
            if(id>=100) { Launch(AsusKeyboard.LaunchCommand(id-100)); return; }
            var macro=MacroStore.Get(id);
            if(macro!=null) { Triggers.Down(id,macro); return; }
            var command=AsusMouse.MouseCombos[id].ResolveCommand();
            var tokens=command.Split(' ',StringSplitOptions.RemoveEmptyEntries);
            var keys=new List<Keys>();
            foreach(var token in tokens) { if(!int.TryParse(token.Replace("0x",""),System.Globalization.NumberStyles.HexNumber,null,out int n)||n<1||n>254) { Launch(command); return; } keys.Add((Keys)n); }
            var steps=keys.Select(k=>new MacroStep("Down",k.ToString())).Concat(keys.AsEnumerable().Reverse().Select(k=>new MacroStep("Up",k.ToString()))).ToList();
            if(steps.Count>0) Triggers.Down(id,new MacroDefinition(L.Text("Shortcut"),steps));
        }
        catch(Exception e) { Logger.WriteLine(e.ToString()); MessageBox.Show(e.Message,L.Text("PlaybackFailed")); }
    }
    static void Launch(string command) { if(!string.IsNullOrWhiteSpace(command)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(command) { UseShellExecute=true }); }
    public void Dispose() { Microsoft.Win32.SystemEvents.SessionSwitch -= SessionChanged; Microsoft.Win32.SystemEvents.PowerModeChanged -= PowerChanged; if(hook!=IntPtr.Zero)UnhookWindowsHookEx(hook); Triggers.Dispose(); foreach(var id in registered) UnregisterHotKey(Handle,id); DestroyHandle(); }
}
