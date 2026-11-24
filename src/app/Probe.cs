using System;
using System.Drawing;
using System.Windows.Forms;

internal static class Probe
{
    [STAThread]
    private static void Main(string[] args)
    {
        using (var window = new Form
        {
            Text = args[0], Opacity = 0, ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(SystemInformation.VirtualScreen.Right + 1000, 0)
        })
        using (var timer = new Timer { Interval = 45000 })
        {
            timer.Tick += delegate { window.Close(); };
            window.Controls.Add(new TextBox { Text = "Unsaved work", Dock = DockStyle.Fill });
            timer.Start();
            Application.Run(window);
        }
    }
}
