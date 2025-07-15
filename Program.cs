using Microsoft.Win32.TaskScheduler;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MyTrayApp
{
    public partial class SysTrayApp : Form
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
        private const string appName = "WinToLinux";

        private readonly List<string> uefi = [];
        private readonly List<string> uuid = [];
        private readonly List<ToolStripMenuItem> bootOptions = [];
        private string bootSequence;
        private string currentValue;
        private int shift;

        private readonly ToolStripMenuItem startButton = new("Start with system");
        private readonly ToolStripMenuItem rebootToButton = new("Reboot now to...");

        [GeneratedRegex("^(\\{){0,1}[0-9a-fA-F]{8}\\-[0-9a-fA-F]{4}\\-[0-9a-fA-F]{4}\\-[0-9a-fA-F]{4}\\-[0-9a-fA-F]{12}(\\}){0,1}$")]
        private static partial Regex UUIDRegEx();

        public SysTrayApp()
        {
            // Create a simple tray menu with only one item.
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Settings").Enabled = false;
            trayMenu.Items.Add(new ToolStripSeparator());

            startButton.Checked = IsTaskEnabled();
            startButton.Click += OnRegisterInStartup;
            trayMenu.Items.Add(startButton);

            RefreshMenuItems();

            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(rebootToButton);
            trayMenu.Items.Add(new ToolStripSeparator());

            bootOptions.ForEach(item => trayMenu.Items.Add(item));

            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Reboot system", null, OnReboot);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Exit", null, OnExit);

            // Create a tray icon.

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

        private void SetNextBoot(object sender, EventArgs _)
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
        private void SetRadioButtonSelection(string selectedUUID)
        {
            // First, uncheck all boot option menu items
            foreach (var item in bootOptions)
            {
                if (item.Tag is string itemUUID)
                {
                    item.Checked = (itemUUID == selectedUUID);
                }
            }
        }
        private void RefreshMenuItems()
        {
            uuid.Clear();
            uefi.Clear();
            rebootToButton.DropDownItems.Clear();

            GetMenuItems();

            currentValue = bootSequence ?? uuid.FirstOrDefault(string.Empty);
            shift = uuid.Count - uefi.Count;

            foreach (var (value, i) in uefi.Select((value, i) => (value, i)))
            {
                string itemTag = i + shift < uuid.Count ? uuid[i + shift] : string.Empty;

                var bootItem = new ToolStripMenuItem
                {
                    CheckOnClick = true,
                    Tag = itemTag,
                    Text = value
                };
                bootItem.Click += SetNextBoot;

                bootOptions.Add(bootItem);

                var immediateItem = new ToolStripMenuItem
                {
                    Tag = itemTag,
                    Text = value
                };
                immediateItem.Click += (sender, _) =>
                {
                    SetNextBoot(sender, _);
                    OnReboot(sender, _);
                };

                rebootToButton.DropDownItems.Add(immediateItem);
            }

            // Update radio button selection
            SetRadioButtonSelection(currentValue);
        }
        private static void CreateTask()
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

        private static void DeleteTask()
        {
            using var ts = new TaskService();
            if (ts.GetTask(appName) != null)
            {
                ts.RootFolder.DeleteTask(appName);
            }
        }

        private static bool IsTaskEnabled()
        {
            using var ts = new TaskService();
            return ts.GetTask(appName) != null;
        }

        private void OnRegisterInStartup(object _, EventArgs e)
        {
            if (IsTaskEnabled())
            {
                DeleteTask();
                startButton.Checked = false;
            }
            else
            {
                CreateTask();
                startButton.Checked = true;
            }
        }

        private void OnReboot(object sender, EventArgs e)
        {
            var psi = new ProcessStartInfo("shutdown", "/r /t 0");
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

            var match = UUIDRegEx().Match(splitMsg.Last());

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
