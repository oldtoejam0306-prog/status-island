using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace SidebarClockCalendar {
  // Place in a 344px card or use directly as the card body.
  public sealed class ClockCalendarControl : UserControl {
    private static Color PanelColor {get{return IslandIntegrated.Palette.SelectedText;}}
    private static Color TextColor {get{return IslandIntegrated.Palette.Text;}}
    private static Color MutedColor {get{return IslandIntegrated.Palette.Secondary;}}
    private static Color AccentColor {get{return IslandIntegrated.Palette.Accent;}}
    private readonly CalendarModel model;
    private readonly Timer timer;
    private readonly Label time = new Label(), date = new Label(), month = new Label();
    private readonly Button previous, next, today;
    private readonly Button[] days = new Button[42];
    private bool stopped, calendarRendered;
    private DateTime renderedToday;
    private Font controlFont, titleFont, timeFont;
    public ClockCalendarControl() : this(delegate { return DateTime.Now; }) { }
    public ClockCalendarControl(Func<DateTime> localDateTime) {
      model = new CalendarModel(localDateTime);
      Width = 344; Height = 380; SetStyle(ControlStyles.SupportsTransparentBackColor|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true); BackColor = Color.Transparent;
      controlFont = new Font("Microsoft YaHei UI", 9F); Font = controlFont; ForeColor = TextColor;
      AccessibleName = "时钟与月历";
      var title = NewLabel("时钟与月历", 20, 14, 200, 24); titleFont = new Font(Font, FontStyle.Bold); title.Font = titleFont; Controls.Add(title);
      time.SetBounds(20, 42, 200, 38); timeFont = new Font("Microsoft YaHei UI", 22F, FontStyle.Bold); time.Font = timeFont; time.BackColor=Color.Transparent; time.ForeColor = TextColor; Controls.Add(time);
      date.SetBounds(21, 82, 300, 24); date.BackColor=Color.Transparent; date.ForeColor = MutedColor; Controls.Add(date);
      previous = NewButton("‹", "上个月", 17, 111, 36, 28); previous.Click += delegate { if (model.MoveMonth(-1)) RenderCalendar(); };
      next = NewButton("›", "下个月", 224, 111, 36, 28); next.Click += delegate { if (model.MoveMonth(1)) RenderCalendar(); };
      today = NewButton("今天", "返回今天", 267, 111, 58, 28); today.Click += delegate { model.GoToToday(); RenderAll(); };
      month.SetBounds(60, 115, 157, 23); month.TextAlign = ContentAlignment.MiddleCenter; month.BackColor=Color.Transparent; month.ForeColor = TextColor; Controls.Add(month);
      string[] weekdays = { "一", "二", "三", "四", "五", "六", "日" };
      for (int i = 0; i < 7; i++) { var label = NewLabel(weekdays[i], 18 + i * 44, 148, 40, 22); label.TextAlign = ContentAlignment.MiddleCenter; label.ForeColor = MutedColor; Controls.Add(label); }
      for (int i = 0; i < days.Length; i++) {
        int index = i; Button b = NewButton("", "日期", 18 + (i % 7) * 44, 173 + (i / 7) * 32, 40, 29);
        b.Click += delegate { DateTime? cell = model.MonthCells()[index]; if (cell.HasValue) { model.Select(cell.Value); RenderCalendar(); } };
        days[i] = b;
      }
      timer = new Timer(); timer.Interval = 1000; timer.Tick += TimerTick; RenderAll(); timer.Start();
    }
    private Label NewLabel(string text, int x, int y, int w, int h) { var l = new Label(); l.BackColor=Color.Transparent; l.Text = text; l.SetBounds(x, y, w, h); l.ForeColor = TextColor; return l; }
    private Button NewButton(string text, string name, int x, int y, int w, int h) {
      var b = new Button(); b.Text = text; b.AccessibleName = name; b.SetBounds(x, y, w, h); b.FlatStyle = FlatStyle.Flat;
      b.FlatAppearance.BorderSize = 0; b.BackColor = Color.Transparent; b.ForeColor = TextColor; b.TabStop = true; Controls.Add(b); return b;
    }
    public DateTime SelectedDate { get { return model.SelectedDate; } }
    public DateTime DisplayMonth { get { return model.DisplayMonth; } }
    public void RestoreView(DateTime selected, DateTime displayed) {
      model.Select(selected);
      int delta = (displayed.Year - model.DisplayMonth.Year) * 12 + displayed.Month - model.DisplayMonth.Month;
      model.MoveMonth(delta); RenderCalendar();
    }
    private void TimerTick(object sender, EventArgs e) { if (stopped || IsDisposed || Disposing) return; model.RefreshClock(); RenderAll(); }
    private void RenderAll() {
      time.Text = CalendarModel.FormatTime(model.Now);
      string[] weekdays = { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };
      date.Text = CalendarModel.FormatDate(model.Now) + "  " + weekdays[(int)model.Now.DayOfWeek];
      if(NeedsCalendarRefresh(calendarRendered,renderedToday,model.Now.Date))RenderCalendar();
    }
    public static bool NeedsCalendarRefresh(bool rendered,DateTime previous,DateTime today){return !rendered||previous!=today;}
    private void RenderCalendar() {
      calendarRendered=true;renderedToday=model.Now.Date;
      month.Text = CalendarModel.FormatMonth(model.DisplayMonth);
      previous.Enabled = model.DisplayMonth.Year != 1 || model.DisplayMonth.Month != 1;
      next.Enabled = model.DisplayMonth.Year != 9999 || model.DisplayMonth.Month != 12;
      DateTime?[] cells = model.MonthCells();
      for (int i = 0; i < days.Length; i++) {
        Button b = days[i]; DateTime? cell = cells[i]; b.Visible = cell.HasValue;
        if (!cell.HasValue) continue;
        DateTime d = cell.Value; bool selected = d == model.SelectedDate, isToday = d == model.Now.Date;
        b.Text = d.Day.ToString(CultureInfo.InvariantCulture) + (isToday ? "·" : ""); b.AccessibleName = CalendarModel.FormatDate(d) + (isToday ? "，今天" : "") + (selected ? "，已选中" : "");
        b.ForeColor = selected ? PanelColor : isToday ? AccentColor : d.Month == model.DisplayMonth.Month ? TextColor : MutedColor;
        b.BackColor = selected ? AccentColor : Color.Transparent; b.FlatAppearance.BorderSize = isToday && !selected ? 1 : 0; b.FlatAppearance.BorderColor = AccentColor;
      }
    }
    protected override void Dispose(bool disposing) {
      if (disposing && !stopped) {
        stopped = true;
        if (timer != null) { timer.Stop(); timer.Tick -= TimerTick; timer.Dispose(); }
        base.Dispose(disposing);
        if (timeFont != null) { timeFont.Dispose(); timeFont = null; }
        if (titleFont != null) { titleFont.Dispose(); titleFont = null; }
        if (controlFont != null) { controlFont.Dispose(); controlFont = null; }
        return;
      }
      base.Dispose(disposing);
    }
  }
}
