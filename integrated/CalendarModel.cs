using System;
using System.Globalization;

namespace SidebarClockCalendar {
  // Dates from the clock are used as supplied; no timezone conversion is performed.
  public sealed class CalendarModel {
    private readonly Func<DateTime> clock;
    public DateTime Now { get; private set; }
    public DateTime DisplayMonth { get; private set; }
    public DateTime SelectedDate { get; private set; }
    public CalendarModel(Func<DateTime> localDateTime) {
      if (localDateTime == null) throw new ArgumentNullException("localDateTime");
      clock = localDateTime; Now = clock(); SelectedDate = Now.Date;
      DisplayMonth = new DateTime(Now.Year, Now.Month, 1);
    }
    public static string FormatTime(DateTime value) { return value.ToString("HH:mm:ss", CultureInfo.InvariantCulture); }
    public static string FormatDate(DateTime value) { return value.ToString("yyyy年M月d日", CultureInfo.InvariantCulture); }
    public static string FormatMonth(DateTime value) { return value.ToString("yyyy年M月", CultureInfo.InvariantCulture); }
    public void RefreshClock() { Now = clock(); }
    public bool MoveMonth(int delta) {
      long month = (long)(DisplayMonth.Year - 1) * 12 + DisplayMonth.Month - 1 + delta;
      if (month < 0 || month >= 9999L * 12) return false;
      DisplayMonth = new DateTime((int)(month / 12) + 1, (int)(month % 12) + 1, 1);
      return true;
    }
    public void Select(DateTime date) {
      SelectedDate = date.Date;
      DisplayMonth = new DateTime(date.Year, date.Month, 1);
    }
    public void GoToToday() { RefreshClock(); Select(Now); }
    // Monday-first, six rows; null represents a position outside DateTime's range.
    public DateTime?[] MonthCells() {
      DateTime?[] cells = new DateTime?[42];
      int offset = ((int)DisplayMonth.DayOfWeek + 6) % 7;
      long firstDay = DisplayMonth.Ticks / TimeSpan.TicksPerDay - offset;
      long maxDay = DateTime.MaxValue.Ticks / TimeSpan.TicksPerDay;
      for (int i = 0; i < cells.Length; i++) {
        long day = firstDay + i;
        if (day >= 0 && day <= maxDay) cells[i] = new DateTime(day * TimeSpan.TicksPerDay);
      }
      return cells;
    }
  }
}
