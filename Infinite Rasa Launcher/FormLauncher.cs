using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Diagnostics;
using System.Net;
using System.Threading;

namespace Infinite_Rasa_Launcher
{
    public partial class FormLauncher : Form
    {
        private String RootIndexUri = "https://launcher.dahrkael.net";
        private String RootIndexList = "index.tri";
        private String FileIndexList = "files.tri";
        
        private String ConfigFile = "irl.conf";
        private Char   RootIndexServerSeparator = '\n';
        private Char   RootIndexServerDataSeparator = ';';


        private List<ServerInfo> Servers;
        private Patcher patcher;
        private LaunchSettings settings;
        private LinkLabel linkSettings;

        /// <summary>
        /// Mono scales the fixed-size background layout by its own default font, which breaks it.
        /// The designer sets this inside InitializeComponent, where the scaling also happens,
        /// so on Unix the value is replaced here. Windows keeps the designer value (Font).
        /// </summary>
        public new AutoScaleMode AutoScaleMode
        {
            get { return base.AutoScaleMode; }
            set { base.AutoScaleMode = LaunchSettings.IsUnix ? AutoScaleMode.None : value; }
        }

        public FormLauncher()
        {
            InitializeComponent();
            Servers = new List<ServerInfo>();
            settings = LaunchSettings.Load();

            // Mono ignores TransparencyKey, so cut the window to the background image's shape instead
            if (LaunchSettings.IsUnix && this.BackgroundImage is Bitmap)
            {
                this.Region = RegionFromAlpha((Bitmap)this.BackgroundImage);
            }

            linkSettings = new LinkLabel();
            linkSettings.Text = "Settings";
            linkSettings.AutoSize = true;
            linkSettings.BackColor = Color.Transparent;
            linkSettings.LinkColor = Color.FromArgb(30, 172, 199);
            linkSettings.ActiveLinkColor = Color.Cyan;
            linkSettings.Location = new Point(110, 380);
            linkSettings.LinkClicked += delegate { ShowSettings(); };
            this.Controls.Add(linkSettings);

            buttonExit.MouseEnter += new EventHandler(buttonExit_MouseEnter);
            buttonExit.MouseLeave += new EventHandler(buttonExit_MouseLeave);
            buttonPlay.MouseEnter += new EventHandler(buttonPlay_MouseEnter);
            buttonPlay.MouseLeave += new EventHandler(buttonPlay_MouseLeave);
            ServerIndexListBox.DoubleClick += new EventHandler(ServerIndexListBox_DoubleClick);

            // always add the user's own server (Settings > Server, default localhost)
            Servers.Add(OwnServer());

            // if theres a config file, get the root index url from it
            if (File.Exists(ConfigFile))
            {
                RootIndexUri = File.ReadAllText(ConfigFile);
            }
            DownloadIndex();
        }

        private void DownloadIndex()
        {
            WebClient Downloader = new WebClient();
            String index = "";

            // Download the game servers list from the root index
            try {
                index = Downloader.DownloadString(new Uri(RootIndexUri + "/" + RootIndexList)); 
            }
            catch(WebException) {}

            // if we got the index parse the game server information
            if (!String.IsNullOrEmpty(index))
            {
                string[] serversData = index.Split(RootIndexServerSeparator);
                foreach (string serverData in serversData)
                {
                    string[] data = serverData.Trim().Split(RootIndexServerDataSeparator);
                    ServerInfo serverInfo = new ServerInfo(data[0], data[3], Int16.Parse(data[4]), data[2], data[1]);
                    Servers.Add(serverInfo);
                }
            }

            else
            {
                pictureBoxNoServersFound.Visible = true;
            }

            // add the server names to the listbox; the own server stays playable even without an index
            foreach(ServerInfo server in Servers)
            {
                ServerIndexListBox.Items.Add(server.Name);
            }
        }

        private static Region RegionFromAlpha(Bitmap image)
        {
            Region region = new Region(Rectangle.Empty);
            for (int y = 0; y < image.Height; y++)
            {
                int x = 0;
                while (x < image.Width)
                {
                    while (x < image.Width && image.GetPixel(x, y).A == 0) { x++; }
                    int start = x;
                    while (x < image.Width && image.GetPixel(x, y).A != 0) { x++; }
                    if (x > start) { region.Union(new Rectangle(start, y, x - start, 1)); }
                }
            }
            return region;
        }

