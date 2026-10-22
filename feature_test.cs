using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using CyberWishMachine;

// 针对速度条、多飘字、自定义快捷键与设置持久化的专项验证。
// 用临时资源目录，避免污染仓库里的 Assets\logs。
static class FeatureTest
{
    static int _fail;

    static void Check(string name, bool ok, string detail)
    {
        if (!ok) _fail++;
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name + "  " + detail);
    }

    static object Get(Form1 f, string field)
    {
        return typeof(Form1).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(f);
    }

    static void Set(Form1 f, string field, object v)
    {
        typeof(Form1).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(f, v);
    }

    static object Call(Form1 f, string method, params object[] args)
    {
        var m = typeof(Form1).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
        return m.Invoke(f, args);
    }

    [STAThread]
    static int Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // 临时资源目录放在程序目录下（用完即删）：有些受限环境里 %TEMP% 不可写。
        string appDir = Path.Combine(
            Path.GetDirectoryName(typeof(FeatureTest).Assembly.Location), "_feature_test");
        try
        {
            if (Directory.Exists(appDir)) Directory.Delete(appDir, true);
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL  临时目录不可用: " + ex.Message);
            return 1;
        }

        var ctor = typeof(Form1).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(string) }, null);
        var form = (Form1)ctor.Invoke(new object[] { appDir });
        form.Show();
        Application.DoEvents();

        var slider = (Control)Get(form, "_tbSpeed");
        var lbl = (Label)Get(form, "_lblSpeed");

        Console.WriteLine("--- 速度条 ---");
        Check("速度条存在且可见", slider != null && slider.Visible, "Visible=" + (slider != null && slider.Visible));
        Check("标签存在", lbl != null, lbl == null ? "null" : lbl.Text);

        // 通过控件的 Value 走真实的 ValueChanged 链路
        var valueProp = slider.GetType().GetProperty("Value");
        var minProp = slider.GetType().GetProperty("Minimum");
        var maxProp = slider.GetType().GetProperty("Maximum");

        Console.WriteLine("--- 速度条 ---  Minimum=" + minProp.GetValue(slider, null)
            + " Maximum=" + maxProp.GetValue(slider, null));

        // 边界：设为最小 / 最大
        valueProp.SetValue(slider, 1, null);
        Application.DoEvents();
        int s1 = (int)Get(form, "_autoIncenseSpeed");
        double iv1 = (double)typeof(Form1).GetProperty("IncenseIntervalMs",
            BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form, null);
        Check("x1 生效", s1 == 1, "speed=" + s1 + " interval=" + iv1.ToString("0.0") + "ms（应为 250）");

        valueProp.SetValue(slider, 50, null);
        Application.DoEvents();
        int s50 = (int)Get(form, "_autoIncenseSpeed");
        double iv50 = (double)typeof(Form1).GetProperty("IncenseIntervalMs",
            BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form, null);
        Check("x50 生效", s50 == 50, "speed=" + s50 + " interval=" + iv50.ToString("0.0") + "ms（应为 5）");
        Check("标签同步显示", lbl != null && lbl.Text.Contains("x50"), "label='" + (lbl == null ? "" : lbl.Text) + "'");

        // 越界保护
        valueProp.SetValue(slider, 999, null);
        Application.DoEvents();
        Check("超过上限被钳制", (int)Get(form, "_autoIncenseSpeed") <= 50,
            "speed=" + Get(form, "_autoIncenseSpeed"));

        Console.WriteLine();
        Console.WriteLine("--- 伪加速：每次上贡的飘字段数 ---");
        var copiesMethod = typeof(Form1).GetMethod("MeritCopiesForCurrentSpeed",
            BindingFlags.NonPublic | BindingFlags.Instance);

        valueProp.SetValue(slider, 1, null); Application.DoEvents();
        int maxAt1 = 1;
        long totalAt1 = 0;
        for (int i = 0; i < 2000; i++)
        {
            int c = (int)copiesMethod.Invoke(form, new object[] { "自动" });
            totalAt1 += c;
            if (c > maxAt1) maxAt1 = c;
        }
        Check("x1 时每次只飘 1 段", maxAt1 == 1, "最大段数=" + maxAt1 + " 平均=" + (totalAt1 / 2000.0).ToString("0.00"));

        valueProp.SetValue(slider, 5, null); Application.DoEvents();
        int maxAt5 = 1;
        for (int i = 0; i < 2000; i++)
        {
            int c = (int)copiesMethod.Invoke(form, new object[] { "自动" });
            if (c > maxAt5) maxAt5 = c;
        }
        Check("x5 时仍只飘 1 段（阈值为「超过 5 倍」）", maxAt5 == 1, "最大段数=" + maxAt5);

        valueProp.SetValue(slider, 50, null); Application.DoEvents();
        int maxAt50 = 1;
        long totalAt50 = 0;
        for (int i = 0; i < 4000; i++)
        {
            int c = (int)copiesMethod.Invoke(form, new object[] { "自动" });
            totalAt50 += c;
            if (c > maxAt50) maxAt50 = c;
        }
        double avg50 = totalAt50 / 4000.0;
        Check("x50 时明显多段", maxAt50 >= 3 && avg50 > 2.0,
            "最大段数=" + maxAt50 + " 平均=" + avg50.ToString("0.00") + " 段/次");

        // 手动上贡不受伪加速影响
        int manualMax = 1;
        for (int i = 0; i < 500; i++)
        {
            int c = (int)copiesMethod.Invoke(form, new object[] { "手动" });
            if (c > manualMax) manualMax = c;
        }
        Check("手动上贡始终只飘 1 段", manualMax == 1, "最大段数=" + manualMax);

        Console.WriteLine();
        Console.WriteLine("--- 速率累计（直接驱动 tick，不等待真实时间） ---");
        var intervalProp = typeof(Form1).GetProperty("IncenseIntervalMs",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var tickMethod = typeof(Form1).GetMethod("AutoIncenseTick",
            BindingFlags.NonPublic | BindingFlags.Instance);

        valueProp.SetValue(slider, 25, null); Application.DoEvents();
        double iv25 = (double)intervalProp.GetValue(form, null);
        int before;

        Set(form, "_autoIncenseActive", true);
        // 必须同时把 lastTicks 对齐到现在，否则 tick 会把「距上次切换的真实耗时」也算进去，
        // 测的就不是预置的积压量了。
        Set(form, "_autoIncenseLastTicks", Stopwatch.GetTimestamp());
        Set(form, "_autoIncenseAccumMs", iv25 * 10);
        before = (int)Get(form, "_incenseCount");
        tickMethod.Invoke(form, new object[] { null, EventArgs.Empty });
        int delta = (int)Get(form, "_incenseCount") - before;
        Check("x25 一次 tick 补做 10 次上贡", delta == 10, "实际=" + delta + " 次");

        // 上限保护：塞入远超允许的积压
        Set(form, "_autoIncenseActive", true);
        Set(form, "_autoIncenseLastTicks", Stopwatch.GetTimestamp());
        Set(form, "_autoIncenseAccumMs", iv25 * 10000);
        before = (int)Get(form, "_incenseCount");
        tickMethod.Invoke(form, new object[] { null, EventArgs.Empty });
        delta = (int)Get(form, "_incenseCount") - before;
        Check("卡顿后不会雪崩补做", delta <= 24, "实际=" + delta + " 次（上限 24）");

        Console.WriteLine();
        Console.WriteLine("--- 同屏飘字条数上限 ---");
        var overlaysField = typeof(Form1).GetField("_activeOverlays", BindingFlags.NonPublic | BindingFlags.Instance);
        valueProp.SetValue(slider, 50, null); Application.DoEvents();
        Set(form, "_autoIncenseActive", true);
        var incenseMethod = typeof(Form1).GetMethod("Incense", BindingFlags.NonPublic | BindingFlags.Instance);
        for (int i = 0; i < 300; i++) incenseMethod.Invoke(form, new object[] { "自动" });
        Application.DoEvents();

        var overlays = overlaysField.GetValue(form) as IList;
        int itemCount = -1;
        if (overlays != null && overlays.Count > 0)
        {
            var ov = overlays[0];
            var items = ov.GetType().GetField("Items", BindingFlags.Public | BindingFlags.Instance).GetValue(ov) as IList;
            itemCount = items == null ? -1 : items.Count;
        }
        Check("飘字条数被限制在 160 以内", itemCount >= 0 && itemCount <= 160, "当前条数=" + itemCount);
        Check("300 次上贡后仍有飘字层", overlays != null && overlays.Count > 0,
            "层数=" + (overlays == null ? -1 : overlays.Count));

        Console.WriteLine();
        Console.WriteLine("--- 快捷键 ---");
        var processCmdKey = typeof(Form1).GetMethod("ProcessCmdKey",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Keys hotkey = (Keys)Get(form, "_autoIncenseHotkey");
        Check("默认快捷键是 F10", hotkey == Keys.F10, "当前=" + hotkey);

        // 先确保处于停止状态
        Call(form, "StopAutoIncense");
        Application.DoEvents();
        bool activeBefore = (bool)Get(form, "_autoIncenseActive");

        var msg = new Message();
        var args = new object[] { msg, Keys.F10 };
        processCmdKey.Invoke(form, args);
        Application.DoEvents();
        bool activeAfter = (bool)Get(form, "_autoIncenseActive");
        string btnText = ((Button)Get(form, "_btnAutoIncense")).Text;
        Check("按 F10 开启自动上贡", !activeBefore && activeAfter, "前=" + activeBefore + " 后=" + activeAfter + " 按钮='" + btnText + "'");

        args = new object[] { new Message(), Keys.F10 };
        processCmdKey.Invoke(form, args);
        Application.DoEvents();
        bool activeAfter2 = (bool)Get(form, "_autoIncenseActive");
        string btnText2 = ((Button)Get(form, "_btnAutoIncense")).Text;
        Check("再按 F10 停止自动上贡", activeAfter && !activeAfter2, "后=" + activeAfter2 + " 按钮='" + btnText2 + "'");

        // 其它键不应触发
        args = new object[] { new Message(), Keys.F9 };
        processCmdKey.Invoke(form, args);
        Application.DoEvents();
        Check("无关按键不触发", !(bool)Get(form, "_autoIncenseActive"), "激活=" + Get(form, "_autoIncenseActive"));

        // 自定义快捷键后 F10 失效、新键生效
        Set(form, "_autoIncenseHotkey", Keys.F8);
        args = new object[] { new Message(), Keys.F10 };
        processCmdKey.Invoke(form, args);
        Application.DoEvents();
        bool afterOld = (bool)Get(form, "_autoIncenseActive");
        args = new object[] { new Message(), Keys.F8 };
        processCmdKey.Invoke(form, args);
        Application.DoEvents();
        bool afterNew = (bool)Get(form, "_autoIncenseActive");
        Check("改成 F8 后 F10 失效、F8 生效", !afterOld && afterNew,
            "F10后=" + afterOld + "  F8后=" + afterNew);

        // 菜单文字跟随
        Call(form, "StopAutoIncense");
        var menu = (ContextMenuStrip)typeof(Form1).GetMethod("BuildSettingsMenu",
            BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, null);
        string found = "(未找到)";
        foreach (ToolStripItem it in menu.Items)
        {
            if (it.Text != null && (it.Text.Contains("快捷键") || it.Text.Contains("Hotkey"))) found = it.Text;
        }
        Check("菜单显示当前快捷键", found.Contains("F8"), "菜单项='" + found + "'");

        Console.WriteLine();
        Console.WriteLine("--- 设置持久化 ---");
        string ini = Path.Combine(appDir, "Assets", "ui.ini");
        valueProp.SetValue(slider, 33, null);
        Application.DoEvents();
        Check("设置文件已写出", File.Exists(ini), ini);
        if (File.Exists(ini))
        {
            string content = File.ReadAllText(ini);
            Check("文件含 speed=33", content.Contains("speed=33"), content.Replace("\r\n", " | "));
            Check("文件含 hotkey=119（F8）", content.Contains("hotkey=119"), "");
        }

        // 重新开一个窗体，验证读回
        var form2 = (Form1)ctor.Invoke(new object[] { appDir });
        Check("重启后速度恢复", (int)Get(form2, "_autoIncenseSpeed") == 33,
            "speed=" + Get(form2, "_autoIncenseSpeed"));
        Check("重启后快捷键恢复", (Keys)Get(form2, "_autoIncenseHotkey") == Keys.F8,
            "hotkey=" + Get(form2, "_autoIncenseHotkey"));
        var slider2 = (Control)Get(form2, "_tbSpeed");
        Check("重启后滑块位置恢复", (int)valueProp.GetValue(slider2, null) == 33,
            "滑块=" + valueProp.GetValue(slider2, null));
        Check("重启后标签恢复", ((Label)Get(form2, "_lblSpeed")).Text.Contains("x33"),
            "'" + ((Label)Get(form2, "_lblSpeed")).Text + "'");
        form2.Dispose();

        // 坏的设置文件不应崩溃
        File.WriteAllText(ini, "speed=abc\nhotkey=xyz\n乱码行\n");
        var form3 = (Form1)ctor.Invoke(new object[] { appDir });
        Check("设置文件损坏时回退默认值",
            (int)Get(form3, "_autoIncenseSpeed") == 1 && (Keys)Get(form3, "_autoIncenseHotkey") == Keys.F10,
            "speed=" + Get(form3, "_autoIncenseSpeed") + " hotkey=" + Get(form3, "_autoIncenseHotkey"));
        form3.Dispose();

        Console.WriteLine();
        Console.WriteLine("--- 头衔与心愿按受供者保存 ---");
        {
            var lstDeities = Get(form, "_lstDeities");
            var titleBox = Get(form, "_txtDeityTitle");
            var wishBox = Get(form, "_txtWish");
            var titleTextProp = titleBox.GetType().GetProperty("InputText");
            var wishTextProp = wishBox.GetType().GetProperty("InputText");
            var selIndexProp = lstDeities.GetType().GetProperty("SelectedIndex");
            var indexOfM = lstDeities.GetType().GetMethod("IndexOf");

            // 造第二个受供者，用来验证「切换时各load各的」
            string deitiesDir = Path.Combine(appDir, "Assets", "Deities");
            string second = "测试乙";
            string secondDir = Path.Combine(deitiesDir, second);
            Directory.CreateDirectory(secondDir);
            File.Copy(Path.Combine(deitiesDir, "自己", "自己.png"),
                Path.Combine(secondDir, second + ".png"), true);
            Call(form, "LoadDeities");
            Application.DoEvents();

            int iSelf = (int)indexOfM.Invoke(lstDeities, new object[] { "自己" });
            int iSecond = (int)indexOfM.Invoke(lstDeities, new object[] { second });
            Check("两个受供者都在列表里", iSelf >= 0 && iSecond >= 0, "自己=" + iSelf + " " + second + "=" + iSecond);

            selIndexProp.SetValue(lstDeities, iSelf, null);
            Application.DoEvents();
            wishTextProp.SetValue(wishBox, "我要中彩票", null);
            titleTextProp.SetValue(titleBox, "财神爷", null);
            Application.DoEvents();

            string selfWishFile = Path.Combine(deitiesDir, "自己", "自己.wish");
            string selfMetaFile = Path.Combine(deitiesDir, "自己", "自己.meta");
            Check("心愿写进磁盘", File.Exists(selfWishFile) && File.ReadAllText(selfWishFile) == "我要中彩票",
                File.Exists(selfWishFile) ? "'" + File.ReadAllText(selfWishFile) + "'" : "(文件不存在)");
            Check("头衔写进磁盘", File.Exists(selfMetaFile) && File.ReadAllText(selfMetaFile) == "财神爷",
                File.Exists(selfMetaFile) ? "'" + File.ReadAllText(selfMetaFile) + "'" : "(文件不存在)");

            // 切到另一个受供者：右侧应换成它自己的（都为空）
            selIndexProp.SetValue(lstDeities, iSecond, null);
            Application.DoEvents();
            string wishAfterSwitch = (string)wishTextProp.GetValue(wishBox, null);
            string titleAfterSwitch = (string)titleTextProp.GetValue(titleBox, null);
            Check("切换后心愿为空", wishAfterSwitch == "", "'" + wishAfterSwitch + "'");
            Check("切换后头衔为空", titleAfterSwitch == "", "'" + titleAfterSwitch + "'");

            wishTextProp.SetValue(wishBox, "乙的心愿", null);
            titleTextProp.SetValue(titleBox, "乙的头衔", null);
            Application.DoEvents();

            // 再切回来：应恢复自己的那一份
            selIndexProp.SetValue(lstDeities, iSelf, null);
            Application.DoEvents();
            string wishBack = (string)wishTextProp.GetValue(wishBox, null);
            string titleBack = (string)titleTextProp.GetValue(titleBox, null);
            Check("切回后心愿恢复", wishBack == "我要中彩票", "'" + wishBack + "'");
            Check("切回后头衔恢复", titleBack == "财神爷", "'" + titleBack + "'");

            // 重新开一个窗体：内容必须从磁盘读回，而不是只存在内存里
            var formW = (Form1)ctor.Invoke(new object[] { appDir });
            var lstW = Get(formW, "_lstDeities");
            var wishW = Get(formW, "_txtWish");
            var titleW = Get(formW, "_txtDeityTitle");
            var indexOfW = lstW.GetType().GetMethod("IndexOf");
            ((System.Windows.Forms.Control)formW).CreateControl();
            lstW.GetType().GetProperty("SelectedIndex")
                .SetValue(lstW, (int)indexOfW.Invoke(lstW, new object[] { "自己" }), null);
            Application.DoEvents();
            string wishReload = (string)wishW.GetType().GetProperty("InputText").GetValue(wishW, null);
            string titleReload = (string)titleW.GetType().GetProperty("InputText").GetValue(titleW, null);
            Check("重启后心愿还在", wishReload == "我要中彩票", "'" + wishReload + "'");
            Check("重启后头衔还在", titleReload == "财神爷", "'" + titleReload + "'");
            formW.Dispose();
        }

        Console.WriteLine();
        Console.WriteLine("--- 速度条文字随背景明暗自适应 ---");
        {
            var resize = typeof(Form1).GetMethod("Form1_Resize",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var sliderOf = (Control)Get(form, "_tbSpeed");
            var lblOf = (Label)Get(form, "_lblSpeed");
            var trackProp = sliderOf.GetType().GetProperty("TrackColor");

            // 纯白壁纸 → 文字应为深色（否则白字白底看不见）
            var whiteBg = new System.Drawing.Bitmap(700, 480);
            using (var g = System.Drawing.Graphics.FromImage(whiteBg))
                g.Clear(System.Drawing.Color.White);
            form.BackgroundImage = whiteBg;
            resize.Invoke(form, new object[] { form, EventArgs.Empty });
            Application.DoEvents();
            var fgLight = lblOf.ForeColor;
            var tcLight = (System.Drawing.Color)trackProp.GetValue(sliderOf, null);
            Check("白底用深色文字", fgLight.R < 128,
                "R=" + fgLight.R + " G=" + fgLight.G + " B=" + fgLight.B);
            Check("白底用深色轨道", tcLight.R < 200, "R=" + tcLight.R + " A=" + tcLight.A);

            // 纯黑壁纸 → 文字应为浅色
            var blackBg = new System.Drawing.Bitmap(700, 480);
            using (var g = System.Drawing.Graphics.FromImage(blackBg))
                g.Clear(System.Drawing.Color.Black);
            form.BackgroundImage = blackBg;
            resize.Invoke(form, new object[] { form, EventArgs.Empty });
            Application.DoEvents();
            var fgDark = lblOf.ForeColor;
            var tcDark = (System.Drawing.Color)trackProp.GetValue(sliderOf, null);
            Check("黑底用浅色文字", fgDark.R > 128,
                "R=" + fgDark.R + " G=" + fgDark.G + " B=" + fgDark.B);
            Check("黑底用浅色轨道", tcDark.R > 200, "R=" + tcDark.R + " A=" + tcDark.A);

            whiteBg.Dispose();
            blackBg.Dispose();
        }

        Console.WriteLine();
        Console.WriteLine("--- 窄窗口布局不重叠 ---");
        {
            // 背景图已释放，先摘掉再重新布局，避免采样到已 Dispose 的位图
            form.BackgroundImage = null;
            var resize2 = typeof(Form1).GetMethod("Form1_Resize",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var layoutSlots = (System.Windows.Forms.PictureBox[])Get(form, "_pbOfferingSlots");
            var layoutSlider = (Control)Get(form, "_tbSpeed");
            var layoutGear = (Control)Get(form, "_btnSettings");
            var layoutOffNew = (Control)Get(form, "_btnOfferingNew");

            int[,] sizes = new int[,] { { 560, 400 }, { 600, 370 }, { 620, 380 }, { 700, 480 } };
            for (int si = 0; si < sizes.GetLength(0); si++)
            {
                int pw = sizes[si, 0], ph = sizes[si, 1];
                form.ClientSize = new System.Drawing.Size(pw, ph);
                resize2.Invoke(form, new object[] { form, EventArgs.Empty });
                Application.DoEvents();
                string tag = pw + "x" + ph;

                // 中间区（三列贡品）不能越过右栏左边界
                var last = layoutSlots[layoutSlots.Length - 1];
                int slotRight = last.Left + last.Width;
                Check("布局 " + tag + " 贡品不越右栏", slotRight <= layoutOffNew.Left,
                    "槽位右=" + slotRight + " 右栏左=" + layoutOffNew.Left);

                // 速度条不能压住贡品图，也不能被右下角的齿轮压住
                bool hitSlot = false;
                foreach (var slot in layoutSlots)
                {
                    var r = System.Drawing.Rectangle.Intersect(layoutSlider.Bounds, slot.Bounds);
                    if (r.Width > 0 && r.Height > 0) hitSlot = true;
                }
                var rg = System.Drawing.Rectangle.Intersect(layoutSlider.Bounds, layoutGear.Bounds);
                bool hitGear = rg.Width > 0 && rg.Height > 0;
                Check("布局 " + tag + " 速度条不压贡品", !hitSlot,
                    "滑块=" + layoutSlider.Bounds + " 槽位0=" + layoutSlots[0].Bounds);
                Check("布局 " + tag + " 速度条不被齿轮压住", !hitGear,
                    "滑块=" + layoutSlider.Bounds + " 齿轮=" + layoutGear.Bounds);
            }

            form.ClientSize = new System.Drawing.Size(700, 480);
            resize2.Invoke(form, new object[] { form, EventArgs.Empty });
        }

        form.Close();
        form.Dispose();
        try { Directory.Delete(appDir, true); } catch { }

        Console.WriteLine();
        if (_fail == 0) { Console.WriteLine("RESULT_OK"); return 0; }
        Console.WriteLine("RESULT_FAILED  失败项=" + _fail);
        return 1;
    }
}

