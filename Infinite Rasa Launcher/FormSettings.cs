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
        private readonly String exeName;

        private ComboBox comboMode;
        private TextBox  textGameDir;
        private GroupBox groupWine;
        private TextBox  textWinePath;
        private TextBox  textWinePrefix;
        private GroupBox groupLutris;
        private TextBox  textLutrisCommand;
        private Button   buttonDetect;
        private ComboBox comboLutrisGame;
        private ComboBox comboLutrisConfig;
        private Label    labelLutrisStatus;

        public FormSettings(LaunchSettings settings, String exeName)
        {
            this.settings = settings;
            this.exeName = exeName;
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
            y = AddRow(this, y, "Game directory:", textGameDir, BrowseFolderButton(textGameDir, "Folder containing " + exeName));
            y = AddNote(this, y, "Folder with " + exeName + ". Patches are downloaded here.\n" +
                                 "Empty = the launcher's working directory.");

            // wine
            groupWine = NewGroup("Wine", y);
            int gy = 22;
            textWinePath = NewTextBox();
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
            gy = AddNote(groupLutris, gy, "Before each launch the game config is rewritten to run " + exeName + "\n" +
                                          "with the selected server (exe, args, working_dir). The original\n" +
                                          "is kept once as <config>" + Lutris.BackupSuffix + ". Wine version, prefix and\n" +
                                          "other options are used as configured in Lutris.");
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

                BeginInvoke((MethodInvoker)delegate { OnGamesDetected(command, games, error); });
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
                File.Exists(Path.Combine(game.Directory, exeName)))
            {
                textGameDir.Text = game.Directory;
            }
        }

        private void buttonOk_Click(object sender, EventArgs e)
        {
            LaunchSettings edited = new LaunchSettings();
            edited.Mode = (LaunchMode)comboMode.SelectedItem;
            edited.GameDirectory = textGameDir.Text.Trim();
            edited.WinePath = textWinePath.Text.Trim();
            edited.WinePrefix = textWinePrefix.Text.Trim();
            edited.LutrisCommand = textLutrisCommand.Text.Trim();
            edited.LutrisConfigFile = comboLutrisConfig.Text.Trim();
            LutrisGame game = comboLutrisGame.SelectedItem as LutrisGame;
            edited.LutrisGameId = game == null ? "" : game.Id.ToString();
            edited.LutrisGameName = game == null ? "" : game.Name;

            List<String> problems = edited.Validate(exeName);
            if (problems.Count > 0)
            {
                DialogResult answer = MessageBox.Show(this, String.Join("\n", problems.ToArray()) + "\n\nSave anyway?",
                                                      "Settings", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) { return; }
            }

            settings.Mode = edited.Mode;
            settings.GameDirectory = edited.GameDirectory;
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
