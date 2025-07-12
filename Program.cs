using Microsoft.Win32.TaskScheduler;
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
            Application.EnableVisualStyles();
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.Run(new SysTrayApp());
        }

        private readonly NotifyIcon trayIcon;
        private readonly ContextMenuStrip trayMenu;
        private readonly string appName = "WinToLinux";

        private List<string> uefi = new List<string>();
        private List<string> uuid = new List<string>();
        private string bootSequence;
        private string currentValue;
        private int shift;
        private readonly Regex regexUUID = new Regex("^(\\{){0,1}[0-9a-fA-F]{8}\\-[0-9a-fA-F]{4}\\-[0-9a-fA-F]{4}\\-[0-9a-fA-F]{4}\\-[0-9a-fA-F]{12}(\\}){0,1}$");

        public SysTrayApp()
        {
            GetMenuItems();

            currentValue = bootSequence ?? uuid.First();
            shift = uuid.Count() - uefi.Count();

            // Create a simple tray menu with only one item.
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Settings").Enabled = false;
            trayMenu.Items.Add("-");

            var startButton = new ToolStripMenuItem("Start with system");
            startButton.Checked = IsTaskEnabled();
            startButton.Click += OnRegisterInStartup;
            trayMenu.Items.Add(startButton);

            trayMenu.Items.Add("-");
            trayMenu.Items.Add("Reboot to...").Enabled = false;
            trayMenu.Items.Add("-");

            foreach (var pos in uefi.Select((value, i) => new { i, value }))
            {
                var item = new ToolStripMenuItem {
                    CheckOnClick = true,
                    Tag = pos.i + shift < uuid.Count ? uuid[pos.i + shift] : string.Empty,
                    Text = pos.value
                };
                item.Click += OnMenuClick;
                trayMenu.Items.Add(item);
            }

            // Set the initial radio button selection
            SetRadioButtonSelection(currentValue);

            trayMenu.Items.Add("-");
            trayMenu.Items.Add("Reboot system", null, OnReboot);
            trayMenu.Items.Add("-");
            trayMenu.Items.Add("Exit", null, OnExit);

            // Create a tray icon. In this example we use a
            // standard system icon for simplicity, but you
            // can of course use your own custom icon too.

            trayIcon = new NotifyIcon
            {
                Text = "Reboot to Linux",
                Icon = WinToLinux.Properties.Resources.WtL,

                // Add menu to tray icon and show it.
                ContextMenuStrip = trayMenu,
                Visible = true
            };
        }


        private void OnExit(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void OnMenuClick(object sender, EventArgs e)
        {
            if (sender is not ToolStripMenuItem clickedItem || clickedItem.Tag is not string uuid)
                return;

            // Set the boot sequence
            string args = $"/Set {{fwbootmgr}} BootSequence {uuid} /AddFirst";
            var psi = new ProcessStartInfo {
                FileName = "bcdedit",
                Arguments = args,
                CreateNoWindow = true
            };
            Process.Start(psi);
            Console.WriteLine("bcdedit" + args);

            // Update the radio button selection
            SetRadioButtonSelection(uuid);

            // Update the current value
            currentValue = uuid;
        }
        private void SetRadioButtonSelection(string selectedUuid)
        {
            // First, uncheck all boot option menu items
            for (int i = 6; i < trayMenu.Items.Count - 3; i++) // Skip fixed items at start and end
            {
                if (trayMenu.Items[i] is ToolStripMenuItem item && item.Tag is string)
                {
                    item.Checked = false;
                }
            }

            // Then check the selected item
            for (int i = 6; i < trayMenu.Items.Count - 3; i++)
            {
                if (trayMenu.Items[i] is ToolStripMenuItem item &&
                    item.Tag is string itemUUID &&
                    itemUUID == selectedUuid
                    )
                {
                    item.Checked = true;
                    break;
                }
            }
        }
        private void RefreshMenuItems()
        {
            uuid.Clear();
            uefi.Clear();

            GetMenuItems();

            currentValue = bootSequence ?? (uuid.Count > 0 ? uuid.First() : string.Empty);
            shift = uuid.Count - uefi.Count;

            // Update radio button selection
            SetRadioButtonSelection(currentValue);
        }
        private void CreateTask()
        {
            try
            {
                using var ts = new TaskService();
                var td = ts.NewTask();

                td.RegistrationInfo.Description = "WinToLinux. Start on boot";
                td.Triggers.Add(new LogonTrigger());
                td.Actions.Add(new ExecAction(Application.ExecutablePath, null, null));
                td.Principal.RunLevel = TaskRunLevel.Highest;

                td.Settings.StopIfGoingOnBatteries = false;
                td.Settings.DisallowStartIfOnBatteries = false;

                td.Settings.UseUnifiedSchedulingEngine = true;  // why not?

                ts.RootFolder.RegisterTaskDefinition(appName, td);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating startup task: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DeleteTask()
        {
            using var ts = new TaskService();
            if (ts.GetTask(appName) != null)
            {
                ts.RootFolder.DeleteTask(appName);
            }
        }

        private bool IsTaskEnabled()
        {
            using var ts = new TaskService();
            return ts.GetTask(appName) != null;
        }

        private void OnRegisterInStartup(object sender, EventArgs e)
        {
            if (IsTaskEnabled())
            {
                DeleteTask();
                if (trayMenu.Items[2] is ToolStripMenuItem item)
                    item.Checked = false;
            }
            else
            {
                CreateTask();
                if (trayMenu.Items[2] is ToolStripMenuItem item)
                    item.Checked = true;
            }
        }

        private void OnReboot(object sender, EventArgs e)
        {
            var psi = new ProcessStartInfo("shutdown", "/s /t 0");
            psi.CreateNoWindow = true;
            Process.Start(psi);
        }

        private void GetMenuItems()
        {
            var psi = new ProcessStartInfo {
                FileName = "bcdedit",
                Arguments = "/enum firmware",
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var process = new Process { StartInfo = psi };
            process.OutputDataReceived += ParseBCDEditOutput;
            process.Start();
            process.BeginOutputReadLine();
            process.WaitForExit();
        }

        void ParseBCDEditOutput(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data))
                return;

            string strMessage = e.Data;
            string[] splitMsg = strMessage.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (splitMsg.Length == 0)
                return;

            var match = regexUUID.Match(splitMsg.Last());

            if (splitMsg[0] == "description")
            {
                var description = string.Join(" ", splitMsg.Skip(1));
                Console.WriteLine(description);
                uefi.Add(description);
            }
            else if (splitMsg[0] == "bootsequence")
            {
                Console.Write(splitMsg.Last());
                bootSequence = splitMsg.Last();
            }
            else if (splitMsg[0] != "resumeobject" && splitMsg[0] != "bootsequence" &&
                (match.Success || splitMsg.Last() == "{bootmgr}"))
            {
                Console.Write(splitMsg.Last());
                uuid.Add(splitMsg.Last());
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
                trayMenu?.Dispose();
            }

            base.Dispose(isDisposing);
        }
    }
}
