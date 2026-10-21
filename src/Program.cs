using GHelper.Peripherals;
using GHelper.UI;
namespace GHelper;
internal static class Program
{
    public static MainForm settingsForm;
    public static InputDispatcher inputDispatcher;
    [STAThread] static void Main(string[] args)
    {
        L.Initialize();
        if (args.Contains("--diagnose"))
        {
            var devices = HidSharp.DeviceList.Local.GetHidDevices(0x0B05)
                .Select(d => new { Vendor = $"{d.VendorID:X4}", Product = $"{d.ProductID:X4}" }).Distinct().ToArray();
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"devices.json"), System.Text.Json.JsonSerializer.Serialize(devices));
            return;
        }
        if(args.Contains("--ui-smoke")) { UiSmoke.Run(); return; }
        using var singleton = new Mutex(true, "ROGPeripheral.App", out bool first);
        if (!first) { MessageBox.Show(L.Text("AlreadyRunning"), "ROG Peripheral"); return; }
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_,e) => { Logger.WriteLine(e.Exception.ToString()); MessageBox.Show(e.Exception.Message, "ROG Peripheral"); };
        settingsForm = new MainForm();
        inputDispatcher = new InputDispatcher();
        Application.Run(settingsForm);
        inputDispatcher.Dispose();
        foreach (var device in PeripheralsProvider.AllPeripherals().OfType<IDisposable>()) device.Dispose();
    }
}

