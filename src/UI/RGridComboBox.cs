namespace GHelper.UI;

// Use the same themed dropdown as the settings forms while retaining grid navigation.
public sealed class RGridComboBoxColumn : DataGridViewColumn
{
    public RGridComboBoxColumn() : base(new RGridComboBoxCell()) {}
    public object DataSource {get;set;}
    public string DisplayMember {get;set;}="";
    public string ValueMember {get;set;}="";
    public IEnumerable<object> Items=>((System.Collections.IEnumerable)DataSource).Cast<object>();
    public string ItemText(object item,string member)=>string.IsNullOrEmpty(member)?Convert.ToString(item):Convert.ToString(System.ComponentModel.TypeDescriptor.GetProperties(item)[member]?.GetValue(item));
}

public sealed class RGridComboBoxCell : DataGridViewCell
{
    RGridComboBoxColumn Column=>(RGridComboBoxColumn)OwningColumn;
    public override Type ValueType=>typeof(string);
    public override Type FormattedValueType=>typeof(string);
    public override object DefaultNewRowValue=>"";
    public override Type EditType=>typeof(RGridComboBoxEditingControl);
    internal Rectangle DropDownBounds(Rectangle cellBounds)
    {
        int pad=Math.Max(2,DataGridView.DeviceDpi/48);
        return Rectangle.Inflate(cellBounds,-pad,-pad);
    }
    public override void PositionEditingControl(bool setLocation,bool setSize,Rectangle cellBounds,Rectangle cellClip,DataGridViewCellStyle style,bool singleVerticalBorderAdded,bool singleHorizontalBorderAdded,bool isFirstDisplayedColumn,bool isFirstDisplayedRow)
    {
        base.PositionEditingControl(setLocation,setSize,cellBounds,cellClip,style,singleVerticalBorderAdded,singleHorizontalBorderAdded,isFirstDisplayedColumn,isFirstDisplayedRow);
        if(DataGridView.EditingControl is not RGridComboBoxEditingControl editor)return;
        var bounds=DropDownBounds(cellBounds);
        var visible=Rectangle.Intersect(bounds,cellClip);
        DataGridView.EditingPanel.Bounds=visible;
        editor.SetCellBounds(new Rectangle(bounds.X-visible.X,bounds.Y-visible.Y,bounds.Width,bounds.Height));
    }
    protected override void OnMouseClick(DataGridViewCellMouseEventArgs e)
    {
        base.OnMouseClick(e);
        if(e.Button!=MouseButtons.Left || ReadOnly || DataGridView==null)return;
        var grid=DataGridView;
        if(grid.CurrentCell!=this)grid.CurrentCell=this;
        if(grid.BeginEdit(true) && grid.EditingControl is RGridComboBoxEditingControl editor)
        {
            editor.Focus();editor.DroppedDown=true;
        }
    }
    protected override object GetFormattedValue(object value,int rowIndex,ref DataGridViewCellStyle style,System.ComponentModel.TypeConverter valueConverter,System.ComponentModel.TypeConverter formattedConverter,DataGridViewDataErrorContexts context)
    {
        string key=Convert.ToString(value)??"";
        var item=Column.Items.FirstOrDefault(item=>Column.ItemText(item,Column.ValueMember)==key);
        return item==null?key:Column.ItemText(item,Column.DisplayMember);
    }
    public override object ParseFormattedValue(object formattedValue,DataGridViewCellStyle style,System.ComponentModel.TypeConverter formattedConverter,System.ComponentModel.TypeConverter valueConverter)
    {
        string text=Convert.ToString(formattedValue)??"";
        if(text.Length==0)return "";
        var item=Column.Items.FirstOrDefault(item=>Column.ItemText(item,Column.DisplayMember)==text);
        return item==null?throw new FormatException(L.Text("InvalidAction")):Column.ItemText(item,Column.ValueMember);
    }
    public override void InitializeEditingControl(int rowIndex,object initialFormattedValue,DataGridViewCellStyle style)
    {
        base.InitializeEditingControl(rowIndex,initialFormattedValue,style);
        var editor=(RGridComboBoxEditingControl)DataGridView.EditingControl;
        editor.Loading=true;
        editor.DataSource=null;editor.Items.Clear();
        editor.DisplayMember=Column.DisplayMember;editor.ValueMember=Column.ValueMember;
        editor.DataSource=Column.DataSource;
        if(string.IsNullOrEmpty(Column.ValueMember))editor.SelectedItem=Value;
        else editor.SelectedValue=Value??"";
        editor.Loading=false;editor.EditingControlValueChanged=false;
        editor.ApplyCellStyleToEditingControl(style);
    }
    protected override void Paint(Graphics graphics,Rectangle clipBounds,Rectangle cellBounds,int rowIndex,DataGridViewElementStates state,object value,object formattedValue,string errorText,DataGridViewCellStyle style,DataGridViewAdvancedBorderStyle borderStyle,DataGridViewPaintParts parts)
    {
        using var background=new SolidBrush(state.HasFlag(DataGridViewElementStates.Selected)?style.SelectionBackColor:style.BackColor);
        graphics.FillRectangle(background,cellBounds);
        if(ReadOnly)return;
        var bounds=DropDownBounds(cellBounds);
        RGridComboFace.Paint(graphics,bounds,Convert.ToString(formattedValue),style.Font);
    }
}

