using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using IslandIntegrated;

class MenuTests {
  [STAThread]
  static int Main() {
    using(var menu=new ThemePicker()) {
      if(menu.Size!=new Size(216,240)||menu.Items.Count!=7)throw new Exception("menu geometry/items");
      menu.CreateControl();
      var themes=menu.Items.OfType<ToolStripMenuItem>().Where(item=>(item.Tag as string)!="__random__").ToArray();
      var random=menu.Items.OfType<ToolStripMenuItem>().Single(item=>(item.Tag as string)=="__random__");
      if(themes.Length!=4)throw new Exception("four manual themes");
      string selected=null;int requests=0;
      menu.Selected+=(s,e)=>selected=e.Theme;
      menu.RandomRequested+=(s,e)=>{requests++;menu.RandomPending=true;};
      menu.CurrentTheme="tide";
      random.PerformClick();
      if(requests!=1||selected!=null||menu.CurrentTheme!="tide")throw new Exception("random request must defer without changing the current theme");
      if(!menu.RandomPending||!random.Checked||random.Text.IndexOf("已安排",StringComparison.Ordinal)<0)throw new Exception("scheduled state");
      menu.Selected+=(s,e)=>menu.RandomPending=false;
      var manual=themes.Single(item=>(item.Tag as string)=="pine");
      manual.PerformClick();
      if(selected!="pine"||menu.CurrentTheme!="pine"||menu.RandomPending)throw new Exception("manual selection must apply and cancel scheduled request");
      if(themes.Count(item=>item.Checked)!=1)throw new Exception("exactly one selected theme");
      for(int i=0;i<menu.Items.Count;i++){
        Rectangle b=menu.Items[i].Bounds;
        if(b.Left<0||b.Top<0||b.Right>menu.Width||b.Bottom>menu.Height)throw new Exception("item outside menu bounds");
        for(int j=0;j<i;j++)if(!b.IsEmpty&&b.IntersectsWith(menu.Items[j].Bounds))throw new Exception("menu item overlap");
      }
    }
    Console.WriteLine("PASS deferred request, manual apply/cancel, selection and menu bounds");
    return 0;
  }
}