public class MainForm : RForm
{
    readonly FlowLayoutPanel devices = new() { Dock=DockStyle.Fill, FlowDirection=FlowDirection.TopDown, WrapContents=false, AutoScroll=true, Padding=new Padding(18) };
    readonly NotifyIcon tray;
    readonly Dictionary<IPeripheral,Form> windows = new();
    MacroEditor? editor;
    bool exiting, scanning;
    public MainForm()
    {
        Text="ROG Peripheral · G-Helper Lite"; AutoScaleDimensions=new SizeF(192,192); AutoScaleMode=AutoScaleMode.Dpi; ClientSize=new Size(310,540); Font=new Font("Segoe UI",8F); StartPosition=FormStartPosition.CenterScreen;
        Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        var version = typeof(Program).Assembly.GetName().Version!;
        var versionLabel = new Label { Text=$"v{version.Major}.{version.Minor}.{version.Build}", Dock=DockStyle.Bottom, Height=36, TextAlign=ContentAlignment.MiddleCenter };
        Controls.Add(devices); Controls.Add(versionLabel); InitTheme();
        var menu=new CustomContextMenu { Renderer=new CustomMenuRenderer(), BackColor=formBack, ForeColor=foreMain };
        menu.Items.Add(L.Text("Open"),null,(_,_)=>Restore());
        menu.Items.Add(L.Text("StopMacros"),null,(_,_)=>Program.inputDispatcher.Stop());
        menu.Items.Add(L.Text("Exit"),null,(_,_)=>{ exiting=true; Close(); });
        tray=new NotifyIcon { Icon=Icon,Text=L.Text("TrayTitle"),ContextMenuStrip=menu,Visible=true };
        tray.DoubleClick+=(_,_)=>Restore();
        Shown += async (_,_) => { PeripheralsProvider.RegisterForDeviceEvents(); await Scan(); };
        FormClosing+=(_,e)=>
        {
            if (!exiting && e.CloseReason==CloseReason.UserClosing)
            {
                e.Cancel=true; Hide();
                if(!AppConfig.Is("tray_hint")) {tray.ShowBalloonTip(3000,"ROG Peripheral",L.Text("TrayHint"),ToolTipIcon.Info);AppConfig.Set("tray_hint",1);}
                return;
            }
            Program.inputDispatcher.Stop();
            PeripheralsProvider.UnregisterForDeviceEvents();
        };
        FormClosed+=(_,_)=>{tray.Visible=false;tray.Dispose();};
        VisualizePeripherals();
    }
    void Restore(){Show();WindowState=FormWindowState.Normal;Activate();}
    public override void RefreshTheme()
    {
        if (InitTheme()) VisualizePeripherals();
        if (tray?.ContextMenuStrip is { } menu) { menu.BackColor=formBack; menu.ForeColor=foreMain; }
    }
    async Task Scan()
    {
        if(scanning)return;
        scanning=true;VisualizePeripherals();
        try { await Task.Run(()=> { PeripheralsProvider.DetectAllAsusMice(); PeripheralsProvider.DetectAllAsusKeyboards(); PeripheralsProvider.DetectAllAsusHeadsets(); }); }
        catch(Exception e){Logger.WriteLine(e.ToString());MessageBox.Show(this,L.Text("ScanFailed")+e.Message,"ROG Peripheral",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        finally{scanning=false;if(!IsDisposed)VisualizePeripherals();}
    }
    public void UpdateKeyboardLabel() { }
    void OpenDevice(IPeripheral device)
    {
        if(windows.TryGetValue(device,out var existing)&&!existing.IsDisposed){existing.Activate();return;}
        Form form=device switch { Peripherals.Mouse.AsusMouse m=>new AsusMouseSettings(m),Peripherals.Keyboard.AsusKeyboard k=>new AsusKeyboardSettings(k),Peripherals.Headset.AsusHeadset h=>new AsusHeadsetSettings(h),_=>throw new NotSupportedException() };
        windows[device]=form;
        form.Show(this);
        var bounds=Screen.FromControl(this).WorkingArea;
        form.Location=new Point(Math.Max(bounds.Left,Math.Min(Left,bounds.Right-form.Width)),Math.Max(bounds.Top,Math.Min(Top,bounds.Bottom-form.Height)));
    }
    public void VisualizePeripherals()
    {
        if(IsDisposed||Disposing||exiting)return;
        devices.SuspendLayout();
        foreach(Control control in devices.Controls.Cast<Control>().ToArray())control.Dispose();
        devices.Controls.Add(new Label { Text=L.Text("Peripherals"), Font=new Font("Segoe UI",9F,FontStyle.Bold),AutoSize=true,Margin=new Padding(4,0,0,12) });
        var cards=new FlowLayoutPanel{AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=Padding.Empty};
        foreach(var device in PeripheralsProvider.AllPeripherals())
        {
            Image icon=device.DeviceType() switch
            {
                PeripheralType.Mouse=>((Peripherals.Mouse.AsusMouse)device).Icon(),
                PeripheralType.Keyboard=>Properties.Resources.icons8_keyboard_48,
                _=>Properties.Resources.icons8_headphones_48
            };
            var button=Card(device.GetDisplayName().Replace(" (","\n("),icon);
            button.Enabled=device.IsDeviceReady;
            if(device.HasBattery()&&device.IsDeviceReady)
            {
                int level=Math.Clamp((device.Battery+10)/20,0,5);
                var old=button.Image;
                button.Image=ControlHelper.OverlayChargeBars(old,level,5,level<=1?colorTurbo:level<=3?colorStandard:colorEco,iconWidth:Math.Min(old.Width,old.Height),iconHeight:old.Height);
                old.Dispose();
            }
            button.Click+=(_,_)=>OpenDevice(device);cards.Controls.Add(button);
        }
        devices.Controls.Add(cards);
        if(!PeripheralsProvider.IsAnyPeripheralConnect())devices.Controls.Add(new Label{Text=L.Text("NoDevices"),AutoSize=true,MaximumSize=new Size(270,0)});
        var actions=new FlowLayoutPanel{AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=new Padding(0,8,0,4)};
        var macros=Card(L.Text("Macros"),Properties.Resources.icons8_keyboard_48);
        macros.Click+=(_,_)=>{if(editor==null||editor.IsDisposed){editor=new MacroEditor();editor.Show(this);}else editor.Activate();};actions.Controls.Add(macros);
        var refresh=Card(scanning?L.Text("Scanning"):L.Text("Refresh"),Properties.Resources.icons8_refresh_48);
        refresh.Enabled=!scanning;refresh.Click+=async(_,_)=>await Scan();actions.Controls.Add(refresh);
        devices.Controls.Add(actions);
        InitTheme();ControlHelper.Adjust(this);devices.ResumeLayout();
    }    static RButton Card(string text,Image image)
    {
        var button=new RButton{Text=text,Size=new Size(254,100),Font=new Font("Segoe UI",8F),Secondary=true,BorderRadius=5,Margin=new Padding(4),ImageAlign=ContentAlignment.MiddleLeft,TextImageRelation=TextImageRelation.ImageBeforeText,Padding=new Padding(10,4,8,4)};
        button.Image=ControlHelper.TintImage(image,foreMain);
        button.Disposed+=(_,_)=>button.Image?.Dispose();return button;
    }
}