        delegate void RunCallback(String host, Int16 port);

        public void RunGame(String host, Int16 port)
        {
            if (this.pictureBoxTotalBar.InvokeRequired)
            {
                RunCallback d = new RunCallback(RunGame);
                this.Invoke(d, new object[] { host, port });
            }
            else
            {
                try { 
                    GameRunner.Start(settings, host, port.ToString()); 
                }
                catch (Exception ex) 
                { 
                    MessageBox.Show("Error starting " + settings.GameExe + " (" + settings.Mode + " mode):\n" + ex.Message, "Wait"); 
                }
                this.Close();
            }
        }

        private ServerInfo OwnServer()
        {
            String host;
            Int16 port;
            if (!settings.TryGetServer(out host, out port)) { host = "127.0.0.1"; port = 2106; }
            return new ServerInfo("Play on " + host + ":" + port, host, port, "", "");
        }

        private bool ShowSettings()
        {
            using (FormSettings form = new FormSettings(settings))
            {
                if (form.ShowDialog(this) != DialogResult.OK) { return false; }
            }

            // the own server entry follows the settings
            Servers[0] = OwnServer();
            if (ServerIndexListBox.Items.Count > 0)
            {
                // Mono may clear the selection when an item is replaced
                int selected = ServerIndexListBox.SelectedIndex;
                ServerIndexListBox.Items[0] = Servers[0].Name;
                ServerIndexListBox.SelectedIndex = selected;
            }
            return true;
        }

        private void buttonPlay_Click(object sender, EventArgs e)
        {
            if (ServerIndexListBox.SelectedIndex < 0) { return; }

            List<String> problems = settings.Validate();
            if (problems.Count > 0)
            {
                MessageBox.Show(String.Join("\n", problems.ToArray()) + "\n\nPlease check the launcher settings.", "Settings");
                if (!ShowSettings() || settings.Validate().Count > 0) { return; }
            }

            // the patcher works with paths relative to the game directory
            Directory.SetCurrentDirectory(settings.EffectiveGameDirectory);

            ServerIndexListBox.Enabled = false;

            ServerInfo info = Servers[ServerIndexListBox.SelectedIndex];

            patcher = new Patcher(info, FileIndexList);
            patcher.OnChecking         = this.onChecking;
            patcher.OnFailedPatching   = this.onFailedPatching;
            patcher.OnFileProgress     = this.onFileProgress;
            patcher.OnFinishedPatching = this.onFinishedPatching;
            patcher.OnPatchingFile     = this.onPatchingFile;
            patcher.OnProgress         = this.onProgress;

            Thread oThread = new Thread(new ThreadStart(patcher.Run));
            oThread.Start();
        }

        private void buttonExit_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        void buttonExit_MouseLeave(object sender, EventArgs e)
        { this.buttonExit.BackgroundImage = ((System.Drawing.Image)(Properties.Resources.exit)); }


        void buttonExit_MouseEnter(object sender, EventArgs e)
        { this.buttonExit.BackgroundImage = ((System.Drawing.Image)(Properties.Resources.exit2)); }

        void buttonPlay_MouseLeave(object sender, EventArgs e)
        { this.buttonPlay.BackgroundImage = ((System.Drawing.Image)(Properties.Resources.play)); }

        void buttonPlay_MouseEnter(object sender, EventArgs e)
        { this.buttonPlay.BackgroundImage = ((System.Drawing.Image)(Properties.Resources.play2)); }

        private void ServerIndexListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (ServerIndexListBox.SelectedIndex != -1)
            {
                buttonPlay.Visible = true;
            }
        }

        private void ServerIndexListBox_DoubleClick(object sender, EventArgs e)
        {
            // open the server website if available
            if (ServerIndexListBox.SelectedIndex > 0)
            {
                ServerInfo info = Servers[ServerIndexListBox.SelectedIndex];
                if (info.Verify())
                {
                    System.Diagnostics.Process.Start(info.Website);
                }
            }

        }

        delegate void SetVisibleCallback();
        delegate void SetSizeCallback(int size);
        delegate void SetTextCallback(string file);

