using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using CyberWishMachine;

// 针对中/英双语界面的专项验证：
//   - ui.ini 里 language=en / zh / auto 三档的界面文字
//   - 齿轮菜单的语言子菜单与勾选状态
//   - 切换语言后立即生效并写回 ui.ini
//   - 系统语言检测：en-* → 英文，其余（含非中英）→ 中文
//   - 磁盘上的素材名属于用户数据，不随语言翻译
static class I18nTest
{
    const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly Type T = typeof(Form1);
    static int _fail;

    static void Check(string name, bool ok, string detail)
    {
        if (!ok) _fail++;
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name + "  " + detail);
    }

    static object Get(Form1 f, string field)
    {
        return T.GetField(field, F).GetValue(f);
    }

    static object Call(Form1 f, string method, params object[] args)
    {
        return T.GetMethod(method, F).Invoke(f, args);
    }

    static bool UiEnglish
    {
        get { return (bool)T.GetField("UiEnglish", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null); }
    }

    [STAThread]
    static int Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // 临时资源目录放在程序目录下（用完即删）：有些受限环境里 %TEMP% 不可写。
        string appDir = Path.Combine(
            Path.GetDirectoryName(typeof(I18nTest).Assembly.Location), "_i18n_test");
        try
        {
            if (Directory.Exists(appDir)) Directory.Delete(appDir, true);
            Directory.CreateDirectory(Path.Combine(appDir, "Assets"));
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL  临时目录不可用: " + ex.Message);
            return 1;
        }

        File.WriteAllText(Path.Combine(appDir, "Assets", "ui.ini"), "language=en\r\n");
        var ctor = T.GetConstructor(F, null, new Type[] { typeof(string) }, null);

        try
        {
            Console.WriteLine("--- 英文（ui.ini: language=en） ---");
            var form = (Form1)ctor.Invoke(new object[] { appDir });
            form.Show();
            Application.DoEvents();

            Check("窗口标题", form.Text == "Offering Wish Machine", "'" + form.Text + "'");
            Check("受供者标题", ((Label)Get(form, "_lblDeities")).Text == "Deities", "");
            Check("新建按钮", ((Button)Get(form, "_btnDeityNew")).Text == "New", "");
            Check("删除按钮", ((Button)Get(form, "_btnDeityDelete")).Text == "Delete", "");
            Check("上贡按钮", ((Button)Get(form, "_btnIncense")).Text == "Offer", "");
            Check("自动上贡按钮", ((Button)Get(form, "_btnAutoIncense")).Text == "Auto", "");
            Check("倍速标签", ((Label)Get(form, "_lblSpeed")).Text.StartsWith("Speed  x"),
                "'" + ((Label)Get(form, "_lblSpeed")).Text + "'");
            Check("默认飘字", (string)T.GetMethod("GetDeityResponseText", F)
                .Invoke(form, new object[] { null }) == "Merit +1", "");

            var tn = Get(form, "_txtDeityName");
            var ph = tn.GetType().GetProperty("Placeholder", BindingFlags.Public | BindingFlags.Instance);
            Check("占位符", ph != null && (string)ph.GetValue(tn, null) == "Enter deity name", "");

            var menu = (ContextMenuStrip)Call(form, "BuildSettingsMenu");
            bool hasLang = false, enChecked = false, autoChecked = false;
            foreach (ToolStripItem it in menu.Items)
            {
                if (it.Text != "Language") continue;
                hasLang = true;
                foreach (ToolStripItem sub in ((ToolStripMenuItem)it).DropDownItems)
                {
                    if (sub.Text == "English") enChecked = ((ToolStripMenuItem)sub).Checked;
                    if (sub.Text == "Follow system") autoChecked = ((ToolStripMenuItem)sub).Checked;
                }
            }
            Check("菜单有 Language 项", hasLang, "");
            Check("English 已勾选", enChecked, "");
            Check("Follow system 未勾选", !autoChecked, "");

            // 素材名是磁盘数据，不翻译
            var dirs = Directory.GetDirectories(Path.Combine(appDir, "Assets", "Deities"));
            Check("默认受供者目录仍是「自己」", dirs.Length == 1 && Path.GetFileName(dirs[0]) == "自己",
                dirs.Length == 1 ? Path.GetFileName(dirs[0]) : "(数量 " + dirs.Length + ")");

            Console.WriteLine();
            Console.WriteLine("--- 手动切到中文 ---");
            Call(form, "SetLanguageMode", "zh");
            Application.DoEvents();
            Check("标题变中文", form.Text == "上贡许愿机", "'" + form.Text + "'");
            Check("受供者变中文", ((Label)Get(form, "_lblDeities")).Text == "受供者", "");
            Check("上贡按钮变中文", ((Button)Get(form, "_btnIncense")).Text == "上贡", "");
            string ini = File.ReadAllText(Path.Combine(appDir, "Assets", "ui.ini"));
            Check("写回 language=zh", ini.Contains("language=zh"), ini.Replace("\r\n", " | "));

            Console.WriteLine();
            Console.WriteLine("--- 切回 auto（本机系统 zh-CN） ---");
            Call(form, "SetLanguageMode", "auto");
            Application.DoEvents();
            Check("auto 下为中文", form.Text == "上贡许愿机", "'" + form.Text + "'");
            ini = File.ReadAllText(Path.Combine(appDir, "Assets", "ui.ini"));
            Check("写回 language=auto", ini.Contains("language=auto"), ini.Replace("\r\n", " | "));

            Console.WriteLine();
            Console.WriteLine("--- 系统语言检测（auto 档） ---");
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
            Call(form, "ApplyLanguageSetting");
            Check("en-US → 英文", UiEnglish, "UiEnglish=" + UiEnglish);
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-GB");
            Call(form, "ApplyLanguageSetting");
            Check("en-GB → 英文", UiEnglish, "UiEnglish=" + UiEnglish);
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("zh-TW");
            Call(form, "ApplyLanguageSetting");
            Check("zh-TW → 中文", !UiEnglish, "UiEnglish=" + UiEnglish);
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("ja-JP");
            Call(form, "ApplyLanguageSetting");
            Check("ja-JP → 默认中文", !UiEnglish, "UiEnglish=" + UiEnglish);

            form.Dispose();
        }
        finally
        {
            Thread.CurrentThread.CurrentUICulture = CultureInfo.CurrentCulture;
            try { Directory.Delete(appDir, true); } catch { }
        }

        Console.WriteLine();
        Console.WriteLine(_fail == 0 ? "RESULT_OK" : ("FAILURES=" + _fail));
        return _fail == 0 ? 0 : 1;
    }
}
