using GHelper.UI;
using GHelper.Peripherals;
using GHelper.Peripherals.Mouse;
namespace GHelper;
public sealed class MacroEditor : RForm
{
    readonly RComboBox slots=new() { DropDownStyle=ComboBoxStyle.DropDownList,Width=175 };
    readonly RTextBox name=new() { Width=220 };
    readonly RNumericUpDown repeat=new() { Minimum=1,Maximum=100,Width=70 };
    readonly RComboBox trigger=new() { DropDownStyle=ComboBoxStyle.DropDownList,Width=210 };
    readonly RNumericUpDown interval=new() { Minimum=10,Maximum=60000,Value=50,Width=85 };
    readonly DataGridView grid=new() { Dock=DockStyle.Fill,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,AllowUserToAddRows=true,RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false };
    readonly RComboBox mice=new() { DropDownStyle=ComboBoxStyle.DropDownList,Width=350 };
    readonly RComboBox buttons=new() { DropDownStyle=ComboBoxStyle.DropDownList,Width=150 };
    readonly Label status=new() { AutoSize=true,MaximumSize=new Size(720,0) };
    readonly RButton bind=new() { Text=L.Text("Bind"),AutoSize=true };
    public MacroEditor()
    {
        Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Text=L.Text("MacroTitle"); AutoScaleDimensions=new SizeF(192,192);AutoScaleMode=AutoScaleMode.Dpi;ClientSize=new Size(940,720); Font=new Font("Segoe UI",8F); StartPosition=FormStartPosition.CenterParent;
        var layout=new TableLayoutPanel {Dock=DockStyle.Fill,RowCount=6,ColumnCount=1,Padding=new Padding(16)};
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); Controls.Add(layout);
        layout.Controls.Add(new Label {Text=L.Text("MacroHelp"),AutoSize=true,MaximumSize=new Size(740,0),Margin=new Padding(0,0,0,12)},0,0);
        var top=new FlowLayoutPanel {AutoSize=true,Dock=DockStyle.Fill}; top.Controls.AddRange([slots,new Label{Text=L.Text("Name"),AutoSize=true},name,new Label{Text=L.Text("Repeat"),AutoSize=true},repeat]); layout.Controls.Add(top,0,1);
        trigger.Items.AddRange(new object[]{L.Text("Once"),L.Text("Toggle"),L.Text("Hold")});trigger.SelectedIndex=0;
        var modes=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill};
        modes.Controls.AddRange([new Label{Text=L.Text("Trigger"),AutoSize=true},trigger,new Label{Text=L.Text("Interval"),AutoSize=true},interval]);layout.Controls.Add(modes,0,2);
        var hint=new Label{AutoSize=true,Text=L.Text("OnceHint"),Margin=new Padding(12,3,0,0)};modes.Controls.Add(hint);
        trigger.SelectedIndexChanged+=(_,_)=>{interval.Enabled=trigger.SelectedIndex!=0;hint.Text=trigger.SelectedIndex switch {1=>L.Text("ToggleHint"),2=>L.Text("HoldHint"),_=>L.Text("OnceHint")};};
        interval.Enabled=false;
        grid.DataError += (_,e) => { e.ThrowException=false; status.Text=L.Text("InvalidAction"); };
        grid.Columns.Add(new DataGridViewComboBoxColumn {Name="Action",HeaderText=L.Text("Action"),DataSource=new[]{"Press","Down","Up","Delay"}});
        grid.Columns.Add(new DataGridViewTextBoxColumn {Name="Key",HeaderText=L.Text("Key"),ToolTipText=L.Text("KeyHint"),FillWeight=180});
        grid.Columns.Add(new DataGridViewTextBoxColumn {Name="Milliseconds",HeaderText=L.Text("Delay")}); layout.Controls.Add(grid,0,3);
        var actions=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill};
        Add(actions,L.Text("Save"),()=>Save());
        Add(actions,L.Text("Import"),Import); Add(actions,L.Text("Export"),Export);
        Add(actions,L.Text("CopyExample"),()=>{grid.Rows.Clear(); grid.Rows.Add("Down","LControlKey",0);grid.Rows.Add("Press","C",0);grid.Rows.Add("Up","LControlKey",0);grid.Rows.Add("Delay","",200);});
        Add(actions,L.Text("MoveUp"),()=>MoveStep(-1)); Add(actions,L.Text("MoveDown"),()=>MoveStep(1));
        Add(actions,L.Text("DeleteStep"),()=>{if(grid.CurrentRow is {IsNewRow:false} row) grid.Rows.Remove(row);});
        Add(actions,L.Text("ClearSlot"),()=>{AppConfig.Set($"macro_{slots.SelectedIndex}","null");Program.inputDispatcher.RegisterKeys();LoadSlot();status.Text=L.Text("Cleared");});
        Add(actions,L.Text("Stop"),()=>Program.inputDispatcher.Stop()); layout.Controls.Add(actions,0,4);
        var bottom=new FlowLayoutPanel {AutoSize=true,Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown};
        var binding=new FlowLayoutPanel{AutoSize=true}; binding.Controls.AddRange([mice,buttons,bind]); bottom.Controls.Add(binding);
        Add(bottom,L.Text("Restore"),()=>Bind(true)); bottom.Controls.Add(status); layout.Controls.Add(bottom,0,5);
        for(int i=0;i<12;i++) slots.Items.Add(L.Format("Slot", i+1, 13+i)); slots.SelectedIndexChanged+=(_,_)=>LoadSlot(); slots.SelectedIndex=0;
        foreach(var mouse in PeripheralsProvider.SnapshotMice().Where(m=>m.HasButtonBindings())) mice.Items.Add(new MouseItem(mouse));
        mice.SelectedIndexChanged+=(_,_)=>{buttons.Items.Clear(); if(mice.SelectedItem is MouseItem item) foreach(var pair in item.Mouse.ButtonSlots.Where(p=>p.Key!=0)) buttons.Items.Add(new ButtonItem(pair.Key,pair.Value.Name)); if(buttons.Items.Count>0) buttons.SelectedIndex=Math.Max(0,buttons.Items.Cast<ButtonItem>().ToList().FindIndex(b=>b.Slot==3));};
        if(mice.Items.Count>0)mice.SelectedIndex=0;
        bind.Enabled=mice.Items.Count>0; bind.Click+=(_,_)=>Bind(false); InitTheme();
        grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.AutoSize;
    }
    protected override void OnThemeApplied()
    {
        grid.BackgroundColor=formBack;
        grid.DefaultCellStyle.BackColor=buttonMain;
        grid.DefaultCellStyle.ForeColor=foreMain;
        grid.GridColor=borderMain;
        grid.EnableHeadersVisualStyles=false;
        grid.ColumnHeadersDefaultCellStyle.BackColor=buttonSecond;
        grid.ColumnHeadersDefaultCellStyle.ForeColor=foreMain;
        grid.RowHeadersDefaultCellStyle.BackColor=buttonSecond;
        grid.RowHeadersDefaultCellStyle.ForeColor=foreMain;
    }
    record MouseItem(AsusMouse Mouse) { public override string ToString()=>Mouse.GetDisplayName(); }
    record ButtonItem(int Slot,string Name) { public override string ToString()=>Name; }
    void Add(Control host,string text,Action action) {var b=new RButton{Text=text,AutoSize=true,Height=35}; b.Click+=(_,_)=>{try{action();}catch(Exception e){status.Text=e.Message;}};host.Controls.Add(b);}
    void LoadSlot()
    {
        var macro=MacroStore.Get(slots.SelectedIndex); name.Text=macro?.Name??L.Format("MacroName", slots.SelectedIndex+1); repeat.Value=macro?.Repeat??1;trigger.SelectedIndex=(int)(macro?.Trigger??MacroTrigger.Once);interval.Value=macro?.Interval??50;grid.Rows.Clear();
        if(macro!=null)foreach(var step in macro.Steps)grid.Rows.Add(step.Action,step.Key,step.Milliseconds);
    }
    void MoveStep(int offset) {if(grid.CurrentRow is not {IsNewRow:false} row)return;int target=row.Index+offset;if(target<0||target>=grid.Rows.Count-1)return;var values=row.Cells.Cast<DataGridViewCell>().Select(c=>c.Value).ToArray();grid.Rows.Remove(row);grid.Rows.Insert(target,values);grid.CurrentCell=grid.Rows[target].Cells[0];}
    MacroDefinition ReadEditor()
    {
        grid.EndEdit(); var steps=new List<MacroStep>();
        foreach(DataGridViewRow row in grid.Rows) {if(row.IsNewRow)continue;string action=Convert.ToString(row.Cells[0].Value)??""; int ms=0;if(action=="Delay"&&!int.TryParse(Convert.ToString(row.Cells[2].Value),out ms))throw new ArgumentException(L.Text("IntegerDelay"));steps.Add(new MacroStep(action,Convert.ToString(row.Cells[1].Value)??"",ms));}
        var macro=new MacroDefinition(name.Text,steps,(int)repeat.Value,(MacroTrigger)trigger.SelectedIndex,(int)interval.Value);
        MacroStore.Validate(macro);return macro;
    }
    bool Save()
    {
        MacroStore.Save(slots.SelectedIndex,ReadEditor());Program.inputDispatcher.RegisterKeys();
        status.Text=Program.inputDispatcher.Errors.Count==0?L.Text("Saved"):string.Join("\n",Program.inputDispatcher.Errors);return Program.inputDispatcher.Errors.Count==0;
    }
    void Import()
    {
        using var dialog=new OpenFileDialog{Filter=MacroFiles.Filter,Title=L.Text("ImportTitle")};
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        var macro=MacroFiles.Read(dialog.FileName); // Parse fully before replacing any editor content.
        name.Text=macro.Name;repeat.Value=macro.Repeat;trigger.SelectedIndex=(int)macro.Trigger;interval.Value=macro.Interval;grid.Rows.Clear();
        foreach(var step in macro.Steps)grid.Rows.Add(step.Action,step.Key,step.Milliseconds);
        status.Text=L.Format("Imported", macro.Steps.Count);
    }
    void Export()
    {
        var macro=ReadEditor();
        using var dialog=new SaveFileDialog{Filter=MacroFiles.Filter,FileName="macro",DefaultExt="json",AddExtension=true,Title=L.Text("ExportTitle")};
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        string content=MacroFiles.Export(macro,Path.GetExtension(dialog.FileName));
        File.WriteAllText(dialog.FileName,content,System.Text.Encoding.UTF8);
        status.Text=L.Text("Exported");
    }    async void Bind(bool restore)
    {
        try
        {
            if(mice.SelectedItem is not MouseItem item || buttons.SelectedItem is not ButtonItem button) return;
            if(!restore&&!Save())return;
            if(!PeripheralsProvider.IsDeviceConnected(item.Mouse)||!item.Mouse.IsDeviceReady)throw new InvalidOperationException(L.Text("Disconnected"));
            ushort code=restore?item.Mouse.ButtonSlots[button.Slot].SourceCode:(ushort)(0x68+slots.SelectedIndex);
            Program.inputDispatcher.Stop();
            Enabled=false;status.Text=L.Text("Writing");
            await Task.Run(()=>
            {
                item.Mouse.SetButtonBinding(button.Slot,code);
                if(!item.Mouse.WriteOnlySlots.Contains(button.Slot))
                {
                    item.Mouse.ReadAndLogButtonBindings();
                    if(!item.Mouse.ButtonBindingsReady || item.Mouse.ButtonBindings[button.Slot]!=code)
                        throw new IOException(L.Text("ReadbackMismatch"));
                }
            });
            Program.inputDispatcher.RegisterKeys();status.Text=restore?L.Text("Restored"):L.Text("Bound");
        }
        catch(Exception e){status.Text=L.Text("BindFailed")+e.Message;}
        finally{if(!IsDisposed)Enabled=true;}
    }
}