        private void setTotalBarSize(int size)
        {
            if (this.pictureBoxTotalBar.InvokeRequired)
            {
                SetSizeCallback d = new SetSizeCallback(setTotalBarSize);
                this.Invoke(d, new object[] { size });
            }
            else 
            { 
                pictureBoxTotalBar.Width = size; 
            }
        }

        private void setFileBarSize(int size)
        {
            if (this.pictureBoxProgressBar.InvokeRequired)
            {
                SetSizeCallback d = new SetSizeCallback(setFileBarSize);
                this.Invoke(d, new object[] { size });
            }
            else 
            {
                pictureBoxProgressBar.Width = size; 
            }
        }

        private void showChecking()
        {
            if (this.pictureBoxChecking.InvokeRequired)
            {
                SetVisibleCallback d = new SetVisibleCallback(showChecking);
                this.Invoke(d, new object[] { });
            }
            else
            {
                pictureBoxChecking.Visible = true;
                pictureBoxProgressBar.Visible = false;
                pictureBoxTotalBar.Visible = false;
                pictureBoxPublicServersIndex.Visible = false;
                pictureBoxNoServersFound.Visible = false;
                buttonPlay.Visible = false;
                ServerIndexListBox.Visible = false;
                pictureBoxPatching.Visible = false;
                label1.Visible = false;
                this.Refresh();
            }
        }

        private void showPatching()
        {
            if (this.pictureBoxProgressBar.InvokeRequired)
            {
                SetVisibleCallback d = new SetVisibleCallback(showPatching);
                this.Invoke(d, new object[] { });
            }
            else
            {
                pictureBoxProgressBar.Width = 0;
                pictureBoxProgressBar.Visible = true;
                pictureBoxTotalBar.Visible = true;
                pictureBoxPatching.Visible = true;
                pictureBoxChecking.Visible = false;
                label1.Visible = true;
                this.Refresh();
            }
        }

        private void hidePatching()
        {
            if (this.pictureBoxProgressBar.InvokeRequired)
            {
                SetVisibleCallback d = new SetVisibleCallback(hidePatching);
                this.Invoke(d, new object[] { });
            }
            else
            {
                pictureBoxProgressBar.Visible = false;
                pictureBoxTotalBar.Visible = false;
                pictureBoxPublicServersIndex.Visible = true;
                pictureBoxNoServersFound.Visible = false;
                buttonPlay.Visible = true;
                ServerIndexListBox.Visible = true;
                pictureBoxPatching.Visible = false;
                pictureBoxChecking.Visible = false;
                label1.Visible = false;
                this.Refresh();
            }
        }

        private void setFile(string file)
        {
            if (this.pictureBoxProgressBar.InvokeRequired)
            {
                SetTextCallback d = new SetTextCallback(setFile);
                this.Invoke(d, new object[] { file });
            }
            else
            {
                label1.Text = "Current file: " + file;
            }
        }

        // patcher callbacks
        private void onFailedPatching()
        {
            // called from the patcher thread; dialogs from a non-UI thread crash Mono's X11 backend
            if (this.InvokeRequired)
            {
                this.Invoke(new SetVisibleCallback(onFailedPatching));
                return;
            }

            System.Windows.Forms.DialogResult wat;
            wat = System.Windows.Forms.MessageBox.Show("Selected server doesnt support patching.\nRun anyway?",
                                                       "Warning", 
                                                       System.Windows.Forms.MessageBoxButtons.YesNo);

            if (wat == System.Windows.Forms.DialogResult.Yes)
            {
                RunGame(patcher.Info.Host, patcher.Info.Port);
            }
            hidePatching();
            ServerIndexListBox.Enabled = true;
        }

        private void  onFinishedPatching()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new SetVisibleCallback(onFinishedPatching));
                return;
            }

            setFile("Done");
            ServerIndexListBox.Enabled = true;
            RunGame(patcher.Info.Host, patcher.Info.Port);
        }

        private void  onChecking()
        {
            showChecking();
        }

        private void  onPatchingFile(String filename)
        {
            setFile(filename);
        }

        private void  onFileProgress(Byte percentage)
        {
            setFileBarSize(percentage * 4); // 400 top
        }

        private void  onProgress(Int64 current, Int64 total)
        {
            Int32 percentage = (Int32)(current * 100 / total);
            setTotalBarSize(percentage * 4); // 400 top
        }
    }
}
