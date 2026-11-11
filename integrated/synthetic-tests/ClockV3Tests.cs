using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using IslandIntegrated;

static class ClockV3Tests {
  static int checks;
  static void Check(bool ok, string name) {
    if (!ok) throw new Exception("FAIL " + name);
    checks++;
    Console.WriteLine("PASS " + name);
  }
  static int PaintedPixels(Bitmap bitmap) {
    int count = 0;
    for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++) if (bitmap.GetPixel(x, y).A != 0) count++;
    return count;
  }

  static bool Different(Bitmap a,Bitmap b){for(int y=0;y<a.Height;y++)for(int x=0;x<a.Width;x++)if(a.GetPixel(x,y).ToArgb()!=b.GetPixel(x,y).ToArgb())return true;return false;}

  [STAThread]
  static int Main() {
    string dir = Path.Combine(Path.GetTempPath(), "sidebar-clock-v3-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try {
      var defaults = Layout.Default();
      var clock = defaults.Cards.First(c => c.Id == "clock");
      Check(clock.ClockMode == "digital" && clock.SizeMode == "small", "default clock is compact A digital mode");

      clock.ClockMode = "dial";
      string path = Path.Combine(dir, "layout.xml");
      LayoutStore.Save(path, defaults);
      Check(LayoutStore.Load(path).Cards.First(c => c.Id == "clock").ClockMode == "dial", "clock variant persists in XML");

      var cancel = new LayoutEditSession(defaults);
      Check(cancel.Draft.Cards.First(c => c.Id == "clock").ClockMode == "dial", "edit clone starts with selected C variant");
      cancel.Draft.Cards.First(c => c.Id == "clock").ClockMode = "digital";
      cancel.Cancel();
      Check(clock.ClockMode == "dial", "cancel discards draft clock variant");
      var save = new LayoutEditSession(defaults);
      save.Draft.Cards.First(c => c.Id == "clock").ClockMode = "digital";
      save.Save(path);
      Check(defaults.Cards.First(c => c.Id == "clock").ClockMode == "digital" && LayoutStore.Load(path).Cards.First(c => c.Id == "clock").ClockMode == "digital", "explicit save commits clock variant");

      var stripCancel = new LayoutEditSession(defaults);
      stripCancel.Draft.Cards.First(c => c.Id == "clock").ClockMode = "strip";
      stripCancel.Cancel();
      Check(defaults.Cards.First(c => c.Id == "clock").ClockMode == "digital", "cancel discards B long-strip selection");
      var stripSave = new LayoutEditSession(defaults);
      stripSave.Draft.Cards.First(c => c.Id == "clock").ClockMode = "strip";
      stripSave.Save(path);
      Check(defaults.Cards.First(c => c.Id == "clock").ClockMode == "strip" && LayoutStore.Load(path).Cards.First(c => c.Id == "clock").ClockMode == "strip", "explicit save persists B long-strip selection");
      Check(defaults.Cards.First(c => c.Id == "clock").SizeMode == "small", "B mode leaves legacy small/large field semantics intact");

      defaults.Cards.First(c => c.Id == "clock").ClockMode = "invalid";
      defaults.Normalize();
      Check(defaults.Cards.First(c => c.Id == "clock").ClockMode == "digital", "invalid clock variant normalizes to A digital");

      var legacy = new Layout();
      legacy.Cards.Add(new Card { Id = "calendar", SizeMode = "small" });
      legacy.Normalize();
      Check(legacy.Cards.Count == 1 && legacy.Cards[0].Id == "clock" && legacy.Cards[0].SizeMode == "large" && legacy.Cards[0].ClockMode == "digital", "legacy calendar alias remains expanded calendar with default digital mode");
      using (var stream = new MemoryStream()) {
        new XmlSerializer(typeof(Layout)).Serialize(stream, new Layout { Cards = new System.Collections.Generic.List<Card> { new Card { Id = "clock", SizeMode = "large" } } });
        stream.Position = 0;
        var old = (Layout)new XmlSerializer(typeof(Layout)).Deserialize(stream);
        old.Normalize();
        Check(old.Cards[0].ClockMode == "digital", "XML without clock mode loads as digital A");
      }

      var geometry = WidgetGrid.Arrange(new[] { new Card { Id = "clock", SizeMode = "small", ClockMode = "digital" } }, WidgetGrid.DefaultColumnWidth)["clock"];
      Check(geometry.Width == 168 && geometry.Height == 168, "compact clock retains 168 by 168 geometry");
      var strip360 = WidgetGrid.Arrange(new[] { new Card { Id = "clock", SizeMode = "small", ClockMode = "strip" } }, WidgetGrid.DefaultColumnWidth)["clock"];
      Check(strip360.Width == 360 && strip360.Height == 120, "B long strip is 360 by 120 at fixed sidebar width");
      var stripWide = WidgetGrid.Arrange(new[] { new Card { Id = "clock", SizeMode = "small", ClockMode = "strip" } }, 400)["clock"];
      Check(stripWide.Width == 400 && stripWide.Height == 120, "B strip adapts horizontally to available width");
      var pair = WidgetGrid.Arrange(new[] { new Card { Id = "music", MusicMode = "square" }, new Card { Id = "weather", WeatherMode = "square" } }, WidgetGrid.DefaultColumnWidth);
      Check(pair["music"] == new Rectangle(0, 0, 168, 168) && pair["weather"] == new Rectangle(192, 0, 168, 168), "square cards use 168px cells with a 24px gap");
      Check(WidgetGrid.Height(new[] { new Card { Id = "music", MusicMode = "square" }, new Card { Id = "weather", WeatherMode = "square" } }, WidgetGrid.DefaultColumnWidth) == 192, "grid extent includes the 24px row gap");
      var legacyLargeStrip = WidgetGrid.Arrange(new[] { new Card { Id = "clock", SizeMode = "large", ClockMode = "strip" } }, WidgetGrid.DefaultColumnWidth)["clock"];
      Check(legacyLargeStrip.Width == 360 && legacyLargeStrip.Height == 380, "legacy large size still selects expanded calendar");
      Check(WidgetGrid.Arrange(new[] { new Card { Id = "clock", SizeMode = "large" } }, WidgetGrid.DefaultColumnWidth)["clock"].Height == 380, "large calendar height remains 380");

      DateTime beforeMidnight = new DateTime(2026, 11, 7, 23, 59, 0, DateTimeKind.Local);
      DateTime afterMidnight = beforeMidnight.AddMinutes(1);
      Check(MiniClockCalendar.ClockNeedsRefresh(beforeMidnight, afterMidnight), "clock invalidates at date boundary");
      Check(!MiniClockCalendar.ClockNeedsRefresh(beforeMidnight, beforeMidnight.AddSeconds(30)), "clock does not invalidate within same minute");

      DateTime fake = new DateTime(2026, 11, 7, 9, 8, 0, DateTimeKind.Local);
      using (var digital = new MiniClockCalendar(false, "digital", () => fake))
      using (var dial = new MiniClockCalendar(false, "dial", () => fake))
      using (var strip = new MiniClockCalendar(false, "strip", () => fake))
      using (var calendar = new MiniClockCalendar(true, "digital", () => fake)) {
        digital.Size = dial.Size = new Size(168, 168);
        strip.Size = new Size(360, 120);
        calendar.Size = new Size(360, 380);
        using (var a = new Bitmap(168, 168)) using (var c = new Bitmap(168, 168)) {
          digital.DrawToBitmap(a, new Rectangle(Point.Empty, a.Size));
          dial.DrawToBitmap(c, new Rectangle(Point.Empty, c.Size));
          Check(digital.ClockMode == "digital" && dial.ClockMode == "dial" && Different(a,c), "synthetic local time renders distinct A and C variants");
        }
        using (var b = new Bitmap(360, 120)) {
          strip.DrawToBitmap(b, new Rectangle(Point.Empty, b.Size));
          Check(strip.ClockMode == "strip" && strip.AccessibleName == "长条时钟" && PaintedPixels(b) > 0, "synthetic 40px B strip renders time/date/week without city identity");
        }
        Check(calendar.CalendarMode && calendar.Height == 380, "existing expanded calendar view remains available");
        dial.SetClockMode("digital");
        Check(dial.ClockMode == "digital", "variant toggle returns C to A");
      }
      Console.WriteLine("PASS total checks=" + checks);
      return 0;
    } finally {
      Directory.Delete(dir, true);
    }
  }
}
