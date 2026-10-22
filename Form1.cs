using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace CyberWishMachine
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }
    }

    public class Form1 : Form
    {
        private static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

        // 资源根目录：程序目录下的 Assets，四个资源子目录都放在其中
        private readonly string _resourcesFolder;

        private readonly string _deitiesFolder;

        private readonly string _offeringsFolder;

        private readonly string _backgroundFolder;

        // 上贡日志
        private readonly string _logFolder;
        private string _logFile;
        private int _incenseCount;
        private bool _logFailed;

        private string _currentDeityFile;

        private string _currentOfferingFile;

        // Deity controls
        private FlatListBox _lstDeities;

        private PictureBox _pbDeity;

        private Label _lblDeities;

        private Label _lblOfferings;

        private Label _sep;

        private FlatTextBox _txtDeityName;

        private FlatTextBox _txtDeityTitle;

        private FlatTextBox _txtWish;

        // Offering controls (3 slots)
        private FlatListBox _lstOfferings;

        private PictureBox[] _pbOfferingSlots;

        private ComboBox[] _cmbOfferingSlots;

        // Buttons
        private Button _btnDeityNew;

        private Button _btnDeityDelete;

        private Button _btnOfferingNew;

        private Button _btnOfferingDelete;

        private Button _btnAutoIncense;

        private Button _btnIncense;

        // Animation
        private List<MeritOverlayForm> _activeOverlays;

        private Timer _animTimer;

        private static readonly Color TXT_COLOR = Color.Black;

        // Auto offering
        private Timer _autoIncenseTimer;

        private bool _autoIncenseActive;

        // 自动上贡倍速（1~50）与切换快捷键（默认 F10）
        private const int SPEED_MIN = 1;
        private const int SPEED_MAX = 50;
        private int _autoIncenseSpeed = 1;
        private Keys _autoIncenseHotkey = Keys.F10;

        // 速度条：右栏底部的自绘滑块
        private FlatSlider _tbSpeed;
        private Label _lblSpeed;
        private bool _loadingSettings;

        // 设置：侧栏收起状态与列表透明背景（由齿轮菜单切换）
        private Button _btnSettings;
        private bool _btnSettingsShouldShow;   // 齿轮是否应显示（自身状态，不受窗口是否已显示影响）

        // 全局随机源
        private static readonly Random _rnd = new Random();
        private bool _leftCollapsed;
        private bool _rightCollapsed;
        private bool _transparentLists = true;

        // ================================================================
        //  Localization - 界面语言（中文 / English）
        // ================================================================
        // 三档模式存 Assets\ui.ini 的 language 键：auto / zh / en。
        // 只翻译界面文字；磁盘上的素材名（自己、白饭…）是用户数据，不翻译。
        internal const string LangAuto = "auto";
        internal const string LangZh = "zh";
        internal const string LangEn = "en";

        // 静态字段：界面文字、对话框、日志共用
        internal static bool UiEnglish;

        private string _languageMode = LangAuto;

        private static string T(string zh, string en)
        {
            return UiEnglish ? en : zh;
        }

        private static bool IsSystemEnglish()
        {
            try
            {
                CultureInfo c = CultureInfo.CurrentUICulture;
                if (c == null) return false;
                return string.Equals(c.TwoLetterISOLanguageName, "en", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        // 必须在创建控件前决定语言
        private void ApplyLanguageSetting()
        {
            _languageMode = ReadLanguageMode();
            UiEnglish = _languageMode == LangEn
                || (_languageMode == LangAuto && IsSystemEnglish());
        }

        private string ReadLanguageMode()
        {
            try
            {
                string ini = Path.Combine(_resourcesFolder, "ui.ini");
                if (!File.Exists(ini)) return LangAuto;
                foreach (string raw in File.ReadAllLines(ini, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    if (!string.Equals(line.Substring(0, eq).Trim(), "language", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string v = line.Substring(eq + 1).Trim().ToLowerInvariant();
                    return (v == LangZh || v == LangEn) ? v : LangAuto;
                }
            }
            catch
            {
                // 设置文件坏了就按跟随系统
            }
            return LangAuto;
        }

        // 上贡来源标识：仅内部判断用
        private const string AutoIncenseMode = "\u81ea\u52a8";

        private const int CENTER_X = 168;

        private const int CENTER_W = 364;

        private static readonly Color PH_COLOR = Color.FromArgb(160, 160, 160);

        public Form1() : this(Application.StartupPath)
        {
        }

        // 允许显式指定程序目录，默认取 exe 所在目录
        internal Form1(string appDir)
        {
            _resourcesFolder = Path.Combine(appDir, "Assets");
            _deitiesFolder = Path.Combine(_resourcesFolder, "Deities");
            _offeringsFolder = Path.Combine(_resourcesFolder, "Offerings");
            _backgroundFolder = Path.Combine(_resourcesFolder, "background");
            _logFolder = Path.Combine(_resourcesFolder, "logs");

            _pbOfferingSlots = new PictureBox[3];
            _cmbOfferingSlots = new ComboBox[3];

            // 语言先定下来
            ApplyLanguageSetting();
            InitializeComponent();
            ApplyWindowIcon();
            _loadingSettings = true;
            LoadSettings();
            // 设置载入后回填到控件（此时控件已存在）
            if (_tbSpeed != null) _tbSpeed.Value = _autoIncenseSpeed;
            if (_lblSpeed != null) _lblSpeed.Text = SpeedLabelText();
            _loadingSettings = false;
            // 先铺内嵌的初始素材，再按目录加载界面资源
            ApplyEmbeddedInitialAssets();
            LoadBackground();
            EnsureDefaults();
            StartIncenseLog();
            LoadDeities();
            LoadOfferings();
            InitOfferingSlots();
            ApplyListTransparency();

            this.Resize += Form1_Resize;
            Form1_Resize(this, EventArgs.Empty);
            FormClosing += (s, e) =>
            {
                if (_autoIncenseTimer != null) { _autoIncenseTimer.Stop(); _autoIncenseTimer.Dispose(); _autoIncenseTimer = null; }
                // 停掉飘字动画定时器
                if (_animTimer != null) { _animTimer.Stop(); _animTimer.Dispose(); _animTimer = null; }
                WriteLog(T("会话结束，本次共上贡 ", "Session ended, offered ") + _incenseCount + T(" 次", " times"));
                if (_pbDeity.Image != null) _pbDeity.Image.Dispose();
                for (int i = 0; i < 3; i++)
                    if (_pbOfferingSlots[i].Image != null)
                        _pbOfferingSlots[i].Image.Dispose();
                if (this.BackgroundImage != null)
                    this.BackgroundImage.Dispose();

                // 关掉仍在屏幕上的飘字窗口
                if (_activeOverlays != null)
                {
                    for (int i = _activeOverlays.Count - 1; i >= 0; i--)
                    {
                        var ov = _activeOverlays[i];
                        if (ov == null || ov.IsDisposed) continue;
                        ov.Close();
                        ov.Dispose();
                    }
                    _activeOverlays.Clear();
                }
            };
        }
        private void InitializeComponent()
        {
            ClientSize = new Size(700, 480);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = true;      // 标题栏保留最小化按钮，游戏里挂机时可一键缩到任务栏
            MinimumSize = new Size(200, 240);
            DoubleBuffered = true;

            // 内容区几何：取自 CENTER_X / CENTER_W
            const int leftX = 10, leftW = 150;
            const int centerX = CENTER_X, centerW = CENTER_W;
            const int rightX = 540, rightW = 150;

            // ============ Left panel: Deities ============
            _lblDeities = new Label
            {
                Location = new Point(leftX, 8),
                Size = new Size(leftW, 18),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(Font.FontFamily, 9F, FontStyle.Bold),
                BackColor = Color.Transparent
            };

            _lstDeities = new FlatListBox
            {
                Location = new Point(leftX, 28),
                Size = new Size(leftW, 250),
            };
            _lstDeities.SelectedIndexChanged += LstDeities_SelectedIndexChanged;

            _btnDeityNew = new Button { Location = new Point(leftX, 284), Size = new Size(72, 28) };
            _btnDeityNew.Click += BtnDeityNew_Click;

            _btnDeityDelete = new Button { Location = new Point(leftX + 78, 284), Size = new Size(72, 28) };
            _btnDeityDelete.Click += BtnDeityDelete_Click;

            // ============ Center panel ============
            // Deity name (above image)
            _txtDeityName = new FlatTextBox
            {
                Location = new Point(centerX, 6),
                Size = new Size(centerW, 24),
                TextAlign = HorizontalAlignment.Center,
                Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold)
            };

            // Wish textbox
            _txtWish = new FlatTextBox
            {
                Location = new Point(centerX, 34),
                Size = new Size(centerW, 24),
                TextAlign = HorizontalAlignment.Center
            };

            // Deity image 180x180
            _pbDeity = new PictureBox
            {
                Location = new Point(centerX + (centerW - 180) / 2, 64),
                Size = new Size(180, 176),
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.None,
                BackColor = Color.Transparent
            };

            // Title textbox (below image)
            _txtDeityTitle = new FlatTextBox
            {
                Location = new Point(centerX, 245),
                Size = new Size(centerW, 24),
                TextAlign = HorizontalAlignment.Center
            };

            // Separator label
            _sep = new Label
            {
                Location = new Point(centerX, 274),
                Size = new Size(centerW, 14),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Gray,
                Font = new Font(Font.FontFamily, 8F),
                BackColor = Color.Transparent
            };

            // 3 offering slots
            int slotW = 104;
            int slotGap = (centerW - 3 * slotW) / 4;
            int slotImgY = 292;
            int slotComboY = 398;

            for (int i = 0; i < 3; i++)
            {
                int sx = centerX + slotGap + i * (slotW + slotGap);

                _pbOfferingSlots[i] = new PictureBox
                {
                    Location = new Point(sx, slotImgY),
                    Size = new Size(slotW, 100),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BorderStyle = BorderStyle.None,
                    BackColor = Color.Transparent
                };

                _cmbOfferingSlots[i] = new ComboBox
                {
                    Location = new Point(sx, slotComboY),
                    Size = new Size(slotW, 21),
                    DropDownStyle = ComboBoxStyle.DropDownList
                };
                int idx = i;
                _cmbOfferingSlots[i].SelectedIndexChanged += (s, e) => OfferingCombo_SelectedIndexChanged(idx);
            }

            // ============ Right panel: Offerings ============
            _lblOfferings = new Label
            {
                Location = new Point(rightX, 8),
                Size = new Size(rightW, 18),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(Font.FontFamily, 9F, FontStyle.Bold),
                BackColor = Color.Transparent,
            };

            _lstOfferings = new FlatListBox
            {
                Location = new Point(rightX, 28),
                Size = new Size(rightW, 250),
            };
            _lstOfferings.SelectedIndexChanged += LstOfferings_SelectedIndexChanged;
            BuildOfferingMenu();   // 右键菜单：重命名贡品

            _btnOfferingNew = new Button { Location = new Point(rightX, 284), Size = new Size(72, 28) };
            _btnOfferingNew.Click += BtnOfferingNew_Click;

            _btnOfferingDelete = new Button { Location = new Point(rightX + 78, 284), Size = new Size(72, 28) };
            _btnOfferingDelete.Click += BtnOfferingDelete_Click;

            // ============ Bottom buttons ============
            _btnAutoIncense = new Button
            {
                Location = new Point(centerX, 440),
                Size = new Size(90, 28),
            };
            _btnAutoIncense.Click += BtnAutoIncense_Click;

            _btnIncense = new Button
            {
                Location = new Point(centerX + centerW - 90, 440),
                Size = new Size(90, 28),
                Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
                ForeColor = Color.DarkRed,
            };
            _btnIncense.Click += BtnIncense_Click;

            // ============ 齿轮设置按钮（右下角） ============
            // 用 Segoe MDL2 Assets 的齿轮字形 EF15；该字体缺失时回落到文字
            bool hasMdl2 = false;
            foreach (var f in FontFamily.Families)
            {
                if (f.Name == "Segoe MDL2 Assets") { hasMdl2 = true; break; }
            }
            _btnSettings = new Button
            {
                Text = hasMdl2 ? "\uE713" : "\u2699",
                Font = hasMdl2 ? new Font("Segoe MDL2 Assets", 12F) : new Font("Segoe UI Symbol", 12F),
                Size = new Size(28, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(90, 90, 90),
                TabStop = false,
                Cursor = Cursors.Hand,
                // 锚定右下角：无论布局代码是否运行过，齿轮都贴在右下角
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Location = new Point(
                    Math.Max(0, ClientSize.Width - 28 - 6),
                    Math.Max(0, ClientSize.Height - 28 - 6)),
            };
            _btnSettings.FlatAppearance.BorderSize = 0;
            _btnSettings.Click += BtnSettings_Click;

            // ============ 右栏底部：自动上贡速度条 ============
            _lblSpeed = new Label
            {
                Location = new Point(rightX, 318),
                Size = new Size(rightW, 18),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(Font.FontFamily, 9F, FontStyle.Bold),
                BackColor = Color.Transparent,
                ForeColor = Color.White,
            };

            _tbSpeed = new FlatSlider
            {
                Location = new Point(rightX, 336),
                Size = new Size(rightW, 24),
                Minimum = SPEED_MIN,
                Maximum = SPEED_MAX,
                Value = _autoIncenseSpeed,
            };
            _tbSpeed.ValueChanged += (s, e) => SpeedChanged();

            // ============ Add controls ============
            Controls.AddRange(new Control[]
            {
                _lblDeities, _lstDeities, _btnDeityNew, _btnDeityDelete,
                _lblOfferings, _lstOfferings, _btnOfferingNew, _btnOfferingDelete,
                _txtDeityName, _txtWish, _pbDeity, _txtDeityTitle,
                _sep, _lblSpeed, _tbSpeed
            });
            for (int i = 0; i < 3; i++)
            {
                Controls.Add(_pbOfferingSlots[i]);
                Controls.Add(_cmbOfferingSlots[i]);
            }
            Controls.Add(_btnAutoIncense);
            Controls.Add(_btnIncense);
            Controls.Add(_btnSettings);

            // Set transparent background on labels/pictureboxes
            SetControlsTransparent();

            // 头衔与心愿：输入即写回该受供者的文件夹
            _txtDeityTitle.InputTextChanged += TxtDeityTitle_InputTextChanged;
            _txtWish.InputTextChanged += TxtWish_InputTextChanged;

            // 统一铺界面文字
            ApplyTexts();
        }

        // 窗口与任务栏图标（资源名 AppIcon.ico，取不到就保持默认）
        private void ApplyWindowIcon()
        {
            try
            {
                using (Stream s = typeof(Form1).Assembly.GetManifestResourceStream("AppIcon.ico"))
                {
                    if (s == null) return;
                    this.Icon = new Icon(s);
                }
            }
            catch { }
        }

        // 按当前语言铺所有常驻控件的文字
        internal void ApplyTexts()
        {
            Text = T("上贡许愿机", "Offering Wish Machine");

            _lblDeities.Text = T("受供者", "Deities");
            _btnDeityNew.Text = T("新建", "New");
            _btnDeityDelete.Text = T("删除", "Delete");

            _txtDeityName.Placeholder = T("此处输入受供者名称", "Enter deity name");
            _txtWish.Placeholder = T("此处输入心愿", "Enter your wish");
            _txtDeityTitle.Placeholder = T("此处输入头衔", "Enter title");

            _sep.Text = T("───── 贡品 ─────", "───── Offerings ─────");

            _lblOfferings.Text = T("贡品库", "Offerings");
            _btnOfferingNew.Text = T("新建", "New");
            _btnOfferingDelete.Text = T("删除", "Delete");

            _btnAutoIncense.Text = AutoIncenseButtonText();
            _btnIncense.Text = T("上贡", "Offer");

            _lblSpeed.Text = SpeedLabelText();

            BuildOfferingMenu();   // 右键菜单文字也跟着语言走

            // 文案变化后重排一次
            if (_pbDeity != null && _sep != null) Form1_Resize(this, EventArgs.Empty);
        }

        private string SpeedLabelText()
        {
            return T("上贡倍速  x", "Speed  x") + _autoIncenseSpeed;
        }

        private string AutoIncenseButtonText()
        {
            return _autoIncenseActive ? T("停止上贡", "Stop") : T("自动上贡", "Auto");
        }

        private void SetControlsTransparent()
        {
            foreach (Control c in Controls)
            {
                if (c is Label || c is PictureBox || c is FlatListBox)
                {
                    c.BackColor = Color.Transparent;
                }
            }
        }

        // ================================================================
        //  Settings - 齿轮菜单：侧栏收起 / 列表透明背景
        // ================================================================

        private void BtnSettings_Click(object sender, EventArgs e)
        {
            var menu = BuildSettingsMenu();
            menu.Show(_btnSettings, new Point(0, _btnSettings.Height));
        }

        // 独立方法，便于测试直接构造并触发
        internal ContextMenuStrip BuildSettingsMenu()
        {
            var menu = new ContextMenuStrip();
            var miLeft = new ToolStripMenuItem(T("收起左侧栏", "Collapse left panel")) { CheckOnClick = false, Checked = _leftCollapsed };
            var miRight = new ToolStripMenuItem(T("收起右侧栏", "Collapse right panel")) { CheckOnClick = false, Checked = _rightCollapsed };
            var miTrans = new ToolStripMenuItem(T("列表背景透明", "Transparent list background")) { CheckOnClick = false, Checked = _transparentLists };

            miLeft.Click += (s, a) =>
            {
                _leftCollapsed = !_leftCollapsed;
                ApplySideVisibility();
            };
            miRight.Click += (s, a) =>
            {
                _rightCollapsed = !_rightCollapsed;
                ApplySideVisibility();
            };
            miTrans.Click += (s, a) =>
            {
                _transparentLists = !_transparentLists;
                ApplyListTransparency();
            };

            menu.Items.Add(miLeft);
            menu.Items.Add(miRight);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miTrans);
            menu.Items.Add(new ToolStripSeparator());
            var miSpeed = new ToolStripMenuItem(T("上贡倍速　x", "Speed  x") + _autoIncenseSpeed) { CheckOnClick = false };
            miSpeed.Click += (s, a) => { if (_tbSpeed != null) _tbSpeed.Focus(); };
            menu.Items.Add(miSpeed);
            var miHotkey = new ToolStripMenuItem(T("上贡快捷键：", "Offering hotkey: ") + DescribeKey(_autoIncenseHotkey));
            miHotkey.Click += (s, a) => PromptAndSetHotkey();
            menu.Items.Add(miHotkey);

            // ---- 语言 ----
            menu.Items.Add(new ToolStripSeparator());
            var miLang = new ToolStripMenuItem(T("语言", "Language"));
            var miAuto = new ToolStripMenuItem(T("跟随系统", "Follow system")) { CheckOnClick = false, Checked = _languageMode == LangAuto };
            var miZh = new ToolStripMenuItem("中文") { CheckOnClick = false, Checked = _languageMode == LangZh };
            var miEn = new ToolStripMenuItem("English") { CheckOnClick = false, Checked = _languageMode == LangEn };
            miAuto.Click += (s, a) => SetLanguageMode(LangAuto);
            miZh.Click += (s, a) => SetLanguageMode(LangZh);
            miEn.Click += (s, a) => SetLanguageMode(LangEn);
            miLang.DropDownItems.Add(miAuto);
            miLang.DropDownItems.Add(miZh);
            miLang.DropDownItems.Add(miEn);
            menu.Items.Add(miLang);

            return menu;
        }

        // 切换语言：落盘后重铺界面
        internal void SetLanguageMode(string mode)
        {
            if (mode != LangZh && mode != LangEn) mode = LangAuto;
            _languageMode = mode;
            UiEnglish = mode == LangEn || (mode == LangAuto && IsSystemEnglish());
            SaveSettings();
            ApplyTexts();
            ApplyListTransparency();
        }

        // 关闭透明时给列表一个纯色底
        internal void ApplyListTransparency()
        {
            _lstDeities.OpaqueMode = !_transparentLists;
            _lstOfferings.OpaqueMode = !_transparentLists;
        }

        // 依据窗口宽度与收起状态，应用到四个侧栏控件的可见性
        private void ApplySideVisibility()
        {
            bool roomForSides = ClientSize.Width >= 560;
            SetSideVisible(true, roomForSides && !_leftCollapsed);
            SetSideVisible(false, roomForSides && !_rightCollapsed);
            Form1_Resize(this, EventArgs.Empty);
        }

        private void SetSideVisible(bool left, bool visible)
        {
            if (left)
            {
                _lblDeities.Visible = visible;
                _lstDeities.Visible = visible;
                _btnDeityNew.Visible = visible;
                _btnDeityDelete.Visible = visible;
            }
            else
            {
                _lblOfferings.Visible = visible;
                _lstOfferings.Visible = visible;
                _btnOfferingNew.Visible = visible;
                _btnOfferingDelete.Visible = visible;
                // 速度条跟随右栏一起收起
                if (_lblSpeed != null) _lblSpeed.Visible = visible;
                if (_tbSpeed != null) _tbSpeed.Visible = visible;
            }
        }

        // ================================================================
        //  Image loading
        // ================================================================

        // 读进内存再解码，避免占住文件句柄
        private static Image LoadImageUnlocked(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] bytes = new byte[fs.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int n = fs.Read(bytes, read, bytes.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                using (var ms = new MemoryStream(bytes, 0, read))
                using (var raw = Image.FromStream(ms))
                    return new Bitmap(raw);   // 复制一份，脱离对流的依赖
            }
        }

        // ================================================================
        //  Embedded initial assets - exe 内嵌的初始素材
        // ================================================================

        // build.ps1 把 Assets 素材以 "init/<相对路径>" 编进 exe，启动时铺到 Assets 下。
        // Assets\.initpack 记录已铺过的包标识：用户删掉的素材不会复活，换包后才重铺。
        private const string InitPackResourcePrefix = "init/";
        private const string InitPackStampFile = ".initpack";

        private void ApplyEmbeddedInitialAssets()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                string[] names = asm.GetManifestResourceNames();
                if (names.Length == 0) return;

                var packed = names
                    .Where(n => n.StartsWith(InitPackResourcePrefix, StringComparison.Ordinal))
                    .ToArray();
                if (packed.Length == 0) return;

                string stampPath = Path.Combine(_resourcesFolder, InitPackStampFile);
                string stamp = BuildInitPackStamp(asm, packed);
                if (File.Exists(stampPath))
                {
                    try
                    {
                        if (File.ReadAllText(stampPath).Trim() == stamp) return;   // 同一包已铺过
                    }
                    catch { }
                }

                Directory.CreateDirectory(_resourcesFolder);
                foreach (string res in packed)
                {
                    string rel = res.Substring(InitPackResourcePrefix.Length);
                    string dest = Path.Combine(_resourcesFolder, rel.Replace('/', Path.DirectorySeparatorChar));
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        using (Stream src = asm.GetManifestResourceStream(res))
                        {
                            if (src == null) continue;
                            // 先落临时文件再替换，避免中途失败留下半张图
                            string tmp = dest + ".tmp";
                            using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                                src.CopyTo(dst);
                            if (File.Exists(dest)) File.Delete(dest);
                            File.Move(tmp, dest);
                        }
                    }
                    catch { }   // 单个素材写失败不影响启动
                }

                try { File.WriteAllText(stampPath, stamp, new UTF8Encoding(false)); }
                catch { }
            }
            catch
            {
                // 任何异常都不能挡住界面
            }
        }

        // 包标识 = 资源名 + 长度的哈希
        private static string BuildInitPackStamp(Assembly asm, string[] resources)
        {
            var sb = new StringBuilder();
            foreach (string n in resources.OrderBy(x => x, StringComparer.Ordinal))
            {
                long len = 0;
                try
                {
                    using (Stream s = asm.GetManifestResourceStream(n))
                        if (s != null) len = s.Length;
                }
                catch { }
                sb.Append(n).Append(':').Append(len).Append('\n');
            }
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                return BitConverter.ToString(hash).Replace("-", "").Substring(0, 32);
            }
        }

        // ================================================================
        //  Background
        // ================================================================

        private void LoadBackground()
        {
            try
            {
                if (Directory.Exists(_backgroundFolder))
                {
                    var bgFiles = Directory.GetFiles(_backgroundFolder)
                        .Where(f => ImageExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                        .ToArray();
                    if (bgFiles.Length > 0)
                    {
                        this.BackgroundImage = LoadImageUnlocked(bgFiles[0]);
                        this.BackgroundImageLayout = ImageLayout.Stretch;
                        return;
                    }
                }
            }
            catch { }

            // Default: white background
            this.BackColor = Color.White;
            this.BackgroundImage = null;
        }

        // ================================================================
        //  Default generation
        // ================================================================

        // 兜底飘字：受供者没有配 txt 时使用，随语言变化
        private static string DefaultMeritText
        {
            get { return T("功德 +1", "Merit +1"); }
        }

        // 取该受供者文件夹里随机一个 txt；null 表示用默认文字或不要文字效果，bool 表示是否用了默认文字
        private string ResolveMeritText(out bool isDefaultText)
        {
            string name = _lstDeities != null && _lstDeities.SelectedItem != null
                ? _lstDeities.SelectedItem.ToString()
                : null;
            string text = GetDeityResponseText(name);
            isDefaultText = (text == DefaultMeritText);
            return text;
        }

        // 新建受供者时随专属文件夹写入的默认弹出文字
        private static readonly string[] DefaultResponseTexts = { "\u51fa\u91d1", "\u4e0d\u6b6a" };

        // 所有贡品都被删除时自动生成的默认贡品
        private const string DefaultOfferingName = "\u767d\u996d";   // 白饭

        // 默认受供者「自己」：无任何素材时自动生成，形象为纯黑
        private const string DefaultDeityName = "\u81ea\u5df1";              // 自己
        private const string DefaultDeityTitle = "\u6c42\u5df1\u4e0d\u5982\u6c42\u4eba";   // 求己不如求人
        private static readonly string[] DefaultDeityTexts =
        {
            "\u65e0",              // 无
            "\u65e0\u65e0",        // 无无
            "\u65e0\u65e0\u65e0",  // 无无无
        };

        private void EnsureDefaults()
        {
            Directory.CreateDirectory(_deitiesFolder);
            Directory.CreateDirectory(_offeringsFolder);

            if (GetDeityNames().Count == 0)
            {
                // 默认受供者：Assets\Deities\自己\ 内含纯黑形象、头衔与弹出文字
                string dir = DeityFolder(_deitiesFolder, DefaultDeityName);
                Directory.CreateDirectory(dir);
                CreateSolidColorImage(Path.Combine(dir, DefaultDeityName + ".png"), Color.Black, 512, 512);
                File.WriteAllText(Path.Combine(dir, DefaultDeityName + ".meta"), DefaultDeityTitle);
                WriteResponseTexts(dir, DefaultDeityTexts);
            }

            bool hasOfferingImg = Directory.GetFiles(_offeringsFolder)
                .Any(f => ImageExts.Contains(Path.GetExtension(f).ToLowerInvariant()));

            // 所有贡品都被删除时，生成默认贡品「白饭」
            if (!hasOfferingImg)
            {
                string imgPath = Path.Combine(_offeringsFolder, DefaultOfferingName + ".png");
                CreatePlaceholderImage(imgPath, DefaultOfferingName, Color.WhiteSmoke, 128, 128);
            }
        }

        // 为新受供者写入默认弹出文字，使其开箱即用
        private void WriteDefaultResponseTexts(string deityFolder)
        {
            WriteResponseTexts(deityFolder, DefaultResponseTexts);
        }

        private void WriteResponseTexts(string deityFolder, string[] texts)
        {
            for (int i = 0; i < texts.Length; i++)
            {
                string p = Path.Combine(deityFolder, (i + 1) + ".txt");
                if (!File.Exists(p)) File.WriteAllText(p, texts[i]);
            }
        }

        // 纯色图（默认受供者「自己」用纯黑形象）
        private void CreateSolidColorImage(string path, Color color, int w, int h)
        {
            using (var bmp = new Bitmap(w, h))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(color);
                bmp.Save(path, ImageFormat.Png);
            }
        }

        private void CreatePlaceholderImage(string path, string text, Color bgColor, int w, int h)
        {
            using (var bmp = new Bitmap(w, h))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(bgColor);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                float fontSize = Math.Min(w, h) > 180 ? 36F : 24F;
                using (var font = new Font("Microsoft YaHei", fontSize, FontStyle.Bold, GraphicsUnit.Point))
                using (var brush = new SolidBrush(Color.White))
                {
                    var sz = g.MeasureString(text, font);
                    g.DrawString(text, font, brush,
                        (w - sz.Width) / 2,
                        (h - sz.Height) / 2);
                }

                bmp.Save(path, ImageFormat.Png);
            }
        }

        // ================================================================
        //  File helpers
        // ================================================================

        private string FindImageFile(string folder, string name)
        {
            foreach (var ext in ImageExts)
            {
                string f = Path.Combine(folder, name + ext);
                if (File.Exists(f)) return f;
            }
            return null;
        }

        private string[] GetImageFiles(string folder)
        {
            if (!Directory.Exists(folder)) return new string[0];
            return Directory.GetFiles(folder)
                .Where(f => ImageExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => Path.GetFileNameWithoutExtension(f))
                .ToArray();
        }

        // ---------------- 受供者：每人一个专属文件夹 ----------------
        // Assets\Deities\<名字>\  内含  <名字>.<图片扩展名>、<名字>.meta、若干 *.txt（上贡时弹出的文字）

        private static string DeityFolder(string deitiesRoot, string name)
        {
            return Path.Combine(deitiesRoot, name);
        }

        // 该受供者文件夹里代表其形象的图片：优先同名文件，否则取第一张图片
        private string FindDeityImage(string name)
        {
            string dir = DeityFolder(_deitiesFolder, name);
            if (!Directory.Exists(dir)) return null;

            foreach (var ext in ImageExts)
            {
                string same = Path.Combine(dir, name + ext);
                if (File.Exists(same)) return same;
            }
            var any = GetImageFiles(dir);
            return any.Length > 0 ? any[0] : null;
        }

        // 迁移只在进程内做一次
        private bool _legacyMigrated;

        private void MigrateLegacyDeityFilesOnce()
        {
            if (_legacyMigrated) return;
            _legacyMigrated = true;
            MigrateLegacyDeityFiles();
        }

        // 把早期"平铺在 Deities 下"的图片与 meta 收进各自专属文件夹，兼容旧素材
        private void MigrateLegacyDeityFiles()
        {
            if (!Directory.Exists(_deitiesFolder)) return;

            foreach (string f in GetImageFiles(_deitiesFolder))
            {
                string name = Path.GetFileNameWithoutExtension(f);
                try
                {
                    string dir = DeityFolder(_deitiesFolder, name);
                    Directory.CreateDirectory(dir);
                    string dest = Path.Combine(dir, Path.GetFileName(f));
                    if (!string.Equals(f, dest, StringComparison.OrdinalIgnoreCase))
                        File.Move(f, dest);

                    string meta = Path.Combine(_deitiesFolder, name + ".meta");
                    if (File.Exists(meta))
                    {
                        string metaDest = Path.Combine(dir, name + ".meta");
                        if (!string.Equals(meta, metaDest, StringComparison.OrdinalIgnoreCase))
                            File.Move(meta, metaDest);
                    }
                }
                catch { }   // 迁移失败不影响启动
            }
        }

        private List<string> GetDeityNames()
        {
            var names = new List<string>();
            if (!Directory.Exists(_deitiesFolder)) return names;

            foreach (string dir in Directory.GetDirectories(_deitiesFolder))
            {
                string name = Path.GetFileName(dir);
                if (FindDeityImage(name) != null)
                    names.Add(name);
            }
            names.Sort(StringComparer.CurrentCulture);
            return names;
        }

        private string GetTitle(string deityName)
        {
            string meta = Path.Combine(DeityFolder(_deitiesFolder, deityName), deityName + ".meta");
            return File.Exists(meta) ? File.ReadAllText(meta).Trim() : "";
        }

        private void SetTitle(string deityName, string title)
        {
            Directory.CreateDirectory(DeityFolder(_deitiesFolder, deityName));
            string meta = Path.Combine(DeityFolder(_deitiesFolder, deityName), deityName + ".meta");
            string text = title ?? "";
            if (File.Exists(meta) && string.Equals(File.ReadAllText(meta), text, StringComparison.Ordinal)) return;
            File.WriteAllText(meta, text);
        }

        // 每个受供者的心愿单独存一份：<名字>.wish
        private string GetWish(string deityName)
        {
            string path = Path.Combine(DeityFolder(_deitiesFolder, deityName), deityName + ".wish");
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }

        private void SetWish(string deityName, string wish)
        {
            Directory.CreateDirectory(DeityFolder(_deitiesFolder, deityName));
            string path = Path.Combine(DeityFolder(_deitiesFolder, deityName), deityName + ".wish");
            string text = wish ?? "";
            if (File.Exists(path) && string.Equals(File.ReadAllText(path), text, StringComparison.Ordinal)) return;
            File.WriteAllText(path, text);
        }

        // 取该受供者文件夹里随机一个 txt：null 表示用默认文字或不要文字效果。
        // 高倍速下缓存各受供者的 txt 内容，避免频繁读盘。
        private const int MERIT_CACHE_TTL_MS = 300;

        private string _meritCacheName;
        private string[] _meritCacheTexts;
        private long _meritCacheStamp;

        private string GetDeityResponseText(string deityName)
        {
            if (string.IsNullOrEmpty(deityName)) return DefaultMeritText;

            long now = Stopwatch.GetTimestamp();
            bool cacheValid = _meritCacheTexts != null
                && string.Equals(_meritCacheName, deityName, StringComparison.Ordinal)
                && (now - _meritCacheStamp) * 1000.0 / Stopwatch.Frequency < MERIT_CACHE_TTL_MS;
            if (!cacheValid)
            {
                _meritCacheName = deityName;
                _meritCacheTexts = ReadDeityResponseTexts(deityName);
                _meritCacheStamp = now;
            }

            string[] texts = _meritCacheTexts;
            if (texts == null || texts.Length == 0) return DefaultMeritText;   // 没有文件夹或没有 txt
            string picked = texts[_rnd.Next(texts.Length)];
            // 文件夹里确实配了 txt：以它的内容为准，内容为空就是「无文字效果」
            return string.IsNullOrEmpty(picked) ? null : picked;
        }

        // 返回 null 表示「没有文件夹 / 没有 txt」，返回空数组表示「txt 全为空」
        private string[] ReadDeityResponseTexts(string deityName)
        {
            string dir = DeityFolder(_deitiesFolder, deityName);
            if (!Directory.Exists(dir)) return null;
            try
            {
                var files = Directory.GetFiles(dir, "*.txt");
                if (files.Length == 0) return null;

                var texts = new string[files.Length];
                for (int i = 0; i < files.Length; i++)
                    texts[i] = File.ReadAllText(files[i]).Trim();
                return texts;
            }
            catch
            {
                return null;
            }
        }

        // ================================================================
        //  Offering log - 上贡日志
        //  写到 Assets\logs\incense.log，每次上贡追加一行，自动切分旧日志
        // ================================================================

        private const long LOG_MAX_BYTES = 512 * 1024;

        private void StartIncenseLog()
        {
            _logFile = Path.Combine(_logFolder, "incense.log");
            WriteLog(T("会话开始", "Session started"));
        }

        // 追加一行；出错就静默关闭日志
        private bool _logFolderReady;
        private long _logBytes = -1;

        private void WriteLog(string message)
        {
            if (_logFailed || string.IsNullOrEmpty(_logFile)) return;
            try
            {
                if (!_logFolderReady)
                {
                    Directory.CreateDirectory(_logFolder);
                    _logFolderReady = true;
                }

                // 首次写入时读取真实长度，之后按追加字节数自行累加
                if (_logBytes < 0)
                    _logBytes = File.Exists(_logFile) ? new FileInfo(_logFile).Length : 0;

                if (_logBytes > LOG_MAX_BYTES)
                {
                    RotateLog();
                    _logBytes = 0;
                }

                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + "\r\n";
                File.AppendAllText(_logFile, line, new System.Text.UTF8Encoding(false));
                _logBytes += System.Text.Encoding.UTF8.GetByteCount(line);
            }
            catch
            {
                _logFailed = true;   // 写不了就放弃，避免每次上贡都抛异常拖慢界面
            }
        }

        // 切分：incense.log -> incense.1.log（只保留一代，避免无限增长）
        private void RotateLog()
        {
            string bak = Path.Combine(_logFolder, "incense.1.log");
            if (File.Exists(bak)) File.Delete(bak);
            if (File.Exists(_logFile)) File.Move(_logFile, bak);
        }

        // 记录一次上贡
        private void LogIncense(string deityName, string text, string mode, bool defaultText)
        {
            _incenseCount++;
            // mode 是内部标识，日志里换成界面语言
            string modeText = mode == AutoIncenseMode ? T("自动", "auto") : T("手动", "manual");
            WriteLog(string.Format(T("#{0}  [{1}]  受供者={2}  文字={3}{4}",
                    "#{0}  [{1}]  deity={2}  text={3}{4}"),
                _incenseCount, modeText,
                string.IsNullOrEmpty(deityName) ? T("<未选择>", "<none>") : deityName,
                string.IsNullOrEmpty(text) ? T("<无>", "<empty>") : text,
                defaultText ? T("（默认文字）", " (default)") : ""));
        }


        // 保持选中项时不重复触发 SelectedIndexChanged
        private bool _suppressListEvents;
        // 回填输入框期间不写盘
        private bool _syncingDeityFields;

        private void LoadDeities()
        {
            _currentDeityFile = null;
            MigrateLegacyDeityFilesOnce();   // 兼容早期平铺素材：仅在首次加载时收进专属文件夹
            _suppressListEvents = true;
            try
            {
                _lstDeities.Items.Clear();
                foreach (string name in GetDeityNames())
                    _lstDeities.Items.Add(name);
            }
            finally
            {
                _suppressListEvents = false;
            }

            // 先退到 -1 再选，确保首项也会触发加载
            if (_lstDeities.Items.Count > 0)
            {
                _lstDeities.SelectedIndex = -1;
                _lstDeities.SelectedIndex = 0;
            }
            else
            {
                _lstDeities.SelectedIndex = -1;
            }
        }

        private void LoadOfferings()
        {
            _currentOfferingFile = null;
            _suppressListEvents = true;
            try
            {
                _lstOfferings.Items.Clear();
                foreach (string f in GetImageFiles(_offeringsFolder))
                    _lstOfferings.Items.Add(Path.GetFileNameWithoutExtension(f));
            }
            finally
            {
                _suppressListEvents = false;
            }
        }

        private void InitOfferingSlots()
        {
            var names = GetImageFiles(_offeringsFolder)
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .ToArray();

            for (int i = 0; i < 3; i++)
            {
                _cmbOfferingSlots[i].Items.Clear();
                _cmbOfferingSlots[i].Items.AddRange(names);

                if (i < names.Length)
                    _cmbOfferingSlots[i].SelectedIndex = i;
                else if (names.Length > 0)
                    _cmbOfferingSlots[i].SelectedIndex = 0;
            }
        }

        private void RefreshOfferingCombos()
        {
            var names = GetImageFiles(_offeringsFolder)
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .ToArray();

            for (int i = 0; i < 3; i++)
            {
                string prev = _cmbOfferingSlots[i].SelectedItem as string;
                _cmbOfferingSlots[i].Items.Clear();
                _cmbOfferingSlots[i].Items.AddRange(names);

                if (prev != null && names.Contains(prev))
                    _cmbOfferingSlots[i].SelectedItem = prev;
                else if (names.Length > 0)
                    _cmbOfferingSlots[i].SelectedIndex = Math.Min(i, names.Length - 1);
            }
        }

        // ================================================================
        //  Selection handlers
        // ================================================================

        private string CurrentDeityName()
        {
            return _lstDeities != null && _lstDeities.SelectedItem != null
                ? _lstDeities.SelectedItem.ToString()
                : null;
        }

        // 头衔：边打字边存（InputTextChanged 见 FlatTextBox）
        private void TxtDeityTitle_InputTextChanged(object sender, EventArgs e)
        {
            if (_syncingDeityFields) return;
            string name = CurrentDeityName();
            if (string.IsNullOrEmpty(name)) return;
            try { SetTitle(name, _txtDeityTitle.InputText.Trim()); }
            catch { }   // 磁盘不可写时不打扰用户，界面照常可用
        }

        // 心愿：同上
        private void TxtWish_InputTextChanged(object sender, EventArgs e)
        {
            if (_syncingDeityFields) return;
            string name = CurrentDeityName();
            if (string.IsNullOrEmpty(name)) return;
            try { SetWish(name, _txtWish.InputText); }
            catch { }
        }

        private void LstDeities_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_suppressListEvents) return;

            _syncingDeityFields = true;
            try
            {
                if (_lstDeities.SelectedItem == null)
                {
                    if (_pbDeity.Image != null) { _pbDeity.Image.Dispose(); _pbDeity.Image = null; }
                    _txtDeityName.InputText = "";
                    _txtDeityName.ForeColor = PH_COLOR;
                    _txtDeityTitle.InputText = "";
                    _txtDeityTitle.ForeColor = PH_COLOR;
                    _txtWish.InputText = "";
                    _txtWish.ForeColor = PH_COLOR;

                    _currentDeityFile = null;
                    return;
                }

                string name = _lstDeities.SelectedItem.ToString();
                _currentDeityFile = FindDeityImage(name);

                if (_currentDeityFile != null)
                {
                    if (_pbDeity.Image != null) _pbDeity.Image.Dispose();
                    _pbDeity.Image = LoadImageUnlocked(_currentDeityFile);
                    _txtDeityName.InputText = name;

                    string title = GetTitle(name);   // 只读一次盘，避免同一文件读两遍
                    _txtDeityTitle.InputText = title;
                    _txtDeityTitle.ForeColor = string.IsNullOrEmpty(title) ? PH_COLOR : TXT_COLOR;

                    string wish = GetWish(name);
                    _txtWish.InputText = wish;
                    _txtWish.ForeColor = string.IsNullOrEmpty(wish) ? PH_COLOR : TXT_COLOR;
                }
            }
            finally
            {
                _syncingDeityFields = false;
            }
        }

        private void LstOfferings_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_suppressListEvents) return;
            if (_lstOfferings.SelectedItem == null)
            {
                _currentOfferingFile = null;
                return;
            }
            string name = _lstOfferings.SelectedItem.ToString();
            _currentOfferingFile = FindImageFile(_offeringsFolder, name);
        }

        private void OfferingCombo_SelectedIndexChanged(int slotIndex)
        {
            var pb = _pbOfferingSlots[slotIndex];
            string name = _cmbOfferingSlots[slotIndex].SelectedItem as string;
            if (name == null)
            {
                if (pb.Image != null) { pb.Image.Dispose(); pb.Image = null; }
                return;
            }
            string file = FindImageFile(_offeringsFolder, name);
            if (file != null)
            {
                if (pb.Image != null) pb.Image.Dispose();
                pb.Image = LoadImageUnlocked(file);
            }
        }

        // ================================================================
        //  Input dialog helper
        // ================================================================

        private string PromptInput(string caption, string prompt, string defaultValue = "")
        {
            using (var form = new Form
            {
                Text = caption,
                ClientSize = new Size(380, 120),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false
            })
            {
                var lbl = new Label
                {
                    Text = prompt,
                    Location = new Point(12, 12),
                    Size = new Size(350, 20)
                };
                var txt = new TextBox
                {
                    Text = defaultValue,
                    Location = new Point(12, 35),
                    Size = new Size(350, 22)
                };
                var ok = new Button
                {
                    Text = T("确定", "OK"),
                    DialogResult = DialogResult.OK,
                    Location = new Point(190, 70),
                    Size = new Size(80, 25)
                };
                var cancel = new Button
                {
                    Text = T("取消", "Cancel"),
                    DialogResult = DialogResult.Cancel,
                    Location = new Point(278, 70),
                    Size = new Size(80, 25)
                };

                form.Controls.Add(lbl);
                form.Controls.Add(txt);
                form.Controls.Add(ok);
                form.Controls.Add(cancel);
                form.AcceptButton = ok;
                form.CancelButton = cancel;

                return form.ShowDialog(this) == DialogResult.OK ? txt.Text.Trim() : null;
            }
        }

        // ================================================================
        //  New
        // ================================================================

        private void BtnDeityNew_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog
            {
                Filter = T("图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif", "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif"),
                Title = T("选择受供者图片", "Select deity image")
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;

                string name = PromptInput(T("新建受供者", "New deity"), T("请输入受供者名称：", "Deity name:"));
                if (string.IsNullOrEmpty(name)) return;

                if (FindDeityImage(name) != null)
                {
                    MessageBox.Show(T("该名称已存在！", "That name already exists!"), T("提示", "Notice"),
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 每个受供者独占一个文件夹，内含形象图片、头衔与上贡弹出文字
                string dir = DeityFolder(_deitiesFolder, name);
                Directory.CreateDirectory(dir);
                string ext = Path.GetExtension(dlg.FileName);
                File.Copy(dlg.FileName, Path.Combine(dir, name + ext));
                SetTitle(name, "");
                WriteDefaultResponseTexts(dir);

                LoadDeities();
                int idx = _lstDeities.Items.IndexOf(name);
                if (idx >= 0) _lstDeities.SelectedIndex = idx;
            }
        }

        private void BtnOfferingNew_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog
            {
                Filter = T("图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif", "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif"),
                Title = T("选择贡品图片", "Select offering image")
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;

                string name = PromptInput(T("新建贡品", "New offering"), T("请输入贡品名称：", "Offering name:"));
                if (string.IsNullOrEmpty(name)) return;

                if (FindImageFile(_offeringsFolder, name) != null)
                {
                    MessageBox.Show(T("该名称已存在！", "That name already exists!"), T("提示", "Notice"),
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string ext = Path.GetExtension(dlg.FileName);
                string dest = Path.Combine(_offeringsFolder, name + ext);
                File.Copy(dlg.FileName, dest);

                LoadOfferings();
                RefreshOfferingCombos();
            }
        }

        // ================================================================
        //  Delete
        // ================================================================

        private void BtnDeityDelete_Click(object sender, EventArgs e)
        {
            if (_lstDeities.SelectedItem == null) return;

            string name = _lstDeities.SelectedItem.ToString();
            string file = FindDeityImage(name);
            if (file == null) return;

            var result = MessageBox.Show(string.Format(T("确定删除受供者「{0}」？\n其专属文件夹（含形象、头衔、弹出文字）将被删除。",
                    "Delete deity \"{0}\"?\nIts folder (image, title and popping texts) will be removed."), name),
                T("确认删除", "Confirm"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes) return;

            try
            {
                // 该受供者的专属文件夹整体删除（含形象、头衔与其上贡弹出文字）
                string dir = DeityFolder(_deitiesFolder, name);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);

                _currentDeityFile = null;
                LoadDeities();
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(T("删除失败：{0}", "Delete failed: {0}"), ex.Message), T("错误", "Error"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnOfferingDelete_Click(object sender, EventArgs e)
        {
            if (_lstOfferings.SelectedItem == null) return;

            string name = _lstOfferings.SelectedItem.ToString();
            string file = FindImageFile(_offeringsFolder, name);
            if (file == null) return;

            var result = MessageBox.Show(string.Format(T("确定删除贡品「{0}」？\n图片文件将被删除。",
                    "Delete offering \"{0}\"?\nThe image file will be removed."), name),
                T("确认删除", "Confirm"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes) return;

            try
            {
                File.Delete(file);
                _currentOfferingFile = null;
                LoadOfferings();
                RefreshOfferingCombos();
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(T("删除失败：{0}", "Delete failed: {0}"), ex.Message), T("错误", "Error"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ================================================================
        //  Rename - 右键贡品库重命名贡品
        // ================================================================

        // 与齿轮菜单同样的写法
        private ContextMenuStrip _menuOfferings;

        private void BuildOfferingMenu()
        {
            // 菜单只建一次，事件也只挂一次
            if (_menuOfferings == null)
            {
                _menuOfferings = new ContextMenuStrip();
                var rename = new ToolStripMenuItem();
                rename.Click += (s, a) => RenameSelectedOffering();
                _menuOfferings.Items.Add(rename);
                _lstOfferings.ContextMenuStrip = _menuOfferings;

                // 右键先选中光标下的项
                _lstOfferings.MouseDown += (s, e) =>
                {
                    if (e.Button == MouseButtons.Right) _lstOfferings.SelectItemAt(e.Y);
                };
            }
            _menuOfferings.Items[0].Text = T("重命名", "Rename");
        }

        private void RenameSelectedOffering()
        {
            if (_lstOfferings.SelectedItem == null) return;

            string oldName = _lstOfferings.SelectedItem.ToString();
            string oldFile = FindImageFile(_offeringsFolder, oldName);
            if (oldFile == null) return;

            string newName = PromptInput(T("重命名贡品", "Rename offering"),
                string.Format(T("请输入「{0}」的新名称：", "New name for \"{0}\":"), oldName), oldName);
            if (string.IsNullOrEmpty(newName) || newName == oldName) return;

            string newFile = Path.Combine(_offeringsFolder, newName + Path.GetExtension(oldFile));
            if (File.Exists(newFile))
            {
                MessageBox.Show(string.Format(T("已存在名为「{0}」的贡品！", "An offering named \"{0}\" already exists!"), newName), T("提示", "Notice"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string err = ApplyOfferingRename(oldName, newName);
            if (err != null)
            {
                MessageBox.Show(string.Format(T("重命名失败：{0}", "Rename failed: {0}"), err), T("错误", "Error"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 改名后刷新列表与槽位下拉框，尽量保持各槽位原先选中的贡品；返回 null 表示成功
        internal string ApplyOfferingRename(string oldName, string newName)
        {
            string oldFile = FindImageFile(_offeringsFolder, oldName);
            if (oldFile == null) return T("找不到原贡品文件", "Original offering file not found");

            string newFile = Path.Combine(_offeringsFolder, newName + Path.GetExtension(oldFile));
            if (File.Exists(newFile)) return T("已存在同名贡品", "An offering with that name already exists");

            // 记录各槽位当前选中的贡品，重命名后尽量保持
            var slotNames = new string[3];
            for (int i = 0; i < 3; i++)
                slotNames[i] = _cmbOfferingSlots[i].SelectedItem as string;
            bool wasUsed = false;
            for (int i = 0; i < 3; i++)
                if (slotNames[i] == oldName) { slotNames[i] = newName; wasUsed = true; }

            // 同目录改名优先用 Move，失败再退回 Copy+Delete（兼容跨卷等情形）
            try
            {
                File.Move(oldFile, newFile);
            }
            catch
            {
                File.Copy(oldFile, newFile);
                File.Delete(oldFile);
            }

            _currentOfferingFile = null;

            // 先抑制事件再重建下拉框，避免加载已删除的旧文件
            _suppressListEvents = true;
            try
            {
                LoadOfferings();
                RefreshOfferingCombos();
                if (wasUsed)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        if (slotNames[i] == null) continue;
                        int idx = _cmbOfferingSlots[i].Items.IndexOf(slotNames[i]);
                        if (idx < 0) continue;
                        _cmbOfferingSlots[i].SelectedIndex = idx;
                        OfferingCombo_SelectedIndexChanged(i);   // 手动补一次，重新载入槽位图片
                    }
                }
            }
            finally
            {
                _suppressListEvents = false;
            }

            int sel = _lstOfferings.Items.IndexOf(newName);
            if (sel >= 0) _lstOfferings.SelectedIndex = sel;
            return null;
        }

        // ================================================================
        //  Auto Offering - 自动上贡
        // ================================================================

        private const int AUTO_INCENSE_INTERVAL = 250; // 1 倍速：250ms 一次，即每秒 4 次

        // 定时器 15ms 一跳，实际节奏由 Stopwatch 累积推进
        private const int AUTO_INCENSE_TICK_MS = 15;
        private const int MAX_INCENSE_PER_TICK = 24;   // 卡顿后不补做太多，避免雪崩
        // 同屏飘字条数上限：50 倍速下每帧新增数十条，不封顶会拖垮合成
        private const int MAX_MERIT_ITEMS = 160;

        private long _autoIncenseLastTicks;
        private double _autoIncenseAccumMs;

        private double IncenseIntervalMs
        {
            get { return Math.Max(1.0, AUTO_INCENSE_INTERVAL / (double)_autoIncenseSpeed); }
        }

        private void BtnAutoIncense_Click(object sender, EventArgs e)
        {
            ToggleAutoIncense();
        }

        // 按钮与快捷键共用同一个入口
        private void ToggleAutoIncense()
        {
            if (_autoIncenseActive)
            {
                StopAutoIncense();
                return;
            }
            if (_lstDeities.SelectedItem == null)
            {
                MessageBox.Show(T("请先选择受供者！", "Please select a deity first!"), T("提示", "Notice"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _autoIncenseActive = true;
            _btnAutoIncense.Text = T("停止上贡", "Stop");
            if (_autoIncenseTimer == null)
            {
                _autoIncenseTimer = new Timer();
                _autoIncenseTimer.Interval = AUTO_INCENSE_TICK_MS;
                _autoIncenseTimer.Tick += AutoIncenseTick;
            }
            _autoIncenseAccumMs = 0;
            _autoIncenseLastTicks = Stopwatch.GetTimestamp();
            _autoIncenseTimer.Start();
        }

        private void StopAutoIncense()
        {
            _autoIncenseActive = false;
            if (_autoIncenseTimer != null)
            {
                _autoIncenseTimer.Stop();
                _autoIncenseTimer.Dispose();
                _autoIncenseTimer = null;
            }
            if (_btnAutoIncense != null)
                _btnAutoIncense.Text = AutoIncenseButtonText();
        }

        private void AutoIncenseTick(object sender, EventArgs e)
        {
            if (!_autoIncenseActive || _lstDeities.SelectedItem == null)
            {
                StopAutoIncense();
                return;
            }

            long now = Stopwatch.GetTimestamp();
            long delta = now - _autoIncenseLastTicks;
            _autoIncenseLastTicks = now;
            double elapsedMs = delta * 1000.0 / Stopwatch.Frequency;
            if (elapsedMs < 0) elapsedMs = 0;

            _autoIncenseAccumMs += elapsedMs;
            double interval = IncenseIntervalMs;

            // 长时间卡顿后不追补太多：宁可少补，也不要把界面彻底堵死
            double backlogLimit = interval * MAX_INCENSE_PER_TICK;
            if (_autoIncenseAccumMs > backlogLimit) _autoIncenseAccumMs = backlogLimit;

            int due = 0;
            while (_autoIncenseAccumMs >= interval && due < MAX_INCENSE_PER_TICK)
            {
                _autoIncenseAccumMs -= interval;
                due++;
            }
            for (int i = 0; i < due && _autoIncenseActive; i++)
                Incense(AutoIncenseMode);   // 自动
        }

        // ================================================================
        //  Offering - Animation
        // ================================================================

        private void BtnIncense_Click(object sender, EventArgs e)
        {
            if (_lstDeities.SelectedItem == null)
            {
                MessageBox.Show(T("请先选择受供者！", "Please select a deity first!"), T("提示", "Notice"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Incense("\u624b\u52a8");   // 手动（内部标识，不翻译）
        }

        // 一次上贡：先记日志，再飘字
        private void Incense(string mode)
        {
            bool isDefaultText;
            string text = ResolveMeritText(out isDefaultText);

            string deityName = _lstDeities != null && _lstDeities.SelectedItem != null
                ? _lstDeities.SelectedItem.ToString()
                : null;
            LogIncense(deityName, text, mode, isDefaultText);

            ShowMeritAnimation(text, MeritCopiesForCurrentSpeed(mode));
        }

        // 5 倍速以上，一次上贡可能飘出多段文字
        private int MeritCopiesForCurrentSpeed(string mode)
        {
            if (mode != AutoIncenseMode) return 1;      // 手动上贡保持原样
            if (_autoIncenseSpeed <= 5) return 1;

            double t = (_autoIncenseSpeed - 5) / (double)(SPEED_MAX - 5);   // 5→0, 50→1
            double chance = 0.25 + 0.6 * t;
            if (_rnd.NextDouble() >= chance) return 1;

            int maxCopies = 2 + (int)Math.Round(6 * t);                      // 最多 8 段
            return 1 + _rnd.Next(1, maxCopies);
        }

        private void ShowMeritAnimation(string text, int copies)
        {
            // 该受供者的文字效果为"无"：不生成飘字层，弹都不弹
            if (string.IsNullOrEmpty(text)) return;

            if (_activeOverlays == null)
                _activeOverlays = new List<MeritOverlayForm>();

            MeritOverlayForm overlay;
            if (_activeOverlays.Count == 0 || _activeOverlays[0] == null || _activeOverlays[0].IsDisposed)
            {
                _activeOverlays.Clear();
                overlay = new MeritOverlayForm(this);
                _activeOverlays.Add(overlay);
                overlay.ShowLayer();
            }
            else
            {
                overlay = _activeOverlays[0];
            }

            int w = ClientSize.Width;
            int h = ClientSize.Height;
            for (int i = 0; i < copies; i++)
            {
                float sx = (float)_rnd.Next(50, Math.Max(51, w - 50));
                float sy = (float)_rnd.Next(30, Math.Max(31, h / 2));
                overlay.Add(text, sx, sy);
            }

            // 同屏最多保留这么多条：再多也只是互相盖住，纯属拖慢合成
            overlay.TrimTo(MAX_MERIT_ITEMS);

            // 动画定时器未运行时立刻推一帧，其余交给 30ms Tick 统一渲染
            bool needImmediateFrame = _animTimer == null;

            if (needImmediateFrame)
            {
                overlay.Render();
                _animTimer = new Timer();
                _animTimer.Interval = 30;
                _animTimer.Tick += AnimationTick;
                _animTimer.Start();
            }
        }

        private void AnimationTick(object sender, EventArgs e)
        {
            if (_activeOverlays == null || _activeOverlays.Count == 0)
            {
                if (_animTimer != null) { _animTimer.Stop(); _animTimer.Dispose(); _animTimer = null; }
                return;
            }

            var ov = _activeOverlays[0];
            if (ov == null || ov.IsDisposed)
            {
                _activeOverlays.Clear();
                if (_animTimer != null) { _animTimer.Stop(); _animTimer.Dispose(); _animTimer = null; }
                return;
            }

            if (ov.Tick())
            {
                ov.Render();
            }
            else
            {
                ov.HideLayer();
                ov.Clear();
                _activeOverlays.Clear();
                if (_animTimer != null) { _animTimer.Stop(); _animTimer.Dispose(); _animTimer = null; }
            }
        }

        // ================================================================
        //  上贡倍速 / 快捷键设置
        // ================================================================

        // 档位与倍速一一对应：滑块 1 格就是 1 倍速
        private void SpeedChanged()
        {
            if (_tbSpeed == null) return;
            _autoIncenseSpeed = Math.Max(SPEED_MIN, Math.Min(SPEED_MAX, _tbSpeed.Value));

            if (_lblSpeed != null)
            {
                _lblSpeed.Text = SpeedLabelText();
            }

            SaveSettings();
        }

        private string SettingsFile
        {
            get { return Path.Combine(_resourcesFolder, "ui.ini"); }
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsFile)) return;
                foreach (string raw in File.ReadAllLines(SettingsFile, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();

                    if (string.Equals(key, "speed", StringComparison.OrdinalIgnoreCase))
                    {
                        int v;
                        if (int.TryParse(val, out v))
                            _autoIncenseSpeed = Math.Max(SPEED_MIN, Math.Min(SPEED_MAX, v));
                    }
                    else if (string.Equals(key, "hotkey", StringComparison.OrdinalIgnoreCase))
                    {
                        int v;
                        if (int.TryParse(val, out v) && v != 0)
                            _autoIncenseHotkey = (Keys)v;
                    }
                }
            }
            catch
            {
                // 设置文件坏了就用默认值，不影响启动
            }
        }

        private void SaveSettings()
        {
            if (_loadingSettings) return;
            try
            {
                Directory.CreateDirectory(_resourcesFolder);
                var sb = new StringBuilder();
                sb.AppendLine(T("# 上贡许愿机界面设置，删除此文件即恢复默认",
                    "# Offering Wish Machine UI settings. Delete this file to restore defaults."));
                sb.AppendLine("speed=" + _autoIncenseSpeed);
                sb.AppendLine("hotkey=" + (int)_autoIncenseHotkey);
                sb.AppendLine("language=" + _languageMode);
                File.WriteAllText(SettingsFile, sb.ToString(), new UTF8Encoding(false));
            }
            catch
            {
                // 写不了就算了，不能因为保存设置影响上贡
            }
        }

        // 让快捷键不受「当前焦点在哪个控件」与 WinForms 的 F10 菜单激活逻辑影响
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData != Keys.None && keyData == _autoIncenseHotkey)
            {
                ToggleAutoIncense();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // 捕获「按一个键」：用于自定义快捷键
        private Keys PromptHotkey(Keys current)
        {
            using (var dlg = new Form())
            {
                dlg.Text = T("设置快捷键", "Set hotkey");
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.ClientSize = new Size(320, 108);
                dlg.MinimizeBox = false;
                dlg.MaximizeBox = false;
                dlg.KeyPreview = true;
                dlg.Font = new Font("Microsoft YaHei UI", 9F);

                var lbl = new Label
                {
                    Text = T("请按下一个按键作为「自动上贡」开关\r\n当前：",
                        "Press a key to toggle auto offering\r\nCurrent: ") + DescribeKey(current),
                    Location = new Point(12, 12),
                    Size = new Size(296, 44),
                    TextAlign = ContentAlignment.MiddleCenter,
                };

                var btnOk = new Button { Text = T("确定", "OK"), Location = new Point(56, 70), Size = new Size(90, 28), DialogResult = DialogResult.OK };
                var btnCancel = new Button { Text = T("取消", "Cancel"), Location = new Point(174, 70), Size = new Size(90, 28), DialogResult = DialogResult.Cancel };

                Keys captured = current;
                bool hasCaptured = false;

                dlg.KeyDown += (s, e) =>
                {
                    KeyEventArgs ke = e;
                    // 单独的修饰键不作为快捷键
                    if (ke.KeyCode == Keys.ControlKey || ke.KeyCode == Keys.ShiftKey ||
                        ke.KeyCode == Keys.Menu || ke.KeyCode == Keys.None)
                        return;

                    captured = ke.KeyCode;
                    hasCaptured = true;
                    lbl.Text = T("请按下一个按键作为「自动上贡」开关\r\n当前：",
                        "Press a key to toggle auto offering\r\nCurrent: ") + DescribeKey(captured);
                    ke.Handled = true;
                    ke.SuppressKeyPress = true;
                };

                dlg.Controls.AddRange(new Control[] { lbl, btnOk, btnCancel });
                dlg.AcceptButton = btnOk;
                dlg.CancelButton = btnCancel;

                if (dlg.ShowDialog(this) != DialogResult.OK || !hasCaptured)
                    return current;
                return captured;
            }
        }

        private static string DescribeKey(Keys key)
        {
            return key == Keys.None ? T("（未设置）", "(not set)") : key.ToString();
        }

        private void PromptAndSetHotkey()
        {
            Keys k = PromptHotkey(_autoIncenseHotkey);
            if (k == Keys.None || k == _autoIncenseHotkey) return;
            _autoIncenseHotkey = k;
            SaveSettings();
        }

        // ================================================================

        private void Form1_Resize(object sender, EventArgs e)
        {
            if (_pbDeity == null || _sep == null) return;

            int w = ClientSize.Width;
            int h = ClientSize.Height;

            float scale = Math.Min(w / 700f, h / 480f);
            if (scale > 1.0f) scale = 1.0f;
            if (scale < 0.3f) scale = 0.3f;

            // 窗口过窄时两侧自动隐藏；齿轮菜单可再分别收起左右侧栏
            bool roomForSides = w >= 560;
            bool showLeft = roomForSides && !_leftCollapsed;
            bool showRight = roomForSides && !_rightCollapsed;

            if (_lstDeities == null || _lstOfferings == null) return;
            SetSideVisible(true, showLeft);
            SetSideVisible(false, showRight);

            int tw = CENTER_W;
            int cx = CENTER_X;
            if (showLeft || showRight)
            {
                // 在可见的侧栏之间居中；已收起的侧栏不再占位
                int midLeft = showLeft ? SideLeft() + SIDE_W + 8 : SIDE_MARGIN;
                int midRight = showRight ? SideRight(w) - 8 : w - SIDE_MARGIN;
                int midW = Math.Max(120, midRight - midLeft);
                // 窗口不够宽时收窄中间内容，避免被侧栏盖住
                if (midW < tw) tw = midW;
                cx = midLeft + (midW - tw) / 2;
            }
            else
            {
                // 无侧栏时整窗居中
                tw = Math.Max(120, Math.Min(CENTER_W, w - 16));
                cx = (w - tw) / 2;
            }

            if (showLeft || showRight)
            {
                int rightX = SideRight(w);
                int listH = Math.Min(250, Math.Max(80, h - 28 - 34 - 6));
                _lstDeities.Height = listH;
                _lstOfferings.Height = listH;
                _lblOfferings.Location = new Point(rightX, 8);
                _lstOfferings.Location = new Point(rightX, 28);
                int sideBtnY = _lstDeities.Bottom + 4;
                _btnDeityNew.Location = new Point(SideLeft(), sideBtnY);
                _btnDeityDelete.Location = new Point(SideLeft() + 78, sideBtnY);
                _btnOfferingNew.Location = new Point(rightX, sideBtnY);
                _btnOfferingDelete.Location = new Point(rightX + 78, sideBtnY);
                // 速度条跟在右栏按钮下方；窗口太矮时隐藏，避免压到上贡按钮上
                if (_lblSpeed != null && _tbSpeed != null)
                {
                    int speedTop = sideBtnY + 28 + 10;
                    bool roomForSpeed = showRight && speedTop + 18 + 24 <= h - 8;
                    _lblSpeed.Visible = roomForSpeed;
                    _tbSpeed.Visible = roomForSpeed;
                    if (roomForSpeed)
                    {
                        // 窗口矮时缩短轨道，给齿轮让位
                        int speedW = SIDE_W;
                        if (_btnSettings != null)
                        {
                            int gearTop = Math.Max(0, h - _btnSettings.Height - 6);
                            int gearLeft = Math.Max(0, w - _btnSettings.Width - 6);
                            if (speedTop + 16 + 24 > gearTop)
                            {
                                int fit = gearLeft - 4 - rightX;
                                if (fit > 60 && fit < speedW) speedW = fit;
                            }
                        }
                        _lblSpeed.Width = speedW;
                        _tbSpeed.Width = speedW;
                        _lblSpeed.Location = new Point(rightX, speedTop);
                        _tbSpeed.Location = new Point(rightX, speedTop + 16);
                        // 采样标签那一条的亮度
                        Color speedFg = SettingsForeColorAt(rightX, speedTop, SIDE_W, 18);
                        _lblSpeed.ForeColor = speedFg;
                        // 文字是浅色 → 说明背景偏深，轨道也用浅色；反之用深色
                        _tbSpeed.TrackColor = speedFg.R > 130
                            ? Color.FromArgb(150, 255, 255, 255)
                            : Color.FromArgb(150, 40, 40, 40);
                        _lblSpeed.Refresh();
                        _tbSpeed.Invalidate();
                    }
                }
            }
            const int btnH = 28;          // 上贡/自动上贡按钮高度
            const int bottomMargin = 4;   // 按钮距窗口底部留白
            const int comboH = 21;        // 槽位下拉框高度
            const int slotsGap = 3;       // 槽位图片与下拉框之间
            const int slotsBtnGap = 8;    // 下拉框/图片与按钮行之间至少留出的间距

            int nameY = 6;
            int wishY = nameY + 24 + 4;
            int deity = (int)Math.Round(200 * scale);
            if (deity < 50) deity = 50;
            // 受供者图不能比自己所在的中间区还宽，否则会顶到侧栏上
            if (deity > tw) deity = tw;
            int deityY = wishY + 24 + 8;
            int titleY = deityY + deity + 5;
            int sepY = titleY + 24 + 6;
            int afterSepY = sepY + 14 + 4;

            int offW = (int)Math.Round(104 * scale);
            if (offW < 26) offW = 26;
            int offH = (int)Math.Round(100 * scale);
            if (offH < 25) offH = 25;
            // 三列贡品必须整排塞进中间区，否则第三列会越过右栏边界
            int maxOffW = (tw - 8) / 3;
            if (offW > maxOffW) offW = Math.Max(26, maxOffW);
            int gap = (tw - 3 * offW) / 4;
            if (gap < 2) gap = 2;

            // 按钮锚定底部，构成不可侵占的按钮行
            int btnY = Math.Max(0, h - bottomMargin - btnH);

            // 纵向空间不足时：先压扁贡品图，再隐藏图片，最后隐藏下拉框
            int slotsBottomLimit = btnY - slotsBtnGap;
            while (offH > 40 && afterSepY + offH + slotsGap + comboH > slotsBottomLimit)
                offH -= 2;

            // 图片只要放得下就显示（不要求连下拉框一起放下，否则会与按钮重叠）
            bool showOfferImgs = afterSepY + offH <= slotsBottomLimit;
            int afterSlotsY = showOfferImgs ? afterSepY + offH + slotsGap : afterSepY;

            bool showCombos = showOfferImgs
                && scale >= 0.55
                && afterSlotsY + comboH <= slotsBottomLimit;

            int slotsY = afterSepY;
            int combosY = afterSlotsY;

            // ---- 按上述坐标摆放控件 ----
            _txtDeityName.Width = tw;
            _txtDeityName.Location = new Point(cx, nameY);

            _txtWish.Width = tw;
            _txtWish.Location = new Point(cx, wishY);

            _pbDeity.Size = new Size(deity, deity);
            _pbDeity.Location = new Point(cx + (tw - deity) / 2, deityY);

            _txtDeityTitle.Width = tw;
            _txtDeityTitle.Location = new Point(cx, titleY);

            _sep.Width = tw;
            _sep.Location = new Point(cx, sepY);
            _sep.Visible = scale >= 0.5;

            for (int i = 0; i < 3; i++)
            {
                int sx = cx + gap + i * (offW + gap);
                _pbOfferingSlots[i].Size = new Size(offW, offH);
                _pbOfferingSlots[i].Location = new Point(sx, slotsY);
                _pbOfferingSlots[i].Visible = showOfferImgs;
            }

            for (int i = 0; i < 3; i++)
            {
                int sx = cx + gap + i * (offW + gap);
                _cmbOfferingSlots[i].Width = offW;
                _cmbOfferingSlots[i].Location = new Point(sx, combosY);
                _cmbOfferingSlots[i].Visible = showCombos;
            }

            // 窄窗口下自适应按钮宽度，保证两个按钮并排且始终完整在窗口内
            int btnW = Math.Min(90, (tw - 4) / 2);
            int btnGap = Math.Max(2, (tw - 2 * btnW) / 3);
            int autoX = cx + btnGap;
            int incenseX = cx + btnGap * 2 + btnW;

            // 极窄高度下按钮仍不可压到槽位图片上，必要时整体下移
            int buttonsY = btnY;
            if (showOfferImgs)
            {
                int slotsBottom = afterSepY + offH;
                if (buttonsY < slotsBottom) buttonsY = Math.Min(slotsBottom, Math.Max(0, h - btnH));
            }

            _btnAutoIncense.Location = new Point(autoX, buttonsY);
            _btnIncense.Location = new Point(incenseX, buttonsY);
            _btnAutoIncense.BringToFront();
            _btnIncense.BringToFront();

            // 齿轮固定在右下角，始终在最上层；窗口过窄时隐藏
            if (_btnSettings != null)
            {
                _btnSettingsShouldShow = w >= 360;
                _btnSettings.Visible = _btnSettingsShouldShow;
                if (_btnSettingsShouldShow)
                {
                    _btnSettings.Location = new Point(
                        Math.Max(0, w - _btnSettings.Width - 6),
                        Math.Max(0, h - _btnSettings.Height - 6));
                    _btnSettings.ForeColor = SettingsForeColorAt(
                        _btnSettings.Left, _btnSettings.Top, _btnSettings.Width);
                    _btnSettings.Refresh();
                }
            }

            // 放在最后：确保齿轮压在所有控件之上，不会被侧栏列表遮挡
            BringSettingsToFront();
        }

        private void BringSettingsToFront()
        {
            // 不能用 Control.Visible 判断（窗口未显示时恒为 false）
            if (_btnSettings != null && _btnSettingsShouldShow)
                _btnSettings.BringToFront();
        }

        // 左右侧栏的横坐标：侧栏宽 150，距边 10
        private const int SIDE_W = 150;
        private const int SIDE_MARGIN = 10;

        private static int SideLeft() { return SIDE_MARGIN; }

        private static int SideRight(int clientWidth) { return clientWidth - SIDE_MARGIN - SIDE_W; }

        // 齿轮前景色跟随其所在位置的背景明暗，避免在深色壁纸上“看不见”
        private Color SettingsForeColorAt(int x, int y, int size)
        {
            return SettingsForeColorAt(x, y, size, size);
        }

        // 采样一块矩形区域的平均亮度：宽高分开传，避免顺带采到下方的按钮
        private Color SettingsForeColorAt(int x, int y, int width, int height)
        {
            Image img = BackgroundImage;
            if (img == null || ClientSize.Width <= 0 || ClientSize.Height <= 0)
                return Color.FromArgb(90, 90, 90);

            var r = new Rectangle(x, y, Math.Max(1, width), Math.Max(1, height));
            r.Intersect(new Rectangle(0, 0, ClientSize.Width, ClientSize.Height));
            if (r.Width <= 0 || r.Height <= 0) return Color.FromArgb(90, 90, 90);

            double sum = 0;
            int n = 0;
            // 背景为 Stretch：按比例换算到源图坐标后再采样
            float fx = (float)img.Width / ClientSize.Width;
            float fy = (float)img.Height / ClientSize.Height;
            var src = new Rectangle(
                (int)(r.Left * fx), (int)(r.Top * fy),
                Math.Max(1, (int)(r.Width * fx)), Math.Max(1, (int)(r.Height * fy)));
            src.Intersect(new Rectangle(0, 0, img.Width, img.Height));
            if (src.Width <= 0 || src.Height <= 0) return Color.FromArgb(90, 90, 90);

            using (var small = new Bitmap(4, 4, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var gg = Graphics.FromImage(small))
                    gg.DrawImage(img, new Rectangle(0, 0, 4, 4), src, GraphicsUnit.Pixel);

                // 一次性取出 16 个像素：比 16 次 GetPixel（每次都要跨 GDI+ 调用）快得多
                System.Drawing.Imaging.BitmapData data = small.LockBits(
                    new Rectangle(0, 0, 4, 4),
                    System.Drawing.Imaging.ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    for (int sy = 0; sy < 4; sy++)
                    {
                        IntPtr row = new IntPtr(data.Scan0.ToInt64() + (long)sy * data.Stride);
                        for (int sx = 0; sx < 4; sx++)
                        {
                            int argb = System.Runtime.InteropServices.Marshal.ReadInt32(row, sx * 4);
                            int b = argb & 0xFF;
                            int gg2 = (argb >> 8) & 0xFF;
                            int rr = (argb >> 16) & 0xFF;
                            sum += 0.299 * rr + 0.587 * gg2 + 0.114 * b;   // 感知亮度
                            n++;
                        }
                    }
                }
                finally
                {
                    small.UnlockBits(data);
                }
            }
            double luma = n > 0 ? sum / n : 255;
            return luma > 140 ? Color.FromArgb(60, 60, 60) : Color.FromArgb(235, 235, 235);
        }

        // ================================================================
        //  飘字渲染：整个主窗体只有一个分层窗口，逐像素 alpha
        // ================================================================

        // 一条飘字：文字、横向中心、起点与当前纵坐标、当前不透明度
        internal sealed class MeritText
        {
            public string Text;
            public float CenterX;
            public float StartY;
            public float Y;
            public float Opacity;
            // 文字宽度缓存：同一段文字只需测量一次，之后各帧复用
            public float Width;
            public bool WidthMeasured;
        }

        private class MeritOverlayForm : Form
        {
            [StructLayout(LayoutKind.Sequential)]
            private struct POINT { public int X; public int Y; }

            [StructLayout(LayoutKind.Sequential)]
            private struct SIZE { public int cx; public int cy; }

            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            private struct BLENDFUNCTION
            {
                public byte BlendOp;
                public byte BlendFlags;
                public byte SourceConstantAlpha;
                public byte AlphaFormat;
            }

            [DllImport("user32.dll", SetLastError = true)]
            private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst,
                ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc,
                int crKey, ref BLENDFUNCTION pblend, int dwFlags);

            [DllImport("user32.dll")]
            private static extern IntPtr GetDC(IntPtr hWnd);

            [DllImport("user32.dll")]
            private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

            [DllImport("gdi32.dll")]
            private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

            [DllImport("gdi32.dll")]
            private static extern bool DeleteDC(IntPtr hdc);

            [DllImport("gdi32.dll")]
            private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

            [DllImport("gdi32.dll")]
            private static extern bool DeleteObject(IntPtr hObject);

            private const int WS_EX_LAYERED = 0x00080000;
            private const int WS_EX_TRANSPARENT = 0x00000020;
            private const int WS_EX_NOACTIVATE = 0x08000000;
            private const int WS_EX_TOOLWINDOW = 0x00000080;
            private const int ULW_ALPHA = 0x00000002;
            private const byte AC_SRC_OVER = 0x00;
            private const byte AC_SRC_ALPHA = 0x01;

            // 每帧位移与淡出步长：与旧实现完全一致（30ms 一帧，约 1.65 秒走完）
            private const float STEP_Y = 2f;
            private const float STEP_OPACITY = 0.018f;
            private const float TRAVEL_LIMIT = 120f;

            public readonly List<MeritText> Items = new List<MeritText>();

            private readonly Form _owner;
            private Bitmap _surface;
            private Graphics _surfaceGraphics;
            private bool _visiblePushed;

            public MeritOverlayForm(Form owner)
            {
                _owner = owner;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                ShowIcon = false;
                StartPosition = FormStartPosition.Manual;
                Owner = owner;
                Location = owner.PointToScreen(Point.Empty);
                Size = owner.ClientSize;
                // 分层窗口的内容完全由 UpdateLayeredWindow 提供，自身的绘制不参与显示
                SetStyle(ControlStyles.Opaque, true);
            }

            // 不抢焦点：点击穿透且永不激活，避免挂机时把主窗体挤到后台
            protected override bool ShowWithoutActivation { get { return true; } }

            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                    return cp;
                }
            }

            private static readonly Font MeritFont = new Font("Microsoft YaHei UI", 24F, FontStyle.Bold);
            // 每帧只改颜色（alpha 逐条不同），复用同一个画笔
            private static readonly SolidBrush MeritBrush = new SolidBrush(Color.Gold);

            public void Clear()
            {
                Items.Clear();
            }

            public void Add(string text, float centerX, float startY)
            {
                Items.Add(new MeritText
                {
                    Text = text,
                    CenterX = centerX,
                    StartY = startY,
                    Y = startY,
                    Opacity = 1f,
                });
            }

            // 推进一帧；返回 false 表示已全部结束，可以停止定时器
            public bool Tick()
            {
                for (int i = Items.Count - 1; i >= 0; i--)
                {
                    var it = Items[i];
                    it.Y -= STEP_Y;
                    it.Opacity -= STEP_OPACITY;
                    if (it.Y < it.StartY - TRAVEL_LIMIT || it.Opacity <= 0f)
                        Items.RemoveAt(i);
                }
                return Items.Count > 0;
            }

            // 超过上限时丢掉最老的一条
            public void TrimTo(int maxCount)
            {
                int extra = Items.Count - maxCount;
                if (extra > 0) Items.RemoveRange(0, extra);
            }

            // 把当前所有飘字合成到分层窗口
            public void Render()
            {
                if (_owner == null || _owner.IsDisposed || IsDisposed) return;

                Size want = _owner.ClientSize;
                if (want.Width <= 0 || want.Height <= 0) return;
                EnsureSurface(want);

                _surfaceGraphics.Clear(Color.Transparent);
                _surfaceGraphics.TextRenderingHint = TextRenderingHint.AntiAlias;

                for (int i = 0; i < Items.Count; i++)
                {
                    var it = Items[i];
                    int alpha = (int)(255f * it.Opacity);
                    if (alpha <= 0) continue;
                    if (alpha > 255) alpha = 255;

                    // 宽度只在第一帧量一次；之后每帧都 MeasureString 会在高倍速下白烧 CPU
                    if (!it.WidthMeasured)
                    {
                        it.Width = _surfaceGraphics.MeasureString(it.Text, MeritFont).Width;
                        it.WidthMeasured = true;
                    }

                    MeritBrush.Color = Color.FromArgb(alpha, 255, 215, 0);   // Gold
                    _surfaceGraphics.DrawString(it.Text, MeritFont, MeritBrush,
                        it.CenterX - it.Width / 2f, it.Y);
                }

                Push(want);
            }

            private void EnsureSurface(Size want)
            {
                if (_surface != null && _surface.Width == want.Width && _surface.Height == want.Height)
                    return;

                if (_surfaceGraphics != null) { _surfaceGraphics.Dispose(); _surfaceGraphics = null; }
                if (_surface != null) { _surface.Dispose(); _surface = null; }

                // 每次都新建到完全相同尺寸，避免残留上一帧的像素
                _surface = new Bitmap(want.Width, want.Height, PixelFormat.Format32bppArgb);
                _surfaceGraphics = Graphics.FromImage(_surface);
                Size = want;
            }

            // 逐像素 alpha 推送，不会有粉边
            private void Push(Size want)
            {
                Point dst = _owner.PointToScreen(Point.Empty);
                IntPtr hBitmap = IntPtr.Zero;
                IntPtr screenDc = IntPtr.Zero;
                IntPtr memDc = IntPtr.Zero;
                IntPtr oldObj = IntPtr.Zero;
                try
                {
                    hBitmap = _surface.GetHbitmap(Color.FromArgb(0));
                    screenDc = GetDC(IntPtr.Zero);
                    memDc = CreateCompatibleDC(screenDc);
                    oldObj = SelectObject(memDc, hBitmap);

                    var pptDst = new POINT { X = dst.X, Y = dst.Y };
                    var psize = new SIZE { cx = want.Width, cy = want.Height };
                    var pptSrc = new POINT { X = 0, Y = 0 };
                    var blend = new BLENDFUNCTION
                    {
                        BlendOp = AC_SRC_OVER,
                        BlendFlags = 0,
                        SourceConstantAlpha = 255,
                        AlphaFormat = AC_SRC_ALPHA,
                    };

                    UpdateLayeredWindow(Handle, screenDc, ref pptDst, ref psize,
                        memDc, ref pptSrc, 0, ref blend, ULW_ALPHA);
                    Location = dst;   // 与系统记录的位置保持一致，便于调试与验证
                }
                catch
                {
                    // 任何一步失败都不应影响主界面
                }
                finally
                {
                    if (oldObj != IntPtr.Zero) SelectObject(memDc, oldObj);
                    if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                    if (memDc != IntPtr.Zero) DeleteDC(memDc);
                    if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
                }
            }

            // 显示/隐藏由主窗体控制：没有飘字时隐藏，避免挂机时白占一个合成图层
            public void ShowLayer()
            {
                if (!_visiblePushed)
                {
                    _visiblePushed = true;
                    Show();
                }
            }

            public void HideLayer()
            {
                if (_visiblePushed)
                {
                    _visiblePushed = false;
                    Hide();
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    if (_surfaceGraphics != null) { _surfaceGraphics.Dispose(); _surfaceGraphics = null; }
                    if (_surface != null) { _surface.Dispose(); _surface = null; }
                }
                base.Dispose(disposing);
            }
        }
    // 真透明的列表控件：从窗体背景图上裁剪对应区域自绘
    private class FlatListBox : Control
    {
        public const int ItemHeight = 20;

        private const int statusH = 15;   // 底部预留状态条区域

        private readonly List<string> _items = new List<string>();
        private int _selectedIndex = -1;
        private bool _opaqueMode;
        private int _scroll;
        private bool _dragging;
        private int _dragStartY;
        private int _dragStartScroll;

        // 复用画笔：选中/未选中只有两种文字色，无需每帧每项 new
        private static readonly SolidBrush SelectedTextBrush = new SolidBrush(Color.White);
        private static readonly SolidBrush NormalTextBrush = new SolidBrush(Color.Black);
        private static readonly SolidBrush SelectedBackBrush = new SolidBrush(Color.FromArgb(80, 0, 100, 200));
        private static readonly SolidBrush OpaqueBackBrush = new SolidBrush(Color.White);
        private static readonly SolidBrush ScrollBarBrush = new SolidBrush(Color.FromArgb(140, 0, 0, 0));
        private static readonly SolidBrush ScrollTextBrush = new SolidBrush(Color.FromArgb(190, 0, 0, 0));
        // 焦点下划线用的是「选中文字色 + 120 alpha」，选中文字恒为白色，故可直接固化
        private static readonly Pen SelectedFocusPen = new Pen(Color.FromArgb(120, Color.White));

        // 背景裁剪缓存：尺寸与源区域不变时复用上一次的位图，避免每次重绘都分配 Width*Height
        private Bitmap _bgCache;
        private Image _bgCacheSrc;
        private Rectangle _bgCacheRect;
        private Size _bgCacheSize;

        public event EventHandler SelectedIndexChanged;

        public FlatListBox()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            TabStop = true;
        }

        public IList<string> Items { get { return _items; } }

        public bool OpaqueMode
        {
            get { return _opaqueMode; }
            set { _opaqueMode = value; Invalidate(); }
        }

        public int SelectedIndex
        {
            get { return _selectedIndex; }
            set
            {
                int v = (value < -1 || value >= _items.Count) ? -1 : value;
                if (v == _selectedIndex) return;
                _selectedIndex = v;
                EnsureVisible();
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        public object SelectedItem
        {
            get { return _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null; }
        }

        public int Count { get { return _items.Count; } }

        public int IndexOf(string item) { return _items.IndexOf(item); }

        public void Add(string item)
        {
            int index = _items.Count;
            _items.Add(item ?? "");
            if (_selectedIndex < 0) SelectedIndex = index;   // 首项自动选中，保持与原 ListBox 行为一致
            Invalidate();
        }

        public void Clear()
        {
            _items.Clear();
            _selectedIndex = -1;
            _scroll = 0;
            Invalidate();
        }

        private int VisibleCount { get { return Math.Max(1, (Height - statusH) / ItemHeight); } }

        private void EnsureVisible()
        {
            if (_selectedIndex < 0)
            {
                _scroll = 0;
                return;
            }
            int visible = VisibleCount;
            if (_selectedIndex < _scroll) _scroll = _selectedIndex;
            else if (_selectedIndex >= _scroll + visible) _scroll = _selectedIndex - visible + 1;
            _scroll = Math.Max(0, Math.Min(_scroll, Math.Max(0, _items.Count - visible)));
        }

        private int IndexAt(int y)
        {
            if (y < 0) return -1;
            int i = _scroll + y / ItemHeight;
            return (i >= 0 && i < _items.Count) ? i : -1;
        }

        // 供右键菜单使用：把指定 y 坐标处的条目选中
        internal void SelectItemAt(int y)
        {
            int i = IndexAt(y);
            if (i >= 0) SelectedIndex = i;
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left) return;
            int i = IndexAt(e.Y);
            if (i >= 0) { SelectedIndex = i; _dragging = false; }
            else { _dragging = true; _dragStartY = e.Y; _dragStartScroll = _scroll; }   // 空白处按住可拖动
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging || (e.Button & MouseButtons.Left) == 0) return;
            _scroll = Math.Max(0, Math.Min(_dragStartScroll + (_dragStartY - e.Y) / ItemHeight,
                                          Math.Max(0, _items.Count - VisibleCount)));
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            _scroll = Math.Max(0, Math.Min(_scroll - Math.Sign(e.Delta),
                                          Math.Max(0, _items.Count - VisibleCount)));
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up:
                case Keys.Down:
                case Keys.Home:
                case Keys.End:
                case Keys.PageUp:
                case Keys.PageDown:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_items.Count == 0) return;

            int index = _selectedIndex;
            switch (e.KeyCode)
            {
                case Keys.Up: index--; break;
                case Keys.Down: index++; break;
                case Keys.Home: index = 0; break;
                case Keys.End: index = _items.Count - 1; break;
                case Keys.PageUp: index -= VisibleCount; break;
                case Keys.PageDown: index += VisibleCount; break;
                default: return;
            }

            e.Handled = true;
            if (index < 0) index = 0;
            if (index > _items.Count - 1) index = _items.Count - 1;
            SelectedIndex = index;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // 关闭透明时用纯色底，保证文字在花哨背景图上依然可读
            if (_opaqueMode)
            {
                e.Graphics.FillRectangle(OpaqueBackBrush, ClientRectangle);
                return;
            }

            Image img = null;
            Size hostSize = Size.Empty;
            var form = FindForm();
            if (form != null)
            {
                img = form.BackgroundImage;
                hostSize = form.ClientSize;
            }

            if (img != null && hostSize.Width > 0 && hostSize.Height > 0)
            {
                // 窗体背景为 Stretch：按窗体客户区尺寸等比，取本控件所占的那一块
                float sx = (float)img.Width / hostSize.Width;
                float sy = (float)img.Height / hostSize.Height;
                var topLeft = form.PointToClient(PointToScreen(Point.Empty));
                var src = new Rectangle(
                    (int)Math.Round(topLeft.X * sx), (int)Math.Round(topLeft.Y * sy),
                    (int)Math.Round(Width * sx), (int)Math.Round(Height * sy));

                // 只在源图、源区域或控件尺寸变化时重建位图；重绘本身不再分配内存
                if (_bgCache == null || !ReferenceEquals(_bgCacheSrc, img)
                    || _bgCacheRect != src || _bgCacheSize != Size)
                {
                    if (_bgCache != null) { _bgCache.Dispose(); _bgCache = null; }
                    if (Width > 0 && Height > 0 && src.Width > 0 && src.Height > 0)
                    {
                        _bgCache = new Bitmap(Width, Height);
                        using (var g = Graphics.FromImage(_bgCache))
                            g.DrawImage(img, new Rectangle(0, 0, Width, Height), src, GraphicsUnit.Pixel);
                        _bgCacheSrc = img;
                        _bgCacheRect = src;
                        _bgCacheSize = Size;
                    }
                }

                if (_bgCache != null)
                {
                    e.Graphics.DrawImageUnscaled(_bgCache, 0, 0);
                    return;
                }
            }

            base.OnPaintBackground(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            int visible = VisibleCount;
            int last = Math.Min(_items.Count, _scroll + visible);

            for (int i = _scroll; i < last; i++)
            {
                int y = (i - _scroll) * ItemHeight;
                bool selected = (i == _selectedIndex);

                if (selected)
                    g.FillRectangle(SelectedBackBrush, 0, y, Width, ItemHeight);

                SolidBrush brush = selected ? SelectedTextBrush : NormalTextBrush;
                g.DrawString(_items[i], Font, brush, 3, y + 2);
                // 焦点提示：仅画一条下划线，避免在透明背景上出现不透明焦点框
                if (selected && Focused)
                {
                    g.DrawLine(SelectedFocusPen, 2, y + ItemHeight - 2, Width - 3, y + ItemHeight - 2);
                }
            }

            // 内容超出时的滚动条与计数
            if (_items.Count > visible)
            {
                int trackH = Math.Max(1, visible * ItemHeight);
                g.FillRectangle(Brushes.LightGray, Width - 3, 0, 3, trackH);
                int barH = Math.Max(12, trackH * visible / _items.Count);
                int barY = _items.Count > visible
                    ? (_scroll * (trackH - barH) / (_items.Count - visible))
                    : 0;
                g.FillRectangle(ScrollBarBrush, Width - 3, barY, 3, barH);
                g.DrawString((_scroll + 1) + "-" + last + "/" + _items.Count, Font, ScrollTextBrush, 3, Height - statusH);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _bgCache != null) { _bgCache.Dispose(); _bgCache = null; }
            base.Dispose(disposing);
        }
    }
    // 真透明的水平滑块：自绘轨道 + 圆形滑块
    private class FlatSlider : Control
    {
        private int _minimum;
        private int _maximum = 100;
        private int _value;
        private bool _dragging;

        private static readonly Pen FillPen = new Pen(Color.FromArgb(255, 255, 214, 92), 3f);
        private static readonly SolidBrush KnobBrush = new SolidBrush(Color.FromArgb(255, 255, 214, 92));

        // 轨道颜色可随背景明暗调整；只在实际变化时重建画笔，避免每个控件常驻一支 Pen
        private Color _trackColor = Color.FromArgb(120, 255, 255, 255);
        private Pen _trackPen;

        public event EventHandler ValueChanged;

        // 轨道颜色跟着背景切换：浅色壁纸上用白色轨道会几乎看不见
        public Color TrackColor
        {
            get { return _trackColor; }
            set
            {
                if (_trackColor == value) return;
                _trackColor = value;
                if (_trackPen != null) { _trackPen.Dispose(); _trackPen = null; }
                Invalidate();
            }
        }

        private Pen TrackPen
        {
            get
            {
                if (_trackPen == null) _trackPen = new Pen(_trackColor, 3f);
                return _trackPen;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _trackPen != null) { _trackPen.Dispose(); _trackPen = null; }
            base.Dispose(disposing);
        }

        public FlatSlider()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            Size = new Size(150, 24);
            TabStop = false;
        }

        public int Minimum
        {
            get { return _minimum; }
            set { _minimum = value; if (_value < _minimum) Value = _minimum; Invalidate(); }
        }

        public int Maximum
        {
            get { return _maximum; }
            set { _maximum = value; if (_value > _maximum) Value = _maximum; Invalidate(); }
        }

        public int Value
        {
            get { return _value; }
            set
            {
                int v = Math.Max(_minimum, Math.Min(_maximum, value));
                if (v == _value) return;
                _value = v;
                Invalidate();
                var h = ValueChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        // 轨道两端留出的半径，保证滑块在任何取值下都完整可见
        private int KnobRadius { get { return 7; } }

        private int TrackLeft { get { return KnobRadius + 1; } }

        private int TrackWidth { get { return Math.Max(1, Width - 2 * (KnobRadius + 1)); } }

        private float Ratio
        {
            get
            {
                int span = _maximum - _minimum;
                return span <= 0 ? 0f : (float)(_value - _minimum) / span;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int y = Height / 2;
            int x0 = TrackLeft;
            int x1 = TrackLeft + TrackWidth;
            int knobX = x0 + (int)Math.Round(TrackWidth * Ratio);

            g.DrawLine(TrackPen, x0, y, x1, y);
            if (knobX > x0) g.DrawLine(FillPen, x0, y, knobX, y);

            int r = KnobRadius;
            g.FillEllipse(KnobBrush, knobX - r, y - r, 2 * r, 2 * r);
        }

        private void SetValueFromX(int x)
        {
            int x0 = TrackLeft;
            int w = TrackWidth;
            float ratio = w <= 0 ? 0f : (float)(x - x0) / w;
            if (ratio < 0f) ratio = 0f;
            if (ratio > 1f) ratio = 1f;
            Value = _minimum + (int)Math.Round((_maximum - _minimum) * ratio);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if ((e.Button & MouseButtons.Left) == 0) return;
            _dragging = true;
            Focus();
            SetValueFromX(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging || (e.Button & MouseButtons.Left) == 0) return;
            SetValueFromX(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
        }

        // 滑块自身不可获得焦点外观，但需要接收方向键微调
        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left:
                case Keys.Right:
                case Keys.Home:
                case Keys.End:
                case Keys.PageUp:
                case Keys.PageDown:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Left: Value = _value - 1; break;
                case Keys.Right: Value = _value + 1; break;
                case Keys.Home: Value = _minimum; break;
                case Keys.End: Value = _maximum; break;
                case Keys.PageUp: Value = _value + 5; break;
                case Keys.PageDown: Value = _value - 5; break;
                default: return;
            }
            e.Handled = true;
        }
    }

    // 内联编辑的单行文本框，自行绘制并处理按键
    private class FlatTextBox : Control
    {
        private string _inputText = "";
        private string _placeholder = "";
        private bool _isEditing;
        private int _caret;          // 光标位置（字符索引）
        private int _selStart = -1;  // 全选等选区起点，-1 表示无选区

        // 绘制资源复用：正文只有「占位灰」与「正文黑」两种色，对齐方式也只有三种
        private static readonly SolidBrush PlaceholderBrush = new SolidBrush(Color.FromArgb(160, 160, 160));
        private static readonly SolidBrush InputTextBrush = new SolidBrush(Color.Black);
        private static readonly Pen CaretPen = new Pen(Color.Black);

        private static StringFormat CreateFormat(StringAlignment align)
        {
            return new StringFormat
            {
                Alignment = align,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap,
            };
        }

        private static readonly StringFormat NearFormat = CreateFormat(StringAlignment.Near);
        private static readonly StringFormat CenterFormat = CreateFormat(StringAlignment.Center);
        private static readonly StringFormat FarFormat = CreateFormat(StringAlignment.Far);

        public FlatTextBox()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            TabStop = true;
        }

        // 文本内容变化时通知外部（用户输入、粘贴、退格与程序赋值都会触发），
        // 供 Form1 把「头衔」「心愿」这类内容即时写回磁盘。
        public event EventHandler InputTextChanged;

        private void NotifyInputTextChanged()
        {
            Invalidate();
            EventHandler handler = InputTextChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public string InputText
        {
            get { return _inputText; }
            set
            {
                string next = value ?? "";
                if (string.Equals(_inputText, next, StringComparison.Ordinal)) return;   // 内容没变就不打扰调用方
                _inputText = next;
                if (_caret > _inputText.Length) _caret = _inputText.Length;
                NotifyInputTextChanged();
            }
        }

        public string Placeholder
        {
            get { return _placeholder; }
            set { _placeholder = value ?? ""; Invalidate(); }
        }

        public HorizontalAlignment TextAlign { get; set; }

        private bool HasSelection { get { return _selStart >= 0 && _selStart != _caret; } }

        private void DeleteSelection()
        {
            if (!HasSelection) return;
            int a = Math.Min(_selStart, _caret);
            int b = Math.Max(_selStart, _caret);
            _inputText = _inputText.Remove(a, b - a);
            _caret = a;
            _selStart = -1;
            NotifyInputTextChanged();
        }

        // 开始编辑：光标置于末尾；若原本为空则显示占位文字（此处不再绘制占位）
        private void BeginEditing()
        {
            _isEditing = true;
            _selStart = -1;
            _caret = _inputText.Length;
            Invalidate();
        }

        private void CommitEditing()
        {
            if (!_isEditing) return;
            _isEditing = false;
            _selStart = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled) return;
            Focus();
            if (!_isEditing) BeginEditing();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            if (!_isEditing) BeginEditing();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            CommitEditing();
        }

        private bool IsHandledKey(Keys k)
        {
            switch (k)
            {
                case Keys.Left:
                case Keys.Right:
                case Keys.Home:
                case Keys.End:
                case Keys.Back:
                case Keys.Delete:
                case Keys.Enter:
                    return true;
            }
            return false;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (IsHandledKey(keyData & Keys.KeyCode)) return true;
            if ((keyData & Keys.KeyCode) == Keys.A && (keyData & Keys.Control) == Keys.Control) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (!_isEditing || char.IsControl(e.KeyChar)) return;

            DeleteSelection();
            if (_caret > _inputText.Length) _caret = _inputText.Length;
            _inputText = _inputText.Insert(_caret, e.KeyChar.ToString());
            _caret++;
            e.Handled = true;
            NotifyInputTextChanged();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!_isEditing) { if (IsHandledKey(e.KeyCode)) e.Handled = true; return; }

            // 全选
            if (e.KeyCode == Keys.A && e.Control) { _selStart = 0; _caret = _inputText.Length; e.Handled = true; Invalidate(); return; }

            switch (e.KeyCode)
            {
                case Keys.Left:
                    _selStart = -1;
                    if (_caret > 0) _caret--;
                    e.Handled = true;
                    break;
                case Keys.Right:
                    _selStart = -1;
                    if (_caret < _inputText.Length) _caret++;
                    e.Handled = true;
                    break;
                case Keys.Home:
                    _selStart = -1; _caret = 0; e.Handled = true; break;
                case Keys.End:
                    _selStart = -1; _caret = _inputText.Length; e.Handled = true; break;
                case Keys.Back:
                    if (HasSelection) DeleteSelection();
                    else if (_caret > 0)
                    {
                        _inputText = _inputText.Remove(_caret - 1, 1);
                        _caret--;
                    }
                    e.Handled = true;
                    NotifyInputTextChanged();
                    break;
                case Keys.Delete:
                    if (HasSelection) DeleteSelection();
                    else if (_caret < _inputText.Length) _inputText = _inputText.Remove(_caret, 1);
                    e.Handled = true;
                    NotifyInputTextChanged();
                    break;
                case Keys.Enter:
                    CommitEditing();
                    var f = FindForm();
                    if (f != null) f.Select();
                    e.Handled = true;
                    break;
                case Keys.Escape:
                    CommitEditing();
                    e.Handled = true;
                    break;
                default:
                    // Ctrl+V 粘贴
                    if (e.KeyCode == Keys.V && e.Control)
                    {
                        try
                        {
                            string pasted = Clipboard.GetText();
                            if (!string.IsNullOrEmpty(pasted))
                            {
                                DeleteSelection();
                                _inputText = _inputText.Insert(
                                    Math.Min(_caret, _inputText.Length), pasted.Replace("\r", "").Replace("\n", ""));
                                _caret += pasted.Replace("\r", "").Replace("\n", "").Length;
                                if (_caret > _inputText.Length) _caret = _inputText.Length;
                            }
                        }
                        catch { }
                        e.Handled = true;
                        NotifyInputTextChanged();
                    }
                    break;
            }
            Invalidate();
        }

        // 未编辑时显示（自身文字 or 灰色占位）；编辑时不显示占位，避免与输入内容叠加
        private string DisplayText()
        {
            if (_isEditing) return _inputText;
            return string.IsNullOrEmpty(_inputText) ? _placeholder : _inputText;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            string display = DisplayText();
            if (string.IsNullOrEmpty(display)) return;

            bool placeholder = !_isEditing && string.IsNullOrEmpty(_inputText);
            var g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            StringFormat sf = TextAlign == HorizontalAlignment.Left ? NearFormat
                            : TextAlign == HorizontalAlignment.Right ? FarFormat
                            : CenterFormat;
            g.DrawString(display, Font, placeholder ? PlaceholderBrush : InputTextBrush, ClientRectangle, sf);

            // 编辑中绘制光标
            if (_isEditing && Focused)
            {
                string head = _inputText.Substring(0, Math.Min(_caret, _inputText.Length));
                float headWidth = g.MeasureString(head, Font).Width;
                float fullWidth = g.MeasureString(_inputText, Font).Width;
                float left;
                if (TextAlign == HorizontalAlignment.Left) left = 2;
                else if (TextAlign == HorizontalAlignment.Right) left = Width - fullWidth - 2;
                else left = (Width - fullWidth) / 2f;
                left += headWidth;
                g.DrawLine(CaretPen, left, 4, left, Height - 4);
            }
        }
    }
    }
}
