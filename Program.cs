using System;
using System.Drawing;
using System.Windows.Forms;

namespace MyTrayApp
{
    public class SysTrayApp : Form
    {
        [STAThread]
        public static void Main()
        {
            Application.Run(new SysTrayApp());
        }

        private NotifyIcon trayIcon;
        private ContextMenu trayMenu;

        public SysTrayApp()
        {
            // Create a simple tray menu with only one item.
            trayMenu = new ContextMenu();
            trayMenu.MenuItems.Add("Reboot to...").Enabled = false;
            trayMenu.MenuItems.Add("-");
            trayMenu.MenuItems.Add("Windows");
            trayMenu.MenuItems.Add("Monjaro");
            trayMenu.MenuItems.Add("-");
            trayMenu.MenuItems.Add("Settings").Enabled = false;
            trayMenu.MenuItems.Add("-");
            trayMenu.MenuItems.Add("Start with system").Checked = true;
            trayMenu.MenuItems.Add("-");
            trayMenu.MenuItems.Add("Exit", OnExit);

            // Create a tray icon. In this example we use a
            // standard system icon for simplicity, but you
            // can of course use your own custom icon too.

            trayIcon = new NotifyIcon();
            trayIcon.Text = "Reboot to Linux";
            trayIcon.Icon = WinToLinux.Properties.Resources.WtL;

            // Add menu to tray icon and show it.
            trayIcon.ContextMenu = trayMenu;
            trayIcon.Visible = true;
        }


        private void OnExit(object sender, EventArgs e)
        {
            Application.Exit();
        }




        // All below for hide a Form
        protected override void OnLoad(EventArgs e)
        {
            Visible = false; // Hide form window.
            ShowInTaskbar = false; // Remove from taskbar.

            base.OnLoad(e);
        }

        protected override void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                // Release the icon resource.
                trayIcon.Dispose();
            }

            base.Dispose(isDisposing);
        }
    }
}
