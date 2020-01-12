using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MyTrayApp
{
    public class SysTrayApp : Form
    {
        [STAThread]
        public static void Main()
        {

            Console.WriteLine("HEY Im HERE!!");
            Application.Run(new SysTrayApp());

        }

        private readonly NotifyIcon trayIcon;
        private readonly ContextMenu trayMenu;
        readonly RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
        private readonly string appName = "WinToLinux";

        List<string> uefi = new List<string>();
        List<string> uuid = new List<string>();
        string bootsequence;
        string currentValue;
        int shift;
        readonly Regex regexUUID = new Regex("^(\\{){0,1}[0-9a-fA-F]{8}\\-[0-9a-fA-F]{4}\\-[0-9a-fA-F]{4}\\-[0-9a-fA-F]{4}\\-[0-9a-fA-F]{12}(\\}){0,1}$");

        public SysTrayApp()
        {
            bool isStart = registryKey.GetValue(appName) != null;
            GetMenuItems();

            currentValue = bootsequence ?? uuid.First();
            shift = uuid.Count() - uefi.Count();

            // Create a simple tray menu with only one item.
            trayMenu = new ContextMenu();
            trayMenu.MenuItems.Add("Settings").Enabled = false;
            trayMenu.MenuItems.Add("-");
            trayMenu.MenuItems.Add("Start with system", OnRegisterInStartup).Checked = isStart;
            trayMenu.MenuItems.Add("-");
            trayMenu.MenuItems.Add("Reboot to...").Enabled = false;
            trayMenu.MenuItems.Add("-");

            foreach (var pos in uefi.Select((value, i) => new { i, value }))
            {
                MenuItem item = new MenuItem
                {
                    Checked = uuid[pos.i + shift] == currentValue,
                    Tag = uuid[pos.i + shift],
                    Text = pos.value
                };
                item.Click += OnMenuClick;
                trayMenu.MenuItems.Add(item);
            }

            trayMenu.MenuItems.Add("-");
            trayMenu.MenuItems.Add("Reboot system", OnReboot);
            trayMenu.MenuItems.Add("-");
            trayMenu.MenuItems.Add("Exit", OnExit);

            // Create a tray icon. In this example we use a
            // standard system icon for simplicity, but you
            // can of course use your own custom icon too.

            trayIcon = new NotifyIcon
            {
                Text = "Reboot to Linux",
                Icon = WinToLinux.Properties.Resources.WtL,

                // Add menu to tray icon and show it.
                ContextMenu = trayMenu,
                Visible = true
            };
        }


        private void OnExit(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void OnMenuClick(object sender, EventArgs e)
        {
            string UUID = (sender as MenuItem).Tag.ToString();
            string command = "/C bcdedit.exe /set {fwbootmgr} bootsequence " + UUID + " /addfirst";
            Console.WriteLine(command);

            LaunchCMD(command);

            uuid = new List<string>();
            uefi = new List<string>();

            LaunchCMD("/C bcdedit /enum firmware");

            currentValue = bootsequence ?? uuid.First();
            shift = uuid.Count() - uefi.Count();
            foreach (var pos in uefi.Select((value, i) => new { i, value }))
            {
                trayMenu.MenuItems[pos.i + 6].Checked = uuid[pos.i + shift] == currentValue;
            }
        }

        private void OnRegisterInStartup(object sender, EventArgs e)
        {
            bool isStartup = registryKey.GetValue(appName) == null;

            if (isStartup)
            {
                registryKey.SetValue(appName, Application.ExecutablePath);
                trayMenu.MenuItems[2].Checked = true;
            }
            else
            {
                registryKey.DeleteValue(appName);
                trayMenu.MenuItems[2].Checked = false;
            }
        }

        private void OnReboot(object sender, EventArgs e)
        {
            LaunchCMD("/C shutdown /r /t 0");
        }

        private void GetMenuItems()
        {
            LaunchCMD("/C bcdedit /enum firmware");
        }

        private void LaunchCMD(string command)
        {
            Process build = new Process();
            build.StartInfo.Arguments = command;
            build.StartInfo.FileName = "cmd.exe";

            build.StartInfo.UseShellExecute = false;
            build.StartInfo.RedirectStandardOutput = true;
            build.StartInfo.RedirectStandardError = true;
            build.StartInfo.CreateNoWindow = true;
            build.ErrorDataReceived += Build_ErrorAndDataReceived;
            build.OutputDataReceived += Build_ErrorAndDataReceived;
            build.EnableRaisingEvents = true;
            build.Start();
            build.BeginOutputReadLine();
            build.BeginErrorReadLine();
            build.WaitForExit();
        }

        void Build_ErrorAndDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null && e.Data != "")
            {
                string strMessage = e.Data;
                string[] splited = strMessage.Split(' ');
                splited = splited.Where(address => !string.IsNullOrWhiteSpace(address)).ToArray();
                Match match = regexUUID.Match(splited.Last());

                if (splited[0] == "description")
                {
                    splited = splited.Where(w => w != splited[0]).ToArray();
                    Console.WriteLine(String.Join(" ", splited));
                    uefi.Add(String.Join(" ", splited));
                }

                if (splited[0] == "bootsequence")
                {
                    Console.Write(splited.Last());
                    bootsequence = splited.Last();
                }

                if (splited[0] != "resumeobject" && splited[0] != "bootsequence" && match.Success || splited.Last() == "{bootmgr}")
                {
                    Console.Write(splited.Last());
                    uuid.Add(splited.Last());
                }
            }
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
