using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Infinite_Rasa_Launcher
{
    /// <summary>Dialog to edit LaunchSettings. Built in code so it lays out the same on .NET and Mono.</summary>
    public class FormSettings : Form
    {
        private readonly LaunchSettings settings;

        private ComboBox comboMode;
        private TextBox  textGameDir;
        private TextBox  textGameExe;
        private TextBox  textGameArgs;
        private TextBox  textWorkDir;
        private Label    labelCommand;
        private TextBox  textServer;
        private GroupBox groupWine;
        private TextBox  textWinePath;
        private TextBox  textWinePrefix;
        private GroupBox groupLutris;
        private TextBox  textLutrisCommand;
        private Button   buttonDetect;
        private ComboBox comboLutrisGame;
        private ComboBox comboLutrisConfig;
        private Label    labelLutrisStatus;

        public FormSettings(LaunchSettings settings)
        {
            this.settings = settings;
            BuildLayout();
            LoadValues();
        }

        private void BuildLayout()
        {
            // fixed positions instead of TableLayoutPanel/AutoSize: Mono lays those out very differently
            Text = "Launcher settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;

            int y = 12;

            // general
            comboMode = new ComboBox();
            comboMode.DropDownStyle = ComboBoxStyle.DropDownList;
            comboMode.Items.AddRange(new object[] { LaunchMode.Direct, LaunchMode.Wine, LaunchMode.Lutris });
            comboMode.Width = 150;
            comboMode.SelectedIndexChanged += delegate { UpdateEnabled(); };
            y = AddRow(this, y, "Launch mode:", comboMode, null);

            textGameDir = NewTextBox();
            y = AddRow(this, y, "Game directory:", textGameDir, BrowseFolderButton(textGameDir, "Tabula Rasa folder"));
            y = AddNote(this, y, "Patches are downloaded here. Empty = the launcher's working directory.");

            textServer = new TextBox();
            textServer.Width = 200;
            y = AddRow(this, y, "Server:", textServer, null);
            y = AddNote(this, y, "host:port of your own server. Listed first in the server list (default " + LaunchSettings.DefaultServerAddress + ").");

            // game options, used by every launch mode (in Lutris mode they are written to the game config)
            GroupBox groupGame = NewGroup("Game options (Direct, Wine and Lutris)", y);
            int gy = 22;
            textGameExe = NewTextBox();
            gy = AddRow(groupGame, gy, "Executable:", textGameExe, BrowseFileButton(textGameExe, "Game executable", "Programs (*.exe)|*.exe|All files|*"));
            textGameArgs = NewTextBox();
            Button resetArgs = NewButton("Default");
            resetArgs.Click += delegate
            {
                textGameExe.Text = LaunchSettings.DefaultGameExe;
                textGameArgs.Text = LaunchSettings.DefaultGameArguments;
                textWorkDir.Text = "";
            };
            gy = AddRow(groupGame, gy, "Arguments:", textGameArgs, resetArgs);
            textWorkDir = NewTextBox();
            gy = AddRow(groupGame, gy, "Working dir:", textWorkDir, BrowseFolderButton(textWorkDir, "Working directory"));
            gy = AddNote(groupGame, gy, "Executable and working dir may be relative to the game directory.\n" +
                                        "Empty working dir = the game directory. In arguments, {host},\n" +
                                        "{port} and {server} (host:port) are replaced by the selected server.");
            labelCommand = NewLabel("", Edge, gy - 4, FormWidth - 3 * Edge, 2);
            labelCommand.ForeColor = Color.DarkSlateBlue;
            groupGame.Controls.Add(labelCommand);
            gy += 2 * LineHeight + 4;
            groupGame.Height = gy + 4;
            Controls.Add(groupGame);
            y = groupGame.Bottom + 10;

            EventHandler preview = delegate { UpdateCommandPreview(); };
            textGameDir.TextChanged += preview;
            textGameExe.TextChanged += preview;
            textGameArgs.TextChanged += preview;
            textWorkDir.TextChanged += preview;
            textServer.TextChanged += preview;
            comboMode.SelectedIndexChanged += preview;

            // wine
            groupWine = NewGroup("Wine", y);
            gy = 22;
            textWinePath = NewTextBox();
            textWinePath.TextChanged += preview;
            gy = AddRow(groupWine, gy, "Wine binary:", textWinePath, BrowseFileButton(textWinePath, "Wine binary", "All files|*"));
            textWinePrefix = NewTextBox();
            gy = AddRow(groupWine, gy, "Wine prefix:", textWinePrefix, BrowseFolderButton(textWinePrefix, "WINEPREFIX directory"));
            gy = AddNote(groupWine, gy, "Empty prefix = wine's default (~/.wine or $WINEPREFIX).");
            groupWine.Height = gy + 4;
            Controls.Add(groupWine);
            y = groupWine.Bottom + 10;

            // lutris
            groupLutris = NewGroup("Lutris", y);
            gy = 22;
            textLutrisCommand = NewTextBox();
            buttonDetect = NewButton("Detect games");
            buttonDetect.Click += buttonDetect_Click;
            gy = AddRow(groupLutris, gy, "Lutris command:", textLutrisCommand, buttonDetect);

            comboLutrisGame = new ComboBox();
            comboLutrisGame.DropDownStyle = ComboBoxStyle.DropDownList;
            comboLutrisGame.Width = FieldWidth;
            comboLutrisGame.SelectedIndexChanged += comboLutrisGame_SelectedIndexChanged;
            gy = AddRow(groupLutris, gy, "Lutris game:", comboLutrisGame, null);

            comboLutrisConfig = new ComboBox();
            comboLutrisConfig.DropDownStyle = ComboBoxStyle.DropDown;
            comboLutrisConfig.Width = FieldWidth;
            gy = AddRow(groupLutris, gy, "Game config:", comboLutrisConfig, BrowseFileButton(comboLutrisConfig, "Lutris game config", "Lutris config (*.yml)|*.yml|All files|*"));

            labelLutrisStatus = NewLabel("", FieldX, gy, FieldWidth + ButtonWidth, 1);
            groupLutris.Controls.Add(labelLutrisStatus);
            gy += 22;
            gy = AddNote(groupLutris, gy, "Before each launch, exe, args and working_dir in the game config are\n" +
                                          "set from the game options above. The original is kept once as\n" +
                                          "<config>" + Lutris.BackupSuffix + ". Wine version, prefix and other options\n" +
                                          "are used as configured in Lutris.");
            groupLutris.Height = gy + 4;
            Controls.Add(groupLutris);
            y = groupLutris.Bottom + 10;

            // footer
            Button cancel = NewButton("Cancel");
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(FormWidth - Edge - cancel.Width, y);
            Button ok = NewButton("OK");
            ok.Click += buttonOk_Click;
            ok.Location = new Point(cancel.Left - 8 - ok.Width, y);
            Controls.Add(ok);
            Controls.Add(cancel);
            Label labelFile = NewLabel("Saved to " + LaunchSettings.SettingsFile, Edge, y + 5, ok.Left - 8 - Edge, 1);
            labelFile.AutoEllipsis = true;
            Controls.Add(labelFile);

            AcceptButton = ok;
            CancelButton = cancel;
            ClientSize = new Size(FormWidth, ok.Bottom + Edge);
        }

        private void LoadValues()
        {
            comboMode.SelectedItem = settings.Mode;
            textGameDir.Text = settings.GameDirectory;
            textGameExe.Text = settings.GameExe;
            textGameArgs.Text = settings.GameArguments;
            textWorkDir.Text = settings.WorkingDirectory;
            textServer.Text = settings.ServerAddress;
            textWinePath.Text = settings.WinePath;
            textWinePrefix.Text = settings.WinePrefix;
            textLutrisCommand.Text = settings.LutrisCommand;

            if (!String.IsNullOrEmpty(settings.LutrisGameId))
            {
                // show the saved game until the user runs "Detect games"
                LutrisGame saved = new LutrisGame();
                int id;
                Int32.TryParse(settings.LutrisGameId, out id);
                saved.Id = id;
                saved.Name = settings.LutrisGameName;
                saved.Slug = "saved";
                saved.Runner = "?";
                comboLutrisGame.Items.Add(saved);
                comboLutrisGame.SelectedIndex = 0;
            }
            comboLutrisConfig.Text = settings.LutrisConfigFile;
            UpdateEnabled();
        }

        private void UpdateEnabled()
        {
            LaunchMode mode = comboMode.SelectedItem is LaunchMode ? (LaunchMode)comboMode.SelectedItem : LaunchMode.Direct;
            groupWine.Enabled = mode == LaunchMode.Wine;
            groupLutris.Enabled = mode == LaunchMode.Lutris;
        }

        private void buttonDetect_Click(object sender, EventArgs e)
        {
            String command = textLutrisCommand.Text;
            buttonDetect.Enabled = false;
            labelLutrisStatus.Text = "Asking Lutris for installed games...";
            Cursor = Cursors.WaitCursor;

            // Lutris takes a few seconds to answer; keep the dialog responsive
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<LutrisGame> games = null;
                Exception error = null;
                try { games = Lutris.ListGames(command); }
                catch (Exception ex) { error = ex; }

                // the dialog may be closed while Lutris is still answering
                if (IsDisposed || !IsHandleCreated) { return; }
                try { BeginInvoke((MethodInvoker)delegate { OnGamesDetected(command, games, error); }); }
                catch (InvalidOperationException) { }
            });
        }

        private void OnGamesDetected(String command, List<LutrisGame> games, Exception error)
        {
            Cursor = Cursors.Default;
            buttonDetect.Enabled = true;

            if (error != null)
            {
                labelLutrisStatus.Text = "Detection failed.";
                MessageBox.Show(this, "Could not list Lutris games with \"" + command + "\":\n\n" + error.Message,
                                "Lutris", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            String previousId = settings.LutrisGameId;
            comboLutrisGame.Items.Clear();
            foreach (LutrisGame game in games) { comboLutrisGame.Items.Add(game); }

            // keep the saved choice, else preselect anything that looks like Tabula Rasa
            int select = -1;
            for (int i = 0; i < games.Count; i++)
            {
                if (games[i].Id.ToString() == previousId) { select = i; break; }
                if (select < 0 && games[i].Slug != null && games[i].Slug.Contains("tabula")) { select = i; }
            }
            labelLutrisStatus.Text = games.Count + " installed game(s) found.";
            if (select >= 0) { comboLutrisGame.SelectedIndex = select; }
        }

        private void comboLutrisGame_SelectedIndexChanged(object sender, EventArgs e)
        {
            LutrisGame game = comboLutrisGame.SelectedItem as LutrisGame;
            if (game == null || game.Slug == "saved") { return; }

            String current = comboLutrisConfig.Text;
            List<String> configs = Lutris.FindConfigFiles(game.Slug, textLutrisCommand.Text);
            comboLutrisConfig.Items.Clear();
            foreach (String config in configs) { comboLutrisConfig.Items.Add(config); }

            if (configs.Contains(current))  { comboLutrisConfig.Text = current; }
            else if (configs.Count > 0)     { comboLutrisConfig.SelectedIndex = 0; }
            else                            { comboLutrisConfig.Text = ""; }

            if (configs.Count == 0)     { labelLutrisStatus.Text = "No config file found for \"" + game.Slug + "\". Use Browse."; }
            else if (configs.Count > 1) { labelLutrisStatus.Text = configs.Count + " config files match. Check the selection."; }

            if (textGameDir.Text == "" && !String.IsNullOrEmpty(game.Directory) &&
                File.Exists(Path.Combine(game.Directory, textGameExe.Text)))
            {
                textGameDir.Text = game.Directory;
            }
        }

        /// <summary>The settings as currently entered in the dialog.</summary>
        private LaunchSettings ReadForm()
        {
            LaunchSettings edited = new LaunchSettings();
            edited.GameExe = textGameExe.Text.Trim();
            edited.GameArguments = textGameArgs.Text.Trim();
            edited.WorkingDirectory = textWorkDir.Text.Trim();
            edited.ServerAddress = textServer.Text.Trim();
            edited.Mode = comboMode.SelectedItem is LaunchMode ? (LaunchMode)comboMode.SelectedItem : LaunchMode.Direct;
            edited.GameDirectory = textGameDir.Text.Trim();
            edited.WinePath = textWinePath.Text.Trim();
            edited.WinePrefix = textWinePrefix.Text.Trim();
            edited.LutrisCommand = textLutrisCommand.Text.Trim();
            edited.LutrisConfigFile = comboLutrisConfig.Text.Trim();
            LutrisGame game = comboLutrisGame.SelectedItem as LutrisGame;
            edited.LutrisGameId = game == null ? "" : game.Id.ToString();
            edited.LutrisGameName = game == null ? "" : game.Name;

            return edited;
        }

        private void UpdateCommandPreview()
        {
            if (labelCommand == null || comboMode.SelectedItem == null) { return; }
            LaunchSettings edited = ReadForm();
            String host;
            Int16 port;
            if (!edited.TryGetServer(out host, out port)) { host = "<host>"; }
            String command = Quote(edited.EffectiveExePath) + " " + edited.ExpandArguments(host, port.ToString());
            if (edited.Mode == LaunchMode.Wine) { command = edited.WinePath + " " + command; }
            if (edited.Mode == LaunchMode.Lutris) { command = "Lutris: " + command; }
            labelCommand.Text = "Runs (own server): " + command + "\nin: " + edited.EffectiveWorkingDirectory;
        }

        private static String Quote(String value)
        {
            return value.Contains(" ") ? "\"" + value + "\"" : value;
        }

        private void buttonOk_Click(object sender, EventArgs e)
        {
            LaunchSettings edited = ReadForm();
            List<String> problems = edited.Validate();
            if (problems.Count > 0)
            {
                DialogResult answer = MessageBox.Show(this, String.Join("\n", problems.ToArray()) + "\n\nSave anyway?",
                                                      "Settings", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) { return; }
            }

            settings.Mode = edited.Mode;
            settings.GameDirectory = edited.GameDirectory;
            settings.GameExe = edited.GameExe;
            settings.GameArguments = edited.GameArguments;
            settings.WorkingDirectory = edited.WorkingDirectory;
            settings.ServerAddress = edited.ServerAddress;
            settings.WinePath = edited.WinePath;
            settings.WinePrefix = edited.WinePrefix;
            settings.LutrisCommand = edited.LutrisCommand;
            settings.LutrisGameId = edited.LutrisGameId;
            settings.LutrisGameName = edited.LutrisGameName;
            settings.LutrisConfigFile = edited.LutrisConfigFile;
            try
            {
                settings.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save settings:\n" + ex.Message, "Settings");
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        // layout helpers

        private const int Edge      = 12;
        private const int LabelWidth  = 110;
        private const int FieldX      = Edge + LabelWidth;
        private const int FieldWidth  = 360;
        private const int ButtonWidth = 100;
        private const int RowHeight   = 30;
        private const int LineHeight  = 16;
        private const int FormWidth   = FieldX + FieldWidth + 8 + ButtonWidth + 2 * Edge;

        private static int AddRow(Control parent, int y, String label, Control field, Control button)
        {
            parent.Controls.Add(NewLabel(label, Edge, y + 4, LabelWidth, 1));
            field.Location = new Point(FieldX, y);
            parent.Controls.Add(field);
            if (button != null)
            {
                button.Location = new Point(FieldX + FieldWidth + 8, y - 1);
                parent.Controls.Add(button);
            }
            return y + RowHeight;
        }

        private static int AddNote(Control parent, int y, String text)
        {
            int lines = text.Split('\n').Length;
            Label note = NewLabel(text, FieldX, y - 4, FieldWidth + ButtonWidth, lines);
            note.ForeColor = Color.DimGray;
            parent.Controls.Add(note);
            return y - 4 + lines * LineHeight + 10;
        }

        private static Label NewLabel(String text, int x, int y, int width, int lines)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(x, y);
            label.Size = new Size(width, lines * LineHeight);
            return label;
        }

        private static GroupBox NewGroup(String title, int y)
        {
            GroupBox group = new GroupBox();
            group.Text = title;
            group.Location = new Point(Edge / 2, y);
            group.Width = FormWidth - Edge;
            return group;
        }

        private static TextBox NewTextBox()
        {
            TextBox box = new TextBox();
            box.Width = FieldWidth;
            return box;
        }

        private static Button NewButton(String text)
        {
            Button button = new Button();
            button.Text = text;
            button.Size = new Size(ButtonWidth, 26);
            return button;
        }

        private Button BrowseFolderButton(Control target, String description)
        {
            Button button = NewButton("Browse...");
            button.Click += delegate
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                {
                    dialog.Description = description;
                    if (Directory.Exists(target.Text)) { dialog.SelectedPath = target.Text; }
                    if (dialog.ShowDialog(this) == DialogResult.OK) { target.Text = dialog.SelectedPath; }
                }
            };
            return button;
        }

        private Button BrowseFileButton(Control target, String title, String filter)
        {
            Button button = NewButton("Browse...");
            button.Click += delegate
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = title;
                    dialog.Filter = filter;
                    if (File.Exists(target.Text)) { dialog.InitialDirectory = Path.GetDirectoryName(target.Text); }
                    if (dialog.ShowDialog(this) == DialogResult.OK) { target.Text = dialog.FileName; }
                }
            };
            return button;
        }
    }
}
