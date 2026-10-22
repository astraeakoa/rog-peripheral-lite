using GHelper.Peripherals;
namespace GHelper;
// Deterministic rendering smoke test. Does not inject input or write device settings.
internal static class UiSmoke
{
    public static void Run()
    {
        ApplicationConfiguration.Initialize();
        Program.settingsForm=new MainForm();
        Program.inputDispatcher=new InputDispatcher(listen:false);
        if(Environment.GetEnvironmentVariable("ROG_PERIPHERAL_UI_MANUAL")=="1")
        {
            using var preview=new MacroEditor();
            Application.Run(preview);Program.inputDispatcher.Dispose();Program.settingsForm.Dispose();return;
        }
        string output=Path.Combine(AppContext.BaseDirectory,"ui-smoke");Directory.CreateDirectory(output);
        var timer=new System.Windows.Forms.Timer {Interval=4000};
        timer.Tick+=(_,_)=>
        {
            timer.Stop();
            try
            {
                if(Program.inputDispatcher.Errors.Count>0)throw new InvalidOperationException(string.Join("; ",Program.inputDispatcher.Errors));
                Capture(Program.settingsForm,"main");
                using var editor=new MacroEditor();editor.Show();Application.DoEvents();Capture(editor,"macros");
                var macroGrid=Descendants(editor).OfType<DataGridView>().Single();
                var keyColumn=(GHelper.UI.RGridComboBoxColumn)macroGrid.Columns[1];
                var keyLabels=keyColumn.Items.Select(item=>keyColumn.ItemText(item,keyColumn.DisplayMember)).ToArray();
                var expectedKeys=GHelper.Peripherals.Mouse.AsusMouse.KeyboardBindings.Where(pair=>!(pair.Item2.StartsWith('F')&&int.TryParse(pair.Item2[1..],out int number)&&number>=13)).Select(pair=>pair.Item2);
                if(!expectedKeys.All(label=>keyLabels.Contains(label)) || keyLabels.Contains("BrowserBack") || keyLabels.Contains("F13"))
                    throw new InvalidOperationException("Key menu must match mouse Keyboard bindings and exclude reserved triggers.");
                if(((GHelper.UI.RGridComboBoxColumn)macroGrid.Columns[0]).Items.Contains("Press"))
                    throw new InvalidOperationException("Press must not be offered in the editor.");
                int newIndex=macroGrid.NewRowIndex;
                macroGrid.CurrentCell=macroGrid.Rows[newIndex].Cells[1];macroGrid.BeginEdit(true);
                ((GHelper.UI.RGridComboBoxEditingControl)macroGrid.EditingControl).SelectedValue="RButton";
                macroGrid.EndEdit();
                if(!Equals(macroGrid.Rows[newIndex].Cells[0].Value,"Down") || !Equals(macroGrid.Rows[newIndex].Cells[1].Value,"RButton"))
                    throw new InvalidOperationException("Choosing a key first must default to Down without overwriting the chosen key.");
                macroGrid.Rows.RemoveAt(newIndex);
                newIndex=macroGrid.NewRowIndex;
                macroGrid.CurrentCell=macroGrid.Rows[newIndex].Cells[0];macroGrid.BeginEdit(true);
                ((GHelper.UI.RGridComboBoxEditingControl)macroGrid.EditingControl).SelectedItem="Up";
                macroGrid.EndEdit();
                if(!Equals(macroGrid.Rows[newIndex].Cells[0].Value,"Up") || !Equals(macroGrid.Rows[newIndex].Cells[1].Value,"LButton"))
                    throw new InvalidOperationException("Choosing an action first must default to the left mouse button.");
                macroGrid.Rows.RemoveAt(newIndex);
                int downRow=macroGrid.Rows.Add("Down","A",50);
                int delayRow=macroGrid.Rows.Add("Delay","",50);
                var clickedCell=macroGrid.Rows[downRow].Cells[1];
                typeof(GHelper.UI.RGridComboBoxCell).GetMethod("OnMouseClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(clickedCell,[new DataGridViewCellMouseEventArgs(1,downRow,10,10,new MouseEventArgs(MouseButtons.Left,1,10,10,0))]);
                if(macroGrid.EditingControl is not GHelper.UI.RGridComboBoxEditingControl keyEditor)
                    throw new InvalidOperationException("Step keys must use the settings dropdown.");
                if(!keyEditor.DroppedDown)throw new InvalidOperationException("Clicking a step cell must open its dropdown.");
                CheckDropdownFace(keyEditor);
                var expectedBounds=((GHelper.UI.RGridComboBoxCell)clickedCell).DropDownBounds(macroGrid.GetCellDisplayRectangle(1,downRow,false));
                var actualBounds=new Rectangle(macroGrid.PointToClient(keyEditor.PointToScreen(Point.Empty)),keyEditor.Size);
                if(actualBounds!=expectedBounds)throw new InvalidOperationException($"Dropdown moved: expected {expectedBounds}, actual {actualBounds}.");
                keyEditor.DroppedDown=true;Application.DoEvents();keyEditor.DroppedDown=false;
                keyEditor.DroppedDown=true;Application.DoEvents();keyEditor.DroppedDown=false;
                keyEditor.DroppedDown=true;
                keyEditor.SelectedValue="RButton";
                keyEditor.DroppedDown=false;
                Application.DoEvents();
                var selectedBounds=new Rectangle(macroGrid.PointToClient(keyEditor.PointToScreen(Point.Empty)),keyEditor.Size);
                if(selectedBounds!=expectedBounds)throw new InvalidOperationException($"Selection moved dropdown: {selectedBounds}, expected {expectedBounds}.");
                CheckDropdownFace(keyEditor);
                if(keyEditor.GetItemHeight(1)>keyEditor.Font.Height+Math.Max(2,keyEditor.DeviceDpi/96*2))throw new InvalidOperationException("Popup rows are not compact.");
                if(!macroGrid.EndEdit() || !Equals(macroGrid.Rows[downRow].Cells[1].Value,"RButton"))
                    throw new InvalidOperationException("Themed key dropdown failed to commit its selection.");
                macroGrid.CurrentCell=macroGrid.Rows[downRow].Cells[0];macroGrid.BeginEdit(true);
                ((ComboBox)macroGrid.EditingControl).DroppedDown=true;Application.DoEvents();
                ((ComboBox)macroGrid.EditingControl).DroppedDown=false;
                ((GHelper.UI.RGridComboBoxEditingControl)macroGrid.EditingControl).SelectedItem="Up";
                if(!macroGrid.EndEdit() || !Equals(macroGrid.Rows[downRow].Cells[0].Value,"Up"))
                    throw new InvalidOperationException("Themed action dropdown failed to commit its selection.");
                if(macroGrid.Rows[downRow].Cells[2].ReadOnly || macroGrid.Rows[delayRow].Cells[2].ReadOnly)
                    throw new InvalidOperationException("All action rows must allow waiting time.");
                if(!macroGrid.Rows[delayRow].Cells[1].ReadOnly || macroGrid.Rows[downRow].Cells[1].ReadOnly)
                    throw new InvalidOperationException("Only Delay rows must disable key editing.");
                macroGrid.Rows[downRow].Cells[0].Value="Delay";
                if(!macroGrid.Rows[downRow].Cells[1].ReadOnly || !string.IsNullOrEmpty(Convert.ToString(macroGrid.Rows[downRow].Cells[1].Value)))
                    throw new InvalidOperationException("Switching to Delay must clear and disable the key.");
                macroGrid.Rows[downRow].Cells[0].Value="Up";
                if(macroGrid.Rows[downRow].Cells[1].ReadOnly || !Equals(macroGrid.Rows[downRow].Cells[1].Value,"LButton"))
                    throw new InvalidOperationException("Switching back to Up must enable key editing.");
                macroGrid.Rows.RemoveAt(delayRow);macroGrid.Rows.RemoveAt(downRow);
                int insertAnchor=macroGrid.Rows.Add("Up","A",50);
                macroGrid.CurrentCell=macroGrid.Rows[insertAnchor].Cells[0];
                Descendants(editor).OfType<Button>().Single(b=>b.Text==L.Text("InsertStep")).PerformClick();
                if(!Equals(macroGrid.Rows[insertAnchor].Cells[0].Value,"Down") || !Equals(macroGrid.Rows[insertAnchor].Cells[1].Value,"LButton") || !Equals(macroGrid.Rows[insertAnchor+1].Cells[0].Value,"Up"))
                    throw new InvalidOperationException("Insert must place a new action before the selection.");
                macroGrid.CancelEdit();macroGrid.Rows.RemoveAt(insertAnchor+1);macroGrid.Rows.RemoveAt(insertAnchor);
                CheckRowOperations(editor,macroGrid);
                if(Environment.GetEnvironmentVariable("ROG_PERIPHERAL_SMOKE_THEME_CYCLE")=="1")
                {
                    if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ROG_PERIPHERAL_CONFIG_DIR")))
                        throw new InvalidOperationException("Theme cycling requires an isolated config directory.");
                    var original=AppConfig.GetString("ui_mode", "");
                    var grid=Descendants(editor).OfType<DataGridView>().Single();
                    grid.Rows.Add("Down","Z");
                    int rowCount=grid.Rows.Count;
                    try
                    {
                        foreach(var mode in new[]{"dark","light","dark"})
                        {
                            AppConfig.Set("ui_mode",mode);
                            Program.settingsForm.RefreshTheme();editor.RefreshTheme();
                            Application.DoEvents();
                            if(editor.darkTheme!=(mode=="dark") || Program.settingsForm.darkTheme!=(mode=="dark") || grid.BackgroundColor!=GHelper.UI.RForm.buttonMain)
                                throw new InvalidOperationException("Theme did not refresh.");
                            if(grid.Rows.Count!=rowCount || !grid.Rows.Cast<DataGridViewRow>().Any(r=>Equals(r.Cells[1].Value,"Z")))
                                throw new InvalidOperationException("Theme change lost edited steps.");
                        }
                    }
                    finally {AppConfig.Set("ui_mode",original!);Program.settingsForm.RefreshTheme();editor.RefreshTheme();}
                }
                editor.Close();
                foreach(var mouse in PeripheralsProvider.SnapshotMice())
                {
                    using var settings=new AsusMouseSettings(mouse);settings.Show();Application.DoEvents();Capture(settings,"mouse");settings.Close();break;
                }
                File.WriteAllText(Path.Combine(output,"result.txt"),$"UI rendered successfully. Language: {System.Globalization.CultureInfo.CurrentUICulture.Name}; theme: {(Program.settingsForm.darkTheme ? "dark" : "light")}. Theme cycle: {Environment.GetEnvironmentVariable("ROG_PERIPHERAL_SMOKE_THEME_CYCLE") == "1"}.");
            }
            catch(Exception e){Environment.ExitCode=1;File.WriteAllText(Path.Combine(output,"result.txt"),e.ToString());}
            finally { Application.Exit(); }
        };
        timer.Start();Application.Run(Program.settingsForm);Program.inputDispatcher.Dispose();
        void Capture(Form form,string name)
        {
            if(form.FormBorderStyle!=FormBorderStyle.FixedSingle || form.MaximizeBox) throw new InvalidOperationException("Window must have a fixed border and no maximize button.");
            using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(output,name+".png"));
        }
    }
    static IEnumerable<Control> Descendants(Control parent)
    {
        foreach(Control child in parent.Controls)
        {
            yield return child;
            foreach(var nested in Descendants(child))yield return nested;
        }
    }
    // Exercises the real grid/editor and toolbar handlers, but does not replace mouse testing.
    static void CheckRowOperations(Form editor,DataGridView grid)
    {
        void Click(string key)=>Descendants(editor).OfType<Button>().Single(b=>b.Text==L.Text(key)).PerformClick();
        void Expect(bool condition,string message)
        {
            if(!condition)throw new InvalidOperationException(message);
        }
        grid.Rows.Clear();
        Click("DeleteStep");Click("MoveUp");Click("MoveDown");
        Expect(grid.Rows.Count==1,"Empty-list operations must preserve the new-row placeholder.");
        Click("InsertStep");grid.EndEdit();
        Expect(grid.NewRowIndex==1 && Equals(grid.Rows[0].Cells[1].Value,"LButton"),"Insert into an empty list must create a default step.");
        grid.Rows.Add("Up","RButton",40);
        grid.CurrentCell=grid.Rows[0].Cells[2];grid.BeginEdit(true);
        ((TextBox)grid.EditingControl).Text="123";
        Click("MoveDown");
        Expect(Equals(grid.Rows[1].Cells[2].Value,123) && Equals(grid.Rows[0].Cells[1].Value,"RButton"),"Move must commit the current delay and preserve row values.");
        Click("MoveDown");
        Expect(grid.CurrentRow!.Index==1,"Moving the last step down must be a no-op.");
        Click("MoveUp");Click("MoveUp");
        Expect(grid.CurrentRow!.Index==0 && Equals(grid.Rows[0].Cells[2].Value,123),"Move-up and first-row boundary must preserve values.");
        grid.CurrentCell=grid.Rows[1].Cells[0];Click("InsertStep");grid.EndEdit();
        Expect(grid.NewRowIndex==3 && Equals(grid.Rows[2].Cells[0].Value,"Up"),"Middle insertion must preserve the following step.");
        Click("DeleteStep");
        Expect(grid.NewRowIndex==2 && Equals(grid.Rows[1].Cells[0].Value,"Up"),"Deleting the inserted row must restore the original sequence.");
        grid.CurrentCell=grid.Rows[0].Cells[2];grid.BeginEdit(true);
        ((TextBox)grid.EditingControl).Text="-1";
        Click("InsertStep");Click("MoveDown");
        Expect(grid.NewRowIndex==2 && Equals(grid.Rows[0].Cells[2].Value,123),"Invalid delay must block insertion/movement without losing the stored value.");
        grid.CancelEdit();
        grid.CurrentCell=grid.Rows[1].Cells[0];Click("DeleteStep");
        grid.CurrentCell=grid.Rows[0].Cells[0];Click("DeleteStep");
        Expect(grid.Rows.Count==1,"Deleting last/all steps must leave a usable new-row placeholder.");
        grid.CurrentCell=grid.Rows[0].Cells[0];grid.BeginEdit(true);
        ((ComboBox)grid.EditingControl).SelectedItem="Delay";grid.EndEdit();
        Expect(grid.Rows[0].Cells[1].ReadOnly && Equals(grid.Rows[0].Cells[2].Value,1),"A new Delay must have no key and a positive default duration.");
        grid.Rows.Clear();
    }
    static void CheckDropdownFace(GHelper.UI.RGridComboBoxEditingControl editor)
    {
        using var actual=new Bitmap(editor.Width,editor.Height);
        editor.DrawToBitmap(actual,new Rectangle(Point.Empty,editor.Size));
        using var expected=new Bitmap(editor.Width,editor.Height);
        using(var graphics=Graphics.FromImage(expected))
        {
            var dc=graphics.GetHdc();
            try
            {
                using var surface=Graphics.FromHdc(dc);
                surface.Clear(GHelper.UI.RForm.buttonSecond);
                GHelper.UI.RGridComboFace.Paint(surface,new Rectangle(Point.Empty,editor.Size),editor.Text,editor.Font);
            }
            finally{graphics.ReleaseHdc(dc);}
        }
        for(int y=0;y<actual.Height;y++)for(int x=0;x<actual.Width;x++)
            if(actual.GetPixel(x,y)!=expected.GetPixel(x,y))
                throw new InvalidOperationException($"Dropdown face differs from idle renderer at {x},{y}.");
    }
}
