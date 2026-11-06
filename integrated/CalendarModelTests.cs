using System;
using System.Globalization;
using System.Threading;
using SidebarClockCalendar;

static class CalendarModelTests {
  static int assertions;
  static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
  static void Main() {
    DateTime fake = new DateTime(2024, 2, 29, 23, 59, 58, DateTimeKind.Unspecified);
    var m = new CalendarModel(delegate { return fake; });
    Check(m.Now == fake && m.SelectedDate == fake.Date, "injected clock initializes");
    var cells = m.MonthCells();
    Check(cells.Length == 42 && cells[0].Value == new DateTime(2024, 1, 29), "Monday first");
    Check(cells[31].Value == new DateTime(2024, 2, 29), "leap day");
    Check(m.MoveMonth(10) && m.DisplayMonth == new DateTime(2024, 12, 1), "move to December");
    Check(m.MoveMonth(1) && m.DisplayMonth == new DateTime(2025, 1, 1), "cross year forward");
    Check(m.MoveMonth(-1) && m.DisplayMonth == new DateTime(2024, 12, 1), "cross year backward");
    m.Select(new DateTime(2021, 8, 31)); cells = m.MonthCells();
    Check(cells[0].Value == new DateTime(2021, 7, 26) && cells[41].Value == new DateTime(2021, 9, 5), "six-week month");
    fake = new DateTime(2024, 3, 10, 2, 30, 0, DateTimeKind.Unspecified); m.RefreshClock();
    Check(m.Now == fake && m.Now.Kind == DateTimeKind.Unspecified && m.DisplayMonth.Month == 8, "clock refresh preserves supplied wall time and browsing");
    m.GoToToday(); Check(m.DisplayMonth == new DateTime(2024, 3, 1) && m.SelectedDate == fake.Date, "today follows fake clock");
    m.Select(DateTime.MinValue); Check(!m.MoveMonth(-1) && m.MonthCells()[0].Value == DateTime.MinValue, "minimum month bound");
    m.Select(DateTime.MaxValue); Check(!m.MoveMonth(1), "maximum month bound");
    cells = m.MonthCells(); Check(!cells[41].HasValue, "maximum grid out of range");
    bool threw = false; try { new CalendarModel(null); } catch (ArgumentNullException) { threw = true; }
    Check(threw, "null clock rejected");
    CultureInfo original = Thread.CurrentThread.CurrentCulture;
    try {
      foreach (string culture in new string[] { "ar-SA", "th-TH" }) {
        Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
        Check(CalendarModel.FormatDate(DateTime.MinValue) == "0001年1月1日" && CalendarModel.FormatMonth(DateTime.MaxValue) == "9999年12月" && CalendarModel.FormatTime(fake) == "02:30:00", "Gregorian display under " + culture);
      }
    } finally { Thread.CurrentThread.CurrentCulture = original; }
    Console.WriteLine("PASS: " + assertions + " assertions");
  }
}
