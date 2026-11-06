using System;
namespace IslandIntegrated {
 public sealed class StatusPresencePolicy {
  IntPtr lastForeground;long nextAllowed;bool pending=true;
  public void Request(){pending=true;}
  public bool Step(long now,IntPtr foreground,bool visible,bool fullscreen,bool ownedPopup,bool ownForeground){if(!visible||fullscreen){lastForeground=foreground;pending=true;return false;}if(foreground!=lastForeground){lastForeground=foreground;pending=true;}if(ownedPopup||ownForeground||!pending||now<nextAllowed)return false;pending=false;nextAllowed=now+120;return true;}
 }
}
