using System;
using System.IO;
using IslandIntegrated;

static class DesignV3Tests {
  static int checks;
  static void Check(bool ok, string name) {
    if (!ok) throw new Exception("FAIL " + name);
    checks++;
    Console.WriteLine("PASS " + name);
  }

  static void Main() {
    Check(Palette.ValidTheme("bogus") == "tide", "invalid theme falls back to tide");
    Check(Palette.ValidTheme(null) == "tide", "null theme falls back to tide");
    Check(Palette.ValidTheme("pine") == "pine" && Palette.ValidTheme("iris") == "iris" && Palette.ValidTheme("amber") == "amber", "all non-default themes remain valid");

    string dir = Path.Combine(Path.GetTempPath(), "sidebar-design-v3-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    string path = Path.Combine(dir, "layout.xml");
    try {
      var layout = Layout.Default();
      layout.Theme = "iris";
      LayoutStore.Save(path, layout);
      Check(LayoutStore.Load(path).Theme == "iris", "theme survives layout XML round trip");

      var clone = LayoutEditSession.Clone(layout);
      Check(clone.Theme == "iris", "edit clone preserves theme");
      clone.Theme = "amber";
      var edit = new LayoutEditSession(layout);
      edit.Draft.Theme = clone.Theme;
      edit.Save(path);
      Check(layout.Theme == "amber" && LayoutStore.Load(path).Theme == "amber", "edit save updates model and persisted theme");

      var invalid = new Layout { Theme = "unknown" };
      invalid.Normalize();
      Check(invalid.Theme == "tide", "layout normalization repairs invalid theme");
    } finally {
      Directory.Delete(dir, true);
    }

    Check(Math.Abs(StatusSignal.Pulse("warning", 0, false) - .75) < 1e-9, "warning pulse starts at midpoint");
    Check(Math.Abs(StatusSignal.Pulse("warning", 1, false) - 1.0) < 1e-9, "warning pulse reaches maximum at quarter cycle");
    Check(Math.Abs(StatusSignal.Pulse("warning", 4, false) - .75) < 1e-9, "warning pulse repeats after four seconds");
    Check(Math.Abs(StatusSignal.Pulse("error", 1.5, false) - 1.0) < 1e-9, "error pulse reaches maximum at quarter of six-second cycle");
    Check(StatusSignal.Pulse("warning", 1, true) == 1 && StatusSignal.Pulse("error", 1.5, true) == 1, "reduced motion fixes warning and error pulse");
    Check(StatusSignal.Pulse("normal", 1, false) == 1 && StatusSignal.Pulse("unknown", 2, false) == 1, "normal and unknown signals do not pulse");

    Check(OpenMeteoWeatherProvider.Kind(0) == WeatherKind.Clear, "WMO clear mapping");
    Check(OpenMeteoWeatherProvider.Kind(2) == WeatherKind.Cloud, "WMO partly cloudy mapping");
    Check(OpenMeteoWeatherProvider.Kind(3) == WeatherKind.Overcast, "WMO overcast mapping");
    Check(OpenMeteoWeatherProvider.Kind(61) == WeatherKind.Rain, "WMO rain mapping");
    Check(OpenMeteoWeatherProvider.Kind(71) == WeatherKind.Snow, "WMO snow mapping");
    Check(OpenMeteoWeatherProvider.Kind(45) == WeatherKind.Fog, "WMO fog mapping");
    Check(OpenMeteoWeatherProvider.Kind(999) == WeatherKind.Unknown && OpenMeteoWeatherProvider.Kind(null) == WeatherKind.Unknown, "unknown WMO mapping");
    Console.WriteLine("PASS total checks=" + checks);
  }
}
