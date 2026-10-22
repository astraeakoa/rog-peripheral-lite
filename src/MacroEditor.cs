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
    readonly ToolTip tips=new();
    bool updatingStepDefaults;
    readonly List<Label> sectionHeaders=new();
    readonly List<(Panel Frame,Control Input)> inputFrames=new();
    public MacroEditor()
    {
        Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Text=L.Text("MacroTitle"); AutoScaleDimensions=new SizeF(192,192);AutoScaleMode=AutoScaleMode.Dpi;ClientSize=new Size(940,860); Font=new Font("Segoe UI",8F); StartPosition=FormStartPosition.CenterParent;
        var layout=new TableLayoutPanel {Dock=DockStyle.Fill,RowCount=7,ColumnCount=1,Padding=new Padding(16)};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int row=0;row<7;row++)layout.RowStyles.Add(new RowStyle(row==3?SizeType.Percent:SizeType.AutoSize,row==3?100:0));Controls.Add(layout);
        layout.Controls.Add(SectionHeader(L.Text("MacroEditSection")),0,0);
        var settings=Fields(3);settings.Padding=new Padding(6);settings.Margin=new Padding(0,0,0,6);
        Field(settings,0,0,L.Text("MacroSlotLabel"),slots);Field(settings,1,0,L.Text("Name"),name);Field(settings,2,0,L.Text("Repeat"),repeat);
        Field(settings,0,1,L.Text("Trigger"),trigger);Field(settings,1,1,L.Text("Interval"),interval);
        layout.Controls.Add(settings,0,1);
        trigger.Items.AddRange(new object[]{L.Text("Once"),L.Text("Toggle"),L.Text("Hold")});trigger.SelectedIndex=0;
        var hint=new Label{AutoSize=true,Text=L.Text("OnceHint"),Anchor=AnchorStyles.Left,Margin=new Padding(8,0,0,0)};settings.Controls.Add(hint,2,1);
        trigger.SelectedIndexChanged+=(_,_)=>{interval.Enabled=trigger.SelectedIndex!=0;hint.Text=trigger.SelectedIndex switch {1=>L.Text("ToggleHint"),2=>L.Text("HoldHint"),_=>L.Text("OnceHint")};};
        interval.Enabled=false;
        var help=new Label {Text=L.Text("EditorHint"),AutoSize=true,MaximumSize=new Size(880,0),Margin=new Padding(0,0,0,10)};
        tips.SetToolTip(help,L.Text("MacroHelp")+"\n"+L.Text("MouseHelp"));layout.Controls.Add(help,0,2);
        grid.DataError += (_,e) => { e.ThrowException=false; status.Text=L.Text("InvalidAction"); };
        grid.Columns.Add(new RGridComboBoxColumn {Name="Action",HeaderText=L.Text("Action"),DataSource=new[]{"Down","Up","Delay"}});
        grid.Columns.Add(new RGridComboBoxColumn {Name="Key",HeaderText=L.Text("Key"),ToolTipText=L.Text("KeyHint"),FillWeight=180,DataSource=KeyChoices(),DisplayMember="Label",ValueMember="Value"});
        grid.Columns.Add(new DataGridViewTextBoxColumn {Name="Milliseconds",HeaderText=L.Text("Delay"),ToolTipText=L.Text("DelayInputHint")}); layout.Controls.Add(grid,0,3);
        grid.BorderStyle=BorderStyle.None;grid.CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal;
        grid.AllowUserToResizeRows=false;grid.RowTemplate.Height=44;grid.Margin=Padding.Empty;
        grid.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None;
        grid.ColumnHeadersDefaultCellStyle.Font=sectionHeaders[0].Font;
        grid.ColumnHeadersDefaultCellStyle.Padding=new Padding(8,0,8,0);
        grid.ColumnHeadersDefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleLeft;
        grid.Columns[2].DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleRight;
        grid.DefaultCellStyle.Padding=new Padding(6,2,6,2);
        foreach(DataGridViewColumn column in grid.Columns)column.SortMode=DataGridViewColumnSortMode.NotSortable;
        grid.EditingControlShowing+=(_,e)=>{if(e.Control is TextBox box){box.KeyPress-=DelayKeyPress;box.KeyPress+=DelayKeyPress;}};
        grid.CellValidating+=ValidateCell;
        grid.CellParsing+=(_,e)=>{if(e.ColumnIndex==2){e.Value=ParseDelay(Convert.ToString(e.Value)??"",Convert.ToString(grid.Rows[e.RowIndex].Cells[0].Value)=="Delay");e.ParsingApplied=true;}};
        grid.CurrentCellDirtyStateChanged+=(_,_)=>{if(grid.CurrentCell is RGridComboBoxCell && grid.IsCurrentCellDirty)grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};
        grid.CellValueChanged+=(_,e)=>{if(e.RowIndex>=0 && e.ColumnIndex is 0 or 1)UpdateKeyCell(grid.Rows[e.RowIndex]);};
        grid.RowsAdded+=(_,e)=>{for(int i=e.RowIndex;i<e.RowIndex+e.RowCount;i++)UpdateKeyCell(grid.Rows[i]);};
        var toolbar=new TableLayoutPanel{AutoSize=false,Height=84,Dock=DockStyle.Top,ColumnCount=1,RowCount=2,Margin=new Padding(0,8,0,10)};
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent,50));toolbar.RowStyles.Add(new RowStyle(SizeType.Percent,50));
        var actions=ButtonGrid(5);toolbar.Controls.Add(actions);
        Add(actions,L.Text("MoveUp"),()=>MoveStep(-1)); Add(actions,L.Text("MoveDown"),()=>MoveStep(1));
        Add(actions,L.Text("InsertStep"),InsertStep);
        Add(actions,L.Text("DeleteStep"),DeleteStep);
        Add(actions,L.Text("CopyExample"),()=>{grid.Rows.Clear(); grid.Rows.Add("Down","LControlKey",0);grid.Rows.Add("Down","C",0);grid.Rows.Add("Up","C",50);grid.Rows.Add("Up","LControlKey",0);});
        var files=ButtonGrid(5);toolbar.Controls.Add(files);
        Add(files,L.Text("Save"),()=>Save());Add(files,L.Text("Import"),Import); Add(files,L.Text("Export"),Export);
        Add(files,L.Text("ClearSlot"),()=>{AppConfig.Set($"macro_{slots.SelectedIndex}","null");Program.inputDispatcher.RegisterKeys();LoadSlot();status.Text=L.Text("Cleared");});
        Add(files,L.Text("Stop"),()=>Program.inputDispatcher.Stop()); layout.Controls.Add(toolbar,0,4);
        layout.Controls.Add(SectionHeader(L.Text("BindingSection")),0,5);
        var bottom=new TableLayoutPanel {AutoSize=true,Dock=DockStyle.Fill,ColumnCount=1,Padding=Padding.Empty,Margin=Padding.Empty};
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        var binding=Fields(2);Field(binding,0,0,L.Text("DeviceLabel"),mice);Field(binding,1,0,L.Text("ButtonLabel"),buttons);bottom.Controls.Add(binding);
        var footer=ButtonGrid(5);bind.Dock=DockStyle.Fill;bind.Height=34;bind.Margin=new Padding(0,3,4,3);footer.Controls.Add(bind);
        Add(footer,L.Text("Restore"),()=>Bind(true));bottom.Controls.Add(footer);
        status.Margin=new Padding(0,4,0,0);bottom.Controls.Add(status);layout.Controls.Add(bottom,0,6);
        for(int i=0;i<12;i++) slots.Items.Add(L.Format("Slot", i+1, 13+i)); slots.SelectedIndexChanged+=(_,_)=>LoadSlot(); slots.SelectedIndex=0;
        foreach(var mouse in PeripheralsProvider.SnapshotMice().Where(m=>m.HasButtonBindings())) mice.Items.Add(new MouseItem(mouse));
        mice.SelectedIndexChanged+=(_,_)=>{buttons.Items.Clear(); if(mice.SelectedItem is MouseItem item) foreach(var pair in item.Mouse.ButtonSlots.Where(p=>p.Key!=0)) buttons.Items.Add(new ButtonItem(pair.Key,pair.Value.Name)); if(buttons.Items.Count>0) buttons.SelectedIndex=Math.Max(0,buttons.Items.Cast<ButtonItem>().ToList().FindIndex(b=>b.Slot==3));};
        if(mice.Items.Count>0)mice.SelectedIndex=0;
        bind.Enabled=mice.Items.Count>0; bind.Click+=(_,_)=>Bind(false); InitTheme();
        grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;grid.ColumnHeadersHeight=sectionHeaders[0].Height;
        Disposed+=(_,_)=>tips.Dispose();
        Shown+=(_,_)=>AlignInputs();
        DpiChanged+=(_,_)=>BeginInvoke((Action)AlignInputs);
    }
    Label SectionHeader(string text)
    {
        var label=new Label{Text=text,Dock=DockStyle.Fill,Height=30,TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(8,0,0,0),Margin=Padding.Empty,Font=new Font(Font,FontStyle.Bold)};
        sectionHeaders.Add(label);return label;
    }
    void AlignInputs()
    {
        int height=inputFrames.Max(pair=>pair.Input.Height);
        foreach(var (frame,input) in inputFrames)
        {
            frame.Height=height;
            PositionInput(frame,input);
        }
    }
    static TableLayoutPanel Fields(int columns)
    {
        var panel=new TableLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,ColumnCount=columns,Margin=Padding.Empty};
        for(int i=0;i<columns;i++)panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/columns));
        return panel;
    }
    void Field(TableLayoutPanel host,int column,int row,string label,Control control)
    {
        var field=new TableLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,ColumnCount=1,Margin=new Padding(6,4,6,6)};
        field.Controls.Add(new Label{Text=label,AutoSize=true,Margin=new Padding(0,0,0,5)});
        if(control is TextBox text)text.BorderStyle=BorderStyle.None;
        control.Width=100;control.Dock=DockStyle.None;control.Margin=Padding.Empty;
        var frame=new Panel{Dock=DockStyle.Top,Height=36,Margin=Padding.Empty};
        frame.Controls.Add(control);inputFrames.Add((frame,control));
        frame.Resize+=(_,_)=>PositionInput(frame,control);
        frame.Paint+=(_,e)=>
        {
            if(control is RComboBox)return;
            e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var bounds=new Rectangle(0,0,frame.Width-1,frame.Height-1);
            using var fill=new SolidBrush(buttonMain);using var border=new Pen(borderMain);
            RComboBox.FillRoundedRectangle(e.Graphics,fill,bounds,4,4);
            RComboBox.DrawRoundedRectangle(e.Graphics,border,bounds,4,4);
        };
        field.Controls.Add(frame);host.Controls.Add(field,column,row);
    }
    static void PositionInput(Panel frame,Control input)
    {
        int padding=input is RComboBox?0:8;
        input.SetBounds(padding,Math.Max(0,(frame.Height-input.Height)/2),Math.Max(1,frame.ClientSize.Width-padding*2),input.Height);
    }
    static TableLayoutPanel ButtonGrid(int columns)
    {
        var panel=Fields(columns);panel.AutoSize=false;panel.Height=40;panel.Dock=DockStyle.Top;panel.GrowStyle=TableLayoutPanelGrowStyle.FixedSize;panel.RowCount=1;
        panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));return panel;
    }
    record KeyChoice(string Value,string Label);
    static KeyChoice[] KeyChoices()
    {
        var result=new List<KeyChoice>{new("",""),new("LButton",L.Text("MouseLeft")),new("RButton",L.Text("MouseRight"))};
        foreach(var (_,label) in AsusMouse.KeyboardBindings)
        {
            // F13–F24 are reserved as macro triggers, not output keys.
            if(label.StartsWith('F') && int.TryParse(label[1..],out int function) && function>=13)continue;
            string key=label switch
            {
                "-"=>"OemMinus","="=>"Oemplus","["=>"OemOpenBrackets",
                "]"=>"OemCloseBrackets",";"=>"OemSemicolon","'"=>"OemQuotes",
                ","=>"Oemcomma","."=>"OemPeriod","/"=>"OemQuestion",
                "Page Up"=>"PageUp","Page Down"=>"PageDown","Num ."=>"Decimal",
                "Left Ctrl"=>"LControlKey","Right Ctrl"=>"RControlKey",
                "Left Shift"=>"LShiftKey","Right Shift"=>"RShiftKey",
                "Left Alt"=>"LMenu","Right Alt"=>"RMenu",
                "Left Win"=>"LWin","Right Win"=>"RWin",
                _ when label.StartsWith("Num ")=>"NumPad"+label[4..],
                _=>MacroFiles.NormalizeKey(label)
            };
            result.Add(new(MacroStore.ParseKey(key).ToString(),label));
        }
        return result.ToArray();
    }
    internal static int ParseDelay(string value,bool delay)
    {
        value=value.Trim();
        if(value.Length==0&&!delay)return 0;
        if(value.Length==0 || value.Any(c=>c<'0'||c>'9') || !int.TryParse(value,out int ms))throw new ArgumentException(L.Text("IntegerDelay"));
        if(ms<(delay?1:0)||ms>60000)throw new ArgumentException(L.Text(delay?"DelayLimits":"ActionDelayLimits"));
        return ms;
    }
    void DelayKeyPress(object? sender,KeyPressEventArgs e)
    {
        if(grid.CurrentCell?.ColumnIndex==2 && !char.IsControl(e.KeyChar) && (e.KeyChar<'0'||e.KeyChar>'9'))e.Handled=true;
    }
    void ValidateCell(object? sender,DataGridViewCellValidatingEventArgs e)
    {
        var row=grid.Rows[e.RowIndex];if(row.IsNewRow)return;
        try
        {
            if(e.ColumnIndex==2)ParseDelay(Convert.ToString(e.FormattedValue)??"",Convert.ToString(row.Cells[0].Value)=="Delay");
            if(e.ColumnIndex==1 && Convert.ToString(row.Cells[0].Value)!="Delay" && string.IsNullOrWhiteSpace(Convert.ToString(e.FormattedValue)))throw new ArgumentException(L.Text("KeyHint"));
            row.ErrorText="";
        }
        catch(ArgumentException ex){e.Cancel=true;row.ErrorText=ex.Message;status.Text=ex.Message;}
    }
    protected override void OnThemeApplied()
    {
        foreach(var header in sectionHeaders)header.BackColor=buttonSecond;
        foreach(var (frame,_) in inputFrames){frame.BackColor=formBack;frame.Invalidate();}
        grid.BackgroundColor=buttonMain;
        grid.DefaultCellStyle.BackColor=buttonMain;
        grid.DefaultCellStyle.ForeColor=foreMain;
        grid.DefaultCellStyle.SelectionBackColor=buttonSecond;
        grid.DefaultCellStyle.SelectionForeColor=foreMain;
        grid.GridColor=borderMain;
        grid.EnableHeadersVisualStyles=false;
        grid.ColumnHeadersDefaultCellStyle.BackColor=buttonSecond;
        grid.ColumnHeadersDefaultCellStyle.ForeColor=foreMain;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor=buttonSecond;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor=foreMain;
        grid.RowHeadersDefaultCellStyle.BackColor=buttonSecond;
        grid.RowHeadersDefaultCellStyle.ForeColor=foreMain;
    }
    record MouseItem(AsusMouse Mouse) { public override string ToString()=>Mouse.GetDisplayName(); }
    record ButtonItem(int Slot,string Name) { public override string ToString()=>Name; }
    void Add(Control host,string text,Action action) {int index=host.Controls.Count;int count=(host as TableLayoutPanel)?.ColumnCount??1;var b=new RButton{Text=text,AutoSize=true,Dock=DockStyle.Fill,Height=34,MinimumSize=new Size(0,34),Margin=new Padding(index==0?0:4,3,index==count-1?0:4,3),CausesValidation=text!=L.Text("Stop")}; b.Click+=(_,_)=>{try{action();}catch(Exception e){status.Text=e.Message;}};host.Controls.Add(b);}
    void LoadSlot()
    {
        var macro=MacroStore.Get(slots.SelectedIndex); name.Text=macro?.Name??L.Format("MacroName", slots.SelectedIndex+1); repeat.Value=macro?.Repeat??1;trigger.SelectedIndex=(int)(macro?.Trigger??MacroTrigger.Once);interval.Value=macro?.Interval??50;grid.Rows.Clear();
        if(macro!=null)ShowSteps(macro.Steps);
    }
    void UpdateKeyCell(DataGridViewRow row)
    {
        if(updatingStepDefaults)return;
        updatingStepDefaults=true;
        try
        {
            if(string.IsNullOrWhiteSpace(Convert.ToString(row.Cells[0].Value)) && !string.IsNullOrWhiteSpace(Convert.ToString(row.Cells[1].Value)))
                row.Cells[0].Value="Down";
            bool delay=Convert.ToString(row.Cells[0].Value)=="Delay";
            row.Cells[1].ReadOnly=delay;
            if(delay)row.Cells[1].Value="";
            else if(Convert.ToString(row.Cells[0].Value) is "Down" or "Up" && string.IsNullOrWhiteSpace(Convert.ToString(row.Cells[1].Value)))
                row.Cells[1].Value="LButton";
            if(!row.IsNewRow && row.Cells[2].Value==null)row.Cells[2].Value=delay?1:0;
        }
        finally{updatingStepDefaults=false;}
    }
    void InsertStep()
    {
        if(!grid.EndEdit())return;
        int index=grid.CurrentRow?.Index??grid.Rows.Count-1;
        grid.Rows.Insert(index,"Down","",0);
        grid.CurrentCell=grid.Rows[index].Cells[1];
        grid.BeginEdit(true);
    }
    void ShowSteps(IEnumerable<MacroStep> steps)
    {
        // Preserve compatibility with saved macros and formats containing a tap.
        var expanded=MacroStore.EditorSteps(steps);
        var choices=KeyChoices().ToList();
        foreach(var step in expanded.Where(s=>s.Action!="Delay"))
            if(!choices.Any(c=>c.Value==step.Key))choices.Add(new(step.Key,step.Key));
        ((RGridComboBoxColumn)grid.Columns[1]).DataSource=choices.ToArray();
        foreach(var step in expanded)grid.Rows.Add(step.Action,step.Key,step.Milliseconds);
    }
    void MoveStep(int offset)
    {
        if(!grid.EndEdit() || grid.CurrentRow is not {IsNewRow:false} row)return;
        int target=row.Index+offset;
        if(target<0 || target>=grid.NewRowIndex)return;
        var values=row.Cells.Cast<DataGridViewCell>().Select(cell=>cell.Value).ToArray();
        grid.Rows.Remove(row);
        grid.Rows.Insert(target,values);
        grid.CurrentCell=grid.Rows[target].Cells[0];
    }
    void DeleteStep()
    {
        if(!grid.EndEdit() || grid.CurrentRow is not {IsNewRow:false} row)return;
        int index=row.Index;
        grid.Rows.Remove(row);
        grid.CurrentCell=grid.Rows[Math.Min(index,grid.NewRowIndex)].Cells[0];
    }
    MacroDefinition ReadEditor()
    {
        if(!grid.EndEdit())throw new ArgumentException(L.Text("DelayInputHint")); var steps=new List<MacroStep>();
        foreach(DataGridViewRow row in grid.Rows) {if(row.IsNewRow)continue;string action=Convert.ToString(row.Cells[0].Value)??"";int ms=ParseDelay(Convert.ToString(row.Cells[2].Value)??"",action=="Delay");steps.Add(new MacroStep(action,Convert.ToString(row.Cells[1].Value)??"",ms));}
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
        ShowSteps(macro.Steps);
        status.Text=L.Format("Imported", macro.Steps.Count);
    }
    void Export()
    {
        var macro=ReadEditor();
        using var dialog=new SaveFileDialog{Filter=MacroFiles.Filter,FileName="macro",DefaultExt="json",AddExtension=true,Title=L.Text("ExportTitle")};
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        bool gmac=Path.GetExtension(dialog.FileName).Equals(".gmac",StringComparison.OrdinalIgnoreCase);
        if(gmac && MessageBox.Show(this,L.Text("GmacExportWarning"),"GMAC",MessageBoxButtons.YesNo,MessageBoxIcon.Information)!=DialogResult.Yes)return;
        string content=MacroFiles.Export(macro,Path.GetExtension(dialog.FileName));
        File.WriteAllText(dialog.FileName,content,new System.Text.UTF8Encoding(false));
        status.Text=L.Text(gmac?"GmacExported":"Exported");
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
