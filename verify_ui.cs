// 验证脚本：检查列表控件透明是否为“真透明”，以及齿轮菜单的收起功能。
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;
using CyberWishMachine;

class VerifyUi
{
    const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    static Type T = typeof(Form1);

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var form = new Form1();
        form.Show();
        Application.DoEvents();

        var lstDeities = (Control)T.GetField("_lstDeities", F).GetValue(form);
        var lstOfferings = (Control)T.GetField("_lstOfferings", F).GetValue(form);
        var btnSettings = (Button)T.GetField("_btnSettings", F).GetValue(form);

        Console.WriteLine("Form ClientSize = " + form.ClientSize);
        Console.WriteLine("背景图已加载  = " + (form.BackgroundImage != null)
            + (form.BackgroundImage != null ? "  尺寸=" + form.BackgroundImage.Width + "x" + form.BackgroundImage.Height : ""));
        if (form.BackgroundImage == null)
        {
            Console.WriteLine("!! 背景图未加载，透明测试无意义（请在与 exe 同级的目录运行本验证程序）");
            return;
        }
        Console.WriteLine("_lstDeities   位置=" + lstDeities.Location + " 大小=" + lstDeities.Size + " 可见=" + lstDeities.Visible);
        Console.WriteLine("_lstOfferings 位置=" + lstOfferings.Location + " 大小=" + lstOfferings.Size + " 可见=" + lstOfferings.Visible);
        Console.WriteLine("齿轮按钮      位置=" + btnSettings.Location + " 大小=" + btnSettings.Size + " 文字='" + btnSettings.Text + "'");
        Console.WriteLine("齿轮在最上层  = " + (form.Controls.GetChildIndex(btnSettings) == 0));

        // ---- 齿轮菜单：真实构造菜单并触发菜单项 ----
        Console.WriteLine();
        Console.WriteLine("--- 齿轮菜单 ---");
        var menu = (ContextMenuStrip)T.GetMethod("BuildSettingsMenu", F).Invoke(form, null);
        Console.WriteLine("菜单项数 = " + menu.Items.Count);
        foreach (ToolStripItem it in menu.Items)
            Console.WriteLine("  [" + it.GetType().Name + "] '" + it.Text + "'"
                + (it is ToolStripMenuItem ? "  Checked=" + ((ToolStripMenuItem)it).Checked : ""));
        var miLeft = (ToolStripMenuItem)menu.Items[0];
        var miRight = (ToolStripMenuItem)menu.Items[1];
        var miTrans = (ToolStripMenuItem)menu.Items[3];

        Console.WriteLine();
        Console.WriteLine("--- 收起左/右侧栏（触发菜单项 Click） ---");
        miLeft.PerformClick();
        Application.DoEvents();
        Console.WriteLine("收起左后: 左列可见=" + lstDeities.Visible + " 右列可见=" + lstOfferings.Visible
            + "  齿轮位置=" + btnSettings.Location);
        miRight.PerformClick();
        Application.DoEvents();
        Console.WriteLine("收起右后: 左列可见=" + lstDeities.Visible + " 右列可见=" + lstOfferings.Visible);
        miLeft.PerformClick();
        miRight.PerformClick();
        Application.DoEvents();
        Console.WriteLine("全部展开: 左列可见=" + lstDeities.Visible + " 右列可见=" + lstOfferings.Visible);

        // 收起后中间内容区应重新居中
        miLeft.PerformClick();
        Application.DoEvents();
        var txtName = (Control)T.GetField("_txtDeityName", F).GetValue(form);
        Console.WriteLine("仅收起左时 内容区 x=" + txtName.Left + " 宽=" + txtName.Width
            + "（窗体宽 " + form.ClientSize.Width + "）");
        miLeft.PerformClick();
        Application.DoEvents();

        // 用同一组采样点，比较 “透明开” 与 “透明关” 的偏离度：
        // 若透明确实生效，关闭后偏离度应显著上升（列表变成不透明白底）
        var img0 = form.BackgroundImage;
        float sx0 = (float)img0.Width / form.ClientSize.Width;
        float sy0 = (float)img0.Height / form.ClientSize.Height;
        var pts = SamplePoints(lstDeities, form.ClientSize);
        var withTrans = Sample(form, lstDeities, pts, sx0, sy0);
        Stat noTrans;
        Console.WriteLine("透明开: " + Describe(withTrans));
        miTrans.PerformClick();
        Application.DoEvents();
        noTrans = Sample(form, lstDeities, pts, sx0, sy0);
        Console.WriteLine("透明关: " + Describe(noTrans));
        Console.WriteLine("透明生效 = " + (noTrans.meanDeviation > withTrans.meanDeviation * 2));
        miTrans.PerformClick();
        Application.DoEvents();
        Console.WriteLine("切回后 TransparentLists = " + T.GetField("_transparentLists", F).GetValue(form));