// One face renderer for idle, focused, expanded and committed states.
internal static class RGridComboFace
{
    internal static void Paint(Graphics graphics,Rectangle bounds,string text,Font font)
    {
        graphics.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        bounds.Width--;bounds.Height--;
        using var fill=new SolidBrush(RForm.buttonMain);using var border=new Pen(RForm.borderMain);
        RComboBox.FillRoundedRectangle(graphics,fill,bounds,3,3);
        RComboBox.DrawRoundedRectangle(graphics,border,bounds,3,3);
        var textBounds=new Rectangle(bounds.X+4,bounds.Y,bounds.Width-26,bounds.Height);
        TextRenderer.DrawText(graphics,text,font,textBounds,RForm.foreMain,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
        int x=bounds.Right-12,y=bounds.Top+bounds.Height/2;
        using var arrow=new SolidBrush(RForm.foreMain);
        graphics.FillPolygon(arrow,new Point[]{new(x-3,y-1),new(x+3,y-1),new(x,y+2)});
    }
}

public sealed class RGridComboBoxEditingControl : RComboBox,IDataGridViewEditingControl
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct PaintInfo
    {
        public IntPtr Dc;public int Erase,Left,Top,Right,Bottom,Restore,Update;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValArray,SizeConst=32)]public byte[] Reserved;
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr BeginPaint(IntPtr window,out PaintInfo paint);
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool EndPaint(IntPtr window,ref PaintInfo paint);
    protected override void WndProc(ref Message message)
    {
        if(message.Msg==0x000F || message.Msg==0x0317 || message.Msg==0x0318)
        {
            // Do not let the native combo redraw the face with different padding/arrow metrics.
            bool begin=message.Msg==0x000F && message.WParam==IntPtr.Zero;
            PaintInfo paint=default;
            IntPtr dc=begin?BeginPaint(Handle,out paint):message.WParam;
            try
            {
                if(dc!=IntPtr.Zero)
                {
                    using var graphics=Graphics.FromHdc(dc);
                    graphics.Clear(RForm.buttonSecond);
                    RGridComboFace.Paint(graphics,ClientRectangle,Text,Font);
                }
            }
            finally{if(begin)EndPaint(Handle,ref paint);}
            message.Result=IntPtr.Zero;return;
        }
        base.WndProc(ref message);
    }
    internal bool Loading;
    Rectangle? cellBounds;
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    public RGridComboBoxEditingControl(){DropDownStyle=ComboBoxStyle.DropDownList;NativeHeight=true;DrawMode=DrawMode.OwnerDrawVariable;MaxDropDownItems=12;IntegralHeight=true;}
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if((e.State & DrawItemState.ComboBoxEdit)!=0)
        {
            RGridComboFace.Paint(e.Graphics,ClientRectangle,Text,Font);
            return;
        }
        base.OnDrawItem(e);
    }
    protected override void OnMeasureItem(MeasureItemEventArgs e)
    {
        // Popup rows follow the compact binding menu; the closed field keeps its own height.
        e.ItemHeight=Font.Height+Math.Max(2,DeviceDpi/96*2);
    }
    internal void SetCellBounds(Rectangle bounds)
    {
        cellBounds=bounds;
        int chrome=Height-(int)SendMessage(Handle,0x0154,new IntPtr(-1),IntPtr.Zero);
        SendMessage(Handle,0x0153,new IntPtr(-1),new IntPtr(Math.Max(1,bounds.Height-chrome)));
        SetBounds(bounds.X,bounds.Y,bounds.Width,bounds.Height);
    }
    protected override void SetBoundsCore(int x,int y,int width,int height,BoundsSpecified specified)
    {
        // Grid commits can otherwise reapply its padded text-editor bounds after selection.
        if(cellBounds is { } fixedBounds){x=fixedBounds.X;y=fixedBounds.Y;width=fixedBounds.Width;height=fixedBounds.Height;}
        base.SetBoundsCore(x,y,width,height,specified);
    }
    public DataGridView EditingControlDataGridView {get;set;}
    public int EditingControlRowIndex {get;set;}
    public bool EditingControlValueChanged {get;set;}
    public bool RepositionEditingControlOnValueChange=>false;
    public Cursor EditingPanelCursor=>Cursors.Default;
    public object EditingControlFormattedValue {get=>Text;set=>Text=Convert.ToString(value);}
    public object GetEditingControlFormattedValue(DataGridViewDataErrorContexts context)=>Text;
    public void ApplyCellStyleToEditingControl(DataGridViewCellStyle style)
    {
        Font=style.Font;BackColor=RForm.buttonMain;ForeColor=RForm.foreMain;
        BorderColor=RForm.borderMain;ButtonColor=RForm.buttonMain;ArrowColor=RForm.foreMain;
    }
    public void PrepareEditingControlForEdit(bool selectAll){}
    public bool EditingControlWantsInputKey(Keys keyData,bool gridWantsKey)=>
        (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.F4 || !gridWantsKey;
    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        if(Loading || EditingControlDataGridView==null)return;
        EditingControlValueChanged=true;EditingControlDataGridView.NotifyCurrentCellDirty(true);
        if(cellBounds is { } bounds)SetCellBounds(bounds);
    }
}
