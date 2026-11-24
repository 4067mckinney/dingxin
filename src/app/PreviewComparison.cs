using System;
using System.Collections;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

internal static class PreviewComparison
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [STAThread]
    private static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception error)
        {
            for (Exception current = error; current != null; current = current.InnerException)
            {
                Console.Error.WriteLine(current.GetType().FullName);
                Console.Error.WriteLine(current.Message);
                Console.Error.WriteLine(current.StackTrace);
            }
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length < 3 || args.Length > 4) throw new ArgumentException("Usage: PreviewComparison <package> <screens> <scratch> [empty|no-results]");
        string output = Path.GetFullPath(args[1]);
        string data = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(data);
        var package = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var state = Create(package, "State");
        var settings = Read(state, "Settings");
        Set(settings, "Dark", true);
        Set(settings, "ReduceMotion", true);
        Set(settings, "HudOpacity", 88);
        string scenario = args.Length == 4 ? args[3] : "populated";
        var rules = scenario == "empty" ? null : FixtureRules(package);
        Set(settings, "AppRules", rules);
        var engine = Create(package, "FocusEngine", Create(package, "StateFile", data), state);
        using (var show = new EventWaitHandle(false, EventResetMode.AutoReset))
        using (var uninstall = new EventWaitHandle(false, EventResetMode.AutoReset))
        using (var window = (Form)Create(package, "MainWindow", engine, show, uninstall, data, false, null))
        {
            window.StartPosition = FormStartPosition.Manual;
            window.Location = new Point(SystemInformation.VirtualScreen.Right + 500, 0);
            window.Opacity = 0;
            window.ShowInTaskbar = false;
            window.Show();
            window.ClientSize = new Size(1120, 790);
            Pump();
            var settingsSections = Field<Control>(window, "settingsSections");
            var categories = Field<Control>(window, "ruleSections");
            var search = Field<TextBox>(window, "appSearch");
            if (scenario == "no-results") search.Text = "不存在的软件";
            foreach (bool dark in new[] { true, false })
            {
                Set(settings, "Dark", dark);
                Call(window, "LoadPreferences");
                Call(window, "Theme", dark);
                string prefix = dark ? "dark" : "light";
                if (scenario == "populated")
                {
                    Call(window, "SelectPage", 4);
                    Set(settingsSections, "SelectedIndex", 0);
                    Capture(window, output, prefix + "-settings.png");
                    Set(settingsSections, "SelectedIndex", 1);
                    Capture(window, output, prefix + "-hud.png");
                }
                Call(window, "SelectPage", 1);
                Set(categories, "SelectedIndex", 1);
                Capture(window, output, prefix + "-apps-" + (scenario == "populated" ? "55" : scenario) + ".png");
            }
            if (scenario == "populated")
            {
                window.Size = window.MinimumSize;
                Capture(window, output, "compact-apps-55.png");
                Call(window, "SelectPage", 4);
                Set(settingsSections, "SelectedIndex", 0);
                Capture(window, output, "compact-settings.png");
            }
        }
        return 0;
    }

    private static IList FixtureRules(Assembly package)
    {
        Type type = package.GetType("StudyFocus.AppRule", true);
        var rules = (IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(type));
        string[] names = { "7-Zip File Manager", "Adobe Acrobat DC", "Adobe Lightroom Classic", "Adobe Photoshop 2023",
            "Audacity", "Blender", "Canva", "DaVinci Resolve", "DeepSeek Harness", "Draw.io", "Everything",
            "Figma", "GitHub Desktop", "Inkscape", "JupyterLab", "Krita", "LibreOffice Calc", "LibreOffice Writer",
            "Microsoft Excel", "Microsoft OneNote", "Microsoft Word", "Notion", "Obsidian", "PowerToys", "PyCharm",
            "QGIS", "RStudio", "SumatraPDF", "TeXstudio", "Typora", "Visual Studio Code", "Zotero" };
        for (int i = 0; i < names.Length; i++)
        {
            string path = Path.Combine(@"C:\StudyFocus-Fixtures", "App" + i, "Fixture" + i + ".exe");
            var app = Activator.CreateInstance(type);
            Set(app, "Name", names[i]);
            Set(app, "Path", path);
            if (i % 4 == 0)
                Set(app, "Members", new System.Collections.Generic.List<string> { Path.ChangeExtension(path, ".helper.exe") });
            rules.Add(app);
        }
        string[] system = { "notepad", "mspaint", "calc", "mmc", "taskmgr", "write", "cmd", "control", "regedt32", "osk" };
        foreach (string executable in system)
        {
            var app = Activator.CreateInstance(type);
            Set(app, "Name", "Windows · " + executable);
            Set(app, "Path", Path.Combine(Environment.SystemDirectory, executable + ".exe"));
            rules.Add(app);
        }
        while (rules.Count < 55)
        {
            var app = Activator.CreateInstance(type);
            Set(app, "Name", "学习工具 " + (rules.Count - 41).ToString("00"));
            Set(app, "Path", Path.Combine(@"C:\StudyFocus-Fixtures", "Study" + rules.Count + ".exe"));
            rules.Add(app);
        }
        return rules;
    }

    private static void Capture(Form window, string output, string file)
    {
        Console.WriteLine(file);
        Pump();
        var page = Field<Control[]>(window, "pages")[Field<int>(window, "page")];
        using (var full = new Bitmap(window.Width, window.Height))
        using (var part = new Bitmap(page.Width, page.Height))
        {
            window.DrawToBitmap(full, new Rectangle(Point.Empty, window.Size));
            page.DrawToBitmap(part, new Rectangle(Point.Empty, page.Size));
            Point offset = page.PointToScreen(Point.Empty) - (Size)window.Location;
            using (var g = Graphics.FromImage(full)) g.DrawImageUnscaled(part, offset);
            full.Save(Path.Combine(output, file), ImageFormat.Png);
        }
    }


    private static void Pump()
    {
        for (int i = 0; i < 4; i++) { Application.DoEvents(); Thread.Sleep(15); }
    }

    private static object Create(Assembly package, string type, params object[] args)
    {
        return Activator.CreateInstance(package.GetType("StudyFocus." + type, true), args);
    }

    private static object Read(object target, string property) { return target.GetType().GetProperty(property, Instance).GetValue(target, null); }
    private static void Set(object target, string property, object value) { target.GetType().GetProperty(property, Instance).SetValue(target, value, null); }
    private static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, Instance).GetValue(target); }
    private static void Call(object target, string method, params object[] parameters) { target.GetType().GetMethod(method, Instance).Invoke(target, parameters); }
}
