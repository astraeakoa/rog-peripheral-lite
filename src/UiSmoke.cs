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
                if(Environment.GetEnvironmentVariable("ROG_PERIPHERAL_SMOKE_THEME_CYCLE")=="1")
                {
                    if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ROG_PERIPHERAL_CONFIG_DIR")))
                        throw new InvalidOperationException("Theme cycling requires an isolated config directory.");
                    var original=AppConfig.GetString("ui_mode", "");
                    var grid=Descendants(editor).OfType<DataGridView>().Single();
                    grid.Rows.Add("Press","Z",0);
                    int rowCount=grid.Rows.Count;
                    try
                    {
                        foreach(var mode in new[]{"dark","light","dark"})
                        {
                            AppConfig.Set("ui_mode",mode);
                            Program.settingsForm.RefreshTheme();editor.RefreshTheme();
                            Application.DoEvents();
                            if(editor.darkTheme!=(mode=="dark") || Program.settingsForm.darkTheme!=(mode=="dark") || grid.BackgroundColor!=GHelper.UI.RForm.formBack)
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
            catch(Exception e){File.WriteAllText(Path.Combine(output,"result.txt"),e.ToString());}
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
}