        // ---- 选中与事件 ----
        Console.WriteLine();
        Console.WriteLine("--- 列表交互 ---");
        var lb = lstDeities;
        var itemsProp = lb.GetType().GetProperty("Items");
        var items = itemsProp.GetValue(lb, null);
        Console.WriteLine("受供者项数 = " + lb.GetType().GetProperty("Count").GetValue(lb, null));
        Console.WriteLine("当前选中   = " + lb.GetType().GetProperty("SelectedItem").GetValue(lb, null));
        var firstItem = (string)lb.GetType().GetProperty("SelectedItem").GetValue(lb, null);
        Console.WriteLine("IndexOf(" + firstItem + ") = "
            + lb.GetType().GetMethod("IndexOf").Invoke(lb, new object[] { firstItem })
            + "（首项应为 0）");
        Console.WriteLine("IndexOf(不存在的受供者) = "
            + lb.GetType().GetMethod("IndexOf").Invoke(lb, new object[] { "__不存在的受供者__" })
            + "（应为 -1）");

        var pbDeity = (PictureBox)T.GetField("_pbDeity", F).GetValue(form);
        Console.WriteLine("主图已加载 = " + (pbDeity.Image != null));
        lb.GetType().GetProperty("SelectedIndex").SetValue(lb, 1, null);   // 切换到第 2 项
        Application.DoEvents();
        Console.WriteLine("切到索引1后 SelectedItem = " + lb.GetType().GetProperty("SelectedItem").GetValue(lb, null)
            + "  主图已加载 = " + (pbDeity.Image != null));
        Console.WriteLine("_currentDeityFile = " + System.IO.Path.GetFileName((string)T.GetField("_currentDeityFile", F).GetValue(form)));

        // ---- 键盘滚动 ----
        int before = (int)lb.GetType().GetProperty("SelectedIndex").GetValue(lb, null);
        lb.Focus();
        var onKey = lb.GetType().GetMethod("OnKeyDown", F);
        onKey.Invoke(lb, new object[] { new KeyEventArgs(Keys.Down) });
        int after = (int)lb.GetType().GetProperty("SelectedIndex").GetValue(lb, null);
        Console.WriteLine("按 ↓ 前/后选中 = " + before + " / " + after);

        // ---- 上贡动画 ----
        Console.WriteLine();
        Console.WriteLine("--- 上贡动画 ---");
        var overlaysField = T.GetField("_activeOverlays", F);
        var btnIncense = (Button)T.GetField("_btnIncense", F).GetValue(form);
        btnIncense.PerformClick();
        Application.DoEvents();
        var overlays = overlaysField.GetValue(form) as System.Collections.IList;
        Console.WriteLine("点击上贡后 浮动层数 = " + (overlays == null ? -1 : overlays.Count));

        // ---- 自动上贡 ----
        Console.WriteLine();
        Console.WriteLine("--- 自动上贡 ---");
        var btnAuto = (Button)T.GetField("_btnAutoIncense", F).GetValue(form);
        btnAuto.PerformClick();
        Application.DoEvents();
        Console.WriteLine("开启后: 按钮文字='" + btnAuto.Text + "' 计时器=" + (T.GetField("_autoIncenseTimer", F).GetValue(form) != null)
            + " 激活=" + T.GetField("_autoIncenseActive", F).GetValue(form));
        btnAuto.PerformClick();
        Application.DoEvents();
        Console.WriteLine("关闭后: 按钮文字='" + btnAuto.Text + "' 计时器=" + (T.GetField("_autoIncenseTimer", F).GetValue(form) != null)
            + " 激活=" + T.GetField("_autoIncenseActive", F).GetValue(form));

        Console.WriteLine("RESULT_OK");

