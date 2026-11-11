using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace IslandIntegrated {
  public sealed class ThemeSelectedEventArgs : EventArgs {
    public string Theme { get; private set; }
    public ThemeSelectedEventArgs(string theme) { Theme = Palette.ValidTheme(theme); }
  }

  // A compact owner-drawn glass popup. ToolStripDropDownMenu keeps the native
  // keyboard menu behavior while the renderer owns all visible chrome.
  public sealed class ThemePicker : ToolStripDropDownMenu {
    static readonly string[] ThemeIds = { "tide", "pine", "iris", "amber" };
    static readonly string[] ThemeNames = { "深海蓝灰", "墨松绿", "烟紫黑", "暖烟棕" };
    const string RandomId = "__random__";
    const int MenuWidth = 216, MenuHeight = 240;

    sealed class ThemeRenderer : ToolStripProfessionalRenderer, IDisposable {
      readonly Font titleFont = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold, GraphicsUnit.Pixel);
      readonly Font itemFont = new Font("Microsoft YaHei UI", 14f, FontStyle.Regular, GraphicsUnit.Pixel);
      readonly StringFormat textFormat = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center };
      public ThemeRenderer() { RoundedEdges = false; }

      public void Dispose() { titleFont.Dispose(); itemFont.Dispose(); textFormat.Dispose(); }

      protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        if (SystemInformation.HighContrast) {
          using (var path = GlassSurface.Path(bounds, 18))
          using (var brush = new SolidBrush(SystemColors.Window)) g.FillPath(brush, path);
          return;
        }
        GlassSurface.Paint(g, bounds, 18, true);
      }

      protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Color edge = SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(88, Palette.Text);
        using (var path = GlassSurface.Path(new RectangleF(.5f, .5f, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 18))
        using (var pen = new Pen(edge, SystemInformation.HighContrast ? 1.5f : 1f)) e.Graphics.DrawPath(pen, path);
      }

      protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) {
        if (e.Item is ToolStripLabel) return;
        var item = e.Item as ToolStripMenuItem;
        bool selected = e.Item.Selected;
        bool checkedTheme = item != null && item.Checked;
        if (!selected && !checkedTheme && (item == null || (string)item.Tag != RandomId)) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        RectangleF rect = new RectangleF(2, 1, e.Item.Width - 4, e.Item.Height - 2);
        if (SystemInformation.HighContrast) {
          if (selected) using (var path = GlassSurface.Path(rect, 8)) using (var brush = new SolidBrush(SystemColors.Highlight)) e.Graphics.FillPath(brush, path);
          return;
        }
        if (item != null && (string)item.Tag == RandomId) {
          using (var pen = new Pen(Color.FromArgb(48, Palette.Text), 1)) {
            pen.DashStyle = DashStyle.Solid;
            e.Graphics.DrawLine(pen, 10, 0, e.Item.Width - 10, 0);
          }
        }
        int alpha = selected ? 47 : checkedTheme ? 27 : 0;
        if (alpha > 0) using (var path = GlassSurface.Path(rect, 8)) using (var brush = new SolidBrush(Color.FromArgb(alpha, Palette.Text))) e.Graphics.FillPath(brush, path);
      }

      protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) {
        var item = e.Item as ToolStripMenuItem;
        string id = item == null ? null : item.Tag as string;
        bool title = e.Item is ToolStripLabel;
        bool highContrast = SystemInformation.HighContrast;
        Color text = highContrast ? SystemColors.WindowText : Palette.Text;
        if (highContrast && e.Item.Selected) text = SystemColors.HighlightText;
        float cy = e.Item.ContentRectangle.Top + e.Item.ContentRectangle.Height / 2f;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        if (title) {
          using (var brush = new SolidBrush(highContrast ? SystemColors.WindowText : Palette.Secondary))
            e.Graphics.DrawString(e.Text ?? "", titleFont, brush, new RectangleF(8, 0, e.Item.Width - 16, e.Item.Height), textFormat);
          return;
        }

        if (id == RandomId) {
          DrawShuffleMark(e.Graphics, 12, cy, highContrast ? text : Palette.Accent);
        } else {
          Color dot = ThemeColor(id);
          using (var brush = new SolidBrush(dot)) e.Graphics.FillEllipse(brush, 12, cy - 6, 12, 12);
          using (var pen = new Pen(highContrast ? SystemColors.WindowText : Color.FromArgb(150, Color.White), 1f)) e.Graphics.DrawEllipse(pen, 12, cy - 6, 12, 12);
        }

        using (var brush = new SolidBrush(text))
          e.Graphics.DrawString(e.Text ?? "", itemFont, brush, new RectangleF(36, e.Item.ContentRectangle.Top, e.Item.Width - 64, e.Item.Height), textFormat);

        if (item != null && item.Checked) {
          using (var pen = new Pen(text, 1.8f)) {
            pen.StartCap = pen.EndCap = LineCap.Round;
            float x = e.Item.Width - 18;
            e.Graphics.DrawLine(pen, x - 4, cy, x - 1, cy + 3);
            e.Graphics.DrawLine(pen, x - 1, cy + 3, x + 5, cy - 4);
          }
        }
      }

      protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Color color = SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(65, Palette.Text);
        using (var pen = new Pen(color, 1)) e.Graphics.DrawLine(pen, 12, e.Item.Height / 2, e.Item.Width - 12, e.Item.Height / 2);
      }

      protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

      static void DrawShuffleMark(Graphics g, float x, float y, Color color) {
        using (var pen = new Pen(color, 1.6f)) {
          pen.StartCap = pen.EndCap = LineCap.Round;
          g.DrawLine(pen, x, y - 5, x + 8, y + 5);
          g.DrawLine(pen, x, y + 5, x + 8, y - 5);
          g.DrawLine(pen, x + 8, y - 5, x + 5, y - 5);
          g.DrawLine(pen, x + 8, y - 5, x + 8, y - 2);
          g.DrawLine(pen, x + 8, y + 5, x + 5, y + 5);
          g.DrawLine(pen, x + 8, y + 5, x + 8, y + 2);
        }
      }

      static Color ThemeColor(string theme) {
        switch (Palette.ValidTheme(theme)) {
          case "pine": return Color.FromArgb(106, 187, 154);
          case "iris": return Color.FromArgb(186, 147, 216);
          case "amber": return Color.FromArgb(213, 165, 117);
          default: return Color.FromArgb(107, 174, 232);
        }
      }
    }

    readonly ThemeRenderer themeRenderer;
    readonly Func<int, int> randomIndex;
    readonly Random random = new Random();
    string currentTheme = "tide";
    bool randomPending;

    public string CurrentTheme {
      get { return currentTheme; }
      set {
        currentTheme = Palette.ValidTheme(value);
        foreach (ToolStripItem item in Items) {
          var themeItem = item as ToolStripMenuItem;
          if (themeItem != null && (string)themeItem.Tag != RandomId) themeItem.Checked = (string)themeItem.Tag == currentTheme;
        }
        Invalidate();
      }
    }

    public event EventHandler<ThemeSelectedEventArgs> Selected;
    public event EventHandler RandomRequested;

    public bool RandomPending {
      get { return randomPending; }
      set {
        randomPending = value;
        foreach (ToolStripItem entry in Items) {
          var item = entry as ToolStripMenuItem;
          if (item == null || (string)item.Tag != RandomId) continue;
          item.Checked = randomPending;
          item.Text = randomPending ? "已安排，下次展开" : "随机主题";
          item.AccessibleDescription = randomPending
            ? "仅在本次运行内存中待执行；下次鼠标移到屏幕右边缘并展开侧栏时生效。退出或重启后请求会丢失。"
            : "安排在下次鼠标移到屏幕右边缘并展开侧栏时随机切换；不会立即切换。";
          item.ToolTipText = randomPending ? "仅内存待执行；退出或重启后会丢失" : "仅在下次右边缘展开时随机切换，不会立即改变主题";
        }
        Invalidate();
      }
    }

    public ThemePicker() : this(null) { }

    // The deterministic selector makes the random item testable without changing
    // production selection behavior. The argument is the number of alternatives.
    public ThemePicker(Func<int, int> chooseIndex) {
      randomIndex = chooseIndex;
      AutoSize = false;
      Size = new Size(MenuWidth, MenuHeight);
      MinimumSize = MaximumSize = Size;
      Padding = new Padding(12);
      ShowImageMargin = false;
      ShowCheckMargin = false;
      BackColor = SystemInformation.HighContrast ? SystemColors.Window : Palette.Base;
      ForeColor = SystemInformation.HighContrast ? SystemColors.WindowText : Palette.Text;
      themeRenderer = new ThemeRenderer();
      Renderer = themeRenderer;

      var header = new ToolStripLabel("主题外观") {
        AutoSize = false,
        Size = new Size(184, 18),
        Margin = new Padding(16, 12, 16, 6),
        Enabled = false,
        AccessibleName = "主题外观",
        AccessibleRole = AccessibleRole.StaticText
      };
      Items.Add(header);

      for (int i = 0; i < ThemeIds.Length; i++) AddTheme(ThemeIds[i], ThemeNames[i], 4);
      Items.Add(new ToolStripSeparator { AutoSize = false, Size = new Size(184, 1), Margin = new Padding(16, 0, 16, 3) });
      AddRandom();
      foreach (ToolStripItem item in Items) item.Overflow = ToolStripItemOverflow.Never;

      CurrentTheme = Palette.Theme;
      Opening += (sender, args) => CurrentTheme = currentTheme;
    }

    void AddTheme(string id, string name, int bottomGap) {
      var item = new ToolStripMenuItem(name) {
        AutoSize = false,
        Size = new Size(184, 30),
        Margin = new Padding(16, 0, 16, bottomGap),
        Tag = id,
        CheckOnClick = false,
        AccessibleName = name,
        AccessibleRole = AccessibleRole.MenuItem
      };
      item.Click += (sender, args) => SelectTheme(id);
      Items.Add(item);
    }

    void AddRandom() {
      var item = new ToolStripMenuItem("随机主题") {
        AutoSize = false,
        Size = new Size(184, 30),
        Margin = new Padding(16, 0, 16, 0),
        Tag = RandomId,
        CheckOnClick = false,
        AccessibleName = "随机选择其他主题",
        AccessibleDescription = "安排在下次鼠标移到屏幕右边缘并展开侧栏时随机切换；不会立即切换。",
        AccessibleRole = AccessibleRole.MenuItem
      };
      item.Click += (sender, args) => { var handler = RandomRequested; if (handler != null) handler(this, EventArgs.Empty); };
      Items.Add(item);
    }

    void SelectTheme(string theme) {
      CurrentTheme = theme;
      var handler = Selected;
      if (handler != null) handler(this, new ThemeSelectedEventArgs(currentTheme));
    }

    public new void Show(Control owner, Point position) {
      if (owner == null) { base.Show(position); return; }
      Rectangle area = Screen.FromControl(owner).WorkingArea;
      Rectangle bounds = PlaceInWorkArea(area, owner.PointToScreen(position), Size);
      base.Show(owner, owner.PointToClient(bounds.Location));
    }

    internal static Rectangle PlaceInWorkArea(Rectangle area, Point anchor, Size size) {
      int w = Math.Min(Math.Max(1, size.Width), Math.Max(1, area.Width));
      int h = Math.Min(Math.Max(1, size.Height), Math.Max(1, area.Height));
      int x = Math.Max(area.Left, Math.Min(anchor.X, area.Right - w));
      int y = anchor.Y;
      if (y + h > area.Bottom) y = anchor.Y - h;
      y = Math.Max(area.Top, Math.Min(y, area.Bottom - h));
      return new Rectangle(x, y, w, h);
    }

    protected override void OnSizeChanged(EventArgs e) {
      base.OnSizeChanged(e);
      if (Width < 2 || Height < 2) return;
      using (var path = GlassSurface.Path(new RectangleF(0, 0, Width, Height), 18)) {
        var old = Region;
        Region = new Region(path);
        if (old != null) old.Dispose();
      }
    }

    protected override void Dispose(bool disposing) {
      if (disposing) {
        var old = Region;
        Region = null;
        if (old != null) old.Dispose();
        if (themeRenderer != null) themeRenderer.Dispose();
      }
      base.Dispose(disposing);
    }
  }
}