        // ---- 窗口尺寸边界 ----
        Console.WriteLine();
        Console.WriteLine("--- 窗口尺寸边界 ---");
        foreach (int width in new[] { 700, 560, 400, 360, 359, 300, 200, 120 })
        {
            form.ClientSize = new Size(width, 480);
            Application.DoEvents();
            var tn = (Control)T.GetField("_txtDeityName", F).GetValue(form);
            var bIncense = (Control)T.GetField("_btnIncense", F).GetValue(form);
            Console.WriteLine("宽 " + width + ": 左列=" + lstDeities.Visible + " 右列=" + lstOfferings.Visible
                + " 内容区x=" + tn.Left + " 宽=" + tn.Width
                + " | 齿轮可见=" + btnSettings.Visible + " 齿轮x=" + btnSettings.Location.X
                + " 上贡按钮x=" + bIncense.Left + " 右缘=" + (bIncense.Left + bIncense.Width));
        }
        form.Dispose();

        // ---- 条目超出可视区时的滚动 ----
        Console.WriteLine();
        Console.WriteLine("--- 滚动（独立控件，40 项） ---");
        CheckScrolling();
        CheckEmpty();
    }

    static void CheckScrolling()
    {
        // FlatListBox 是 Form1 的私有嵌套类，用反射构造
        var lt = T.GetNestedType("FlatListBox", BindingFlags.NonPublic);
        var lb = (Control)Activator.CreateInstance(lt);
        lb.Font = new Font("Microsoft YaHei UI", 9F);
        lb.Size = new Size(150, 250);   // 该高度下可视 11 行
        var items = lt.GetProperty("Items").GetValue(lb, null) as System.Collections.Generic.IList<string>;
        for (int i = 0; i < 40; i++) items.Add("测试" + i.ToString("00"));

        var sel = lt.GetProperty("SelectedIndex");
        var scrollField = lt.GetField("_scroll", F);
        var onKey = lt.GetMethod("OnKeyDown", F);
        var onWheel = lt.GetMethod("OnMouseWheel", F);

        Console.WriteLine("项数 = " + lt.GetProperty("Count").GetValue(lb, null)
            + "  高度 = " + lb.Height + "  初始选中 = " + sel.GetValue(lb, null)
            + "  初始 _scroll = " + scrollField.GetValue(lb));

        for (int i = 0; i < 45; i++) onKey.Invoke(lb, new object[] { new KeyEventArgs(Keys.Down) });
        Console.WriteLine("连按 45 次 ↓ 后 SelectedIndex = " + sel.GetValue(lb, null)
            + "  _scroll = " + scrollField.GetValue(lb) + "（选中应为 39，不越界）");

        for (int i = 0; i < 45; i++) onKey.Invoke(lb, new object[] { new KeyEventArgs(Keys.Up) });
        Console.WriteLine("连按 45 次 ↑ 后 SelectedIndex = " + sel.GetValue(lb, null)
            + "  _scroll = " + scrollField.GetValue(lb) + "（应为 0 / 0）");

        onKey.Invoke(lb, new object[] { new KeyEventArgs(Keys.End) });
        Console.WriteLine("End  -> SelectedIndex = " + sel.GetValue(lb, null) + "  _scroll = " + scrollField.GetValue(lb));
        onKey.Invoke(lb, new object[] { new KeyEventArgs(Keys.Home) });
        Console.WriteLine("Home -> SelectedIndex = " + sel.GetValue(lb, null) + "  _scroll = " + scrollField.GetValue(lb));

        for (int i = 0; i < 60; i++) onWheel.Invoke(lb, new object[] { new MouseEventArgs(MouseButtons.None, 0, 10, 10, 120) });
        Console.WriteLine("滚轮上 60 次 _scroll = " + scrollField.GetValue(lb) + "（应为 0）");
        for (int i = 0; i < 120; i++) onWheel.Invoke(lb, new object[] { new MouseEventArgs(MouseButtons.None, 0, 10, 10, -120) });
        Console.WriteLine("滚轮下 120 次 _scroll = " + scrollField.GetValue(lb) + "（上限应为 40-11=29）");

        var onDown = lt.GetMethod("OnMouseDown", F);
        var onMove = lt.GetMethod("OnMouseMove", F);
        onDown.Invoke(lb, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 10, 20, 0) });
        Console.WriteLine("点击 y=20 -> SelectedIndex = " + sel.GetValue(lb, null) + "（应为 29+1=30）");
        onDown.Invoke(lb, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 10, 240, 0) });
        onMove.Invoke(lb, new object[] { new MouseEventArgs(MouseButtons.Left, 0, 10, 60, 0) });
        Console.WriteLine("空白处上拖后 _scroll = " + scrollField.GetValue(lb) + "（应减小且不越界）");

        Console.WriteLine("RESULT_OK");
    }

    static void CheckEmpty()
    {
        // 空资源目录下构造窗体不应崩溃。
        // 原实现试图反射改写 Application.StartupPath（静态只读属性，改不了），
        // 因此这段检查从未真正生效过；现在改走 Form1 的 internal 构造重载。
        // 临时目录放在程序目录下（用完即删）：有些受限环境里 %AppData% 不可写。
        string appDir = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(typeof(VerifyUi).Assembly.Location), "_empty_test");
        try
        {
            if (System.IO.Directory.Exists(appDir)) System.IO.Directory.Delete(appDir, true);
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(appDir, "Assets", "Deities"));
        }
        catch (Exception ex)
        {
            // 程序目录不可写时跳过这一项，而不是把整轮回归判成失败
            Console.WriteLine();
            Console.WriteLine("--- 空资源目录 ---");
            Console.WriteLine("  （跳过：临时目录不可用，" + ex.GetType().Name + "）");
            return;
        }
        Console.WriteLine();
        Console.WriteLine("--- 空资源目录 ---");
        Console.WriteLine("  资源目录 = " + appDir);
        Console.Out.Flush();

        var ctor = T.GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(string) }, null);
        var f = (Form1)ctor.Invoke(new object[] { appDir });
        Console.WriteLine("  Form1 构造完成");
        Console.Out.Flush();
        f.Show();
        Console.WriteLine("  Show() 返回");
        Console.Out.Flush();
        Application.DoEvents();
        Console.WriteLine("  消息循环处理完成，未崩溃");
        Console.Out.Flush();

        // 应当自动补齐了默认受供者与默认贡品
        var deitiesDir = System.IO.Path.Combine(appDir, "Assets", "Deities");
        var offeringsDir = System.IO.Path.Combine(appDir, "Assets", "Offerings");
        var deityDirs = System.IO.Directory.GetDirectories(deitiesDir);
        var offeringFiles = System.IO.Directory.GetFiles(offeringsDir);
        Console.WriteLine("  默认受供者目录数 = " + deityDirs.Length
            + "  默认贡品数 = " + offeringFiles.Length);

        f.Dispose();
        Console.WriteLine("  Dispose 完成");
        Console.Out.Flush();
        System.IO.Directory.Delete(appDir, true);
        Console.WriteLine("RESULT_OK");
        Console.Out.Flush();
    }

    class Stat
    {
        public int total, match, close, far;
        public double sum, meanDeviation;
        public string Describe()
        {
            return string.Format("采样 {0} 点 | 完全一致 {1} | 接近 {2} | 明显不同 {3} | 平均偏离 {4:F1}",
                total, match, close, far, meanDeviation);
        }
    }

    static string Describe(Stat s) { return s.Describe(); }

    static Point[] SamplePoints(Control list, Size client)
    {
        var list2 = new System.Collections.Generic.List<Point>();
        for (int dx = 4; dx < list.Width - 6; dx += 7)
            for (int dy = 3; dy < list.Height - 20; dy += 7)   // 跳过底部状态条
                list2.Add(new Point(list.Left + dx, list.Top + dy));
        return list2.ToArray();
    }

    static Stat Sample(Form1 form, Control list, Point[] pts, float sx, float sy)
    {
        var st = new Stat();
        using (var bmp = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
        {
            form.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
            var bg = (Bitmap)form.BackgroundImage;
            foreach (var p in pts)
            {
                if (p.X < 0 || p.Y < 0 || p.X >= bmp.Width || p.Y >= bmp.Height) continue;
                Color actual = bmp.GetPixel(p.X, p.Y);
                Color expected = bg.GetPixel(
                    Math.Min(bg.Width - 1, (int)(p.X * sx)), Math.Min(bg.Height - 1, (int)(p.Y * sy)));
                int diff = Math.Abs(actual.R - expected.R) + Math.Abs(actual.G - expected.G) + Math.Abs(actual.B - expected.B);
                st.total++;
                st.sum += diff;
                if (diff <= 6) st.match++;
                else if (diff <= 60) st.close++;
                else st.far++;
            }
        }
        st.meanDeviation = st.total > 0 ? st.sum / st.total : 0;
        return st;
    }
}

