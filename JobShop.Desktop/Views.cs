using System.Drawing.Drawing2D;
using System.ComponentModel;
using JobShop.Core;

namespace JobShop.Desktop;

internal sealed class GanttView : Control
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Instance? Instance { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Schedule? Schedule { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public int[]? VisibleIds { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public int Highlight { get; set; } = -1;
    private static readonly Color[] Palette = [Color.FromArgb(55,114,174), Color.FromArgb(216,119,47),
        Color.FromArgb(56,145,106),Color.FromArgb(153,95,166),Color.FromArgb(198,76,91),
        Color.FromArgb(72,149,160),Color.FromArgb(155,135,56),Color.FromArgb(97,117,148),
        Color.FromArgb(172,106,123),Color.FromArgb(107,140,67)];
    public GanttView() { DoubleBuffered = true; BackColor = Color.White; ResizeRedraw = true; }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        if (Instance is null || Schedule is null)
        {
            g.DrawString("Натисніть «Запустити MMAS», щоб побудувати розклад.",Font,Brushes.DimGray,24,30); return;
        }
        var operations = Instance.Operations;
        var left = 62f; var top = 35f; var right = 24f; var bottom = 55f;
        var plotW = Math.Max(1,ClientSize.Width-left-right);
        var rowH = Math.Max(10,(ClientSize.Height-top-bottom)/Instance.Machines);
        var scale = plotW/Math.Max(1,Schedule.Makespan);
        using var gridPen = new Pen(Color.FromArgb(225,230,236));
        using var small = new Font("Segoe UI",9);
        var ticks=Math.Min(10,Math.Max(1,Schedule.Makespan));
        for(var t=0;t<=ticks;t++)
        {
            var x=left+plotW*t/ticks;
            g.DrawLine(gridPen,x,top,x,top+rowH*Instance.Machines);
            g.DrawString((Schedule.Makespan*t/ticks).ToString(),small,Brushes.DimGray,x-8,top+rowH*Instance.Machines+5);
        }
        for(var m=0;m<Instance.Machines;m++)
        {
            var y=top+m*rowH;
            g.DrawString($"M{m+1}",Font,Brushes.Black,12,y+Math.Max(0,rowH/2-8));
            g.DrawLine(gridPen,left,y+rowH,left+plotW,y+rowH);
        }
        foreach(var s in Schedule.Slots)
        {
            if (VisibleIds is not null && !VisibleIds.Contains(s.OperationId))continue;
            var o=operations[s.OperationId];
            var rect=new RectangleF(left+s.Start*scale,top+o.Machine*rowH+4,
                Math.Max(1,(s.End-s.Start)*scale),Math.Max(3,rowH-8));
            using var brush=new SolidBrush(Palette[o.Job%Palette.Length]);
            g.FillRectangle(brush,rect);
            using var border=new Pen(s.OperationId==Highlight?Color.Black:Color.White,s.OperationId==Highlight?3:1);
            g.DrawRectangle(border,rect.X,rect.Y,rect.Width,rect.Height);
            if(rect.Width>24 && rect.Height>14)
                TextRenderer.DrawText(g,$"J{o.Job+1}",small,Rectangle.Round(rect),Color.White,
                    TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
        }
        g.DrawString($"Діаграма Ганта   •   {Instance.Name}   •   Cmax = {Schedule.Makespan}",Font,Brushes.Black,12,8);
        g.DrawString("Час, умовні одиниці",small,Brushes.DimGray,Math.Max(left,ClientSize.Width-180),ClientSize.Height-21);
    }
}

internal sealed class ConvergenceView : Control
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public IterationFrame[] Frames { get; set; }=[];
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public int SelectedIteration { get; set; }
    public ConvergenceView(){DoubleBuffered=true;BackColor=Color.White;ResizeRedraw=true;}
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if(Frames.Length==0)return;
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        var left=64f;var top=29f;var w=Math.Max(1,Width-left-30);var h=Math.Max(1,Height-top-32);
        var min=Frames.Min(f=>f.BestSoFar);var max=Math.Max(min+1,(int)Math.Ceiling(Frames.Max(f=>f.Mean)));
        float X(int i)=>left+w*i/Math.Max(1,Frames.Length-1);
        float Y(double v)=>top+h-(float)(v-min)/(max-min)*h;
        using var grid=new Pen(Color.FromArgb(225,230,236));
        using var small=new Font("Segoe UI",9);
        for(var i=0;i<=4;i++)
        {
            var value=min+(max-min)*i/4.0;
            g.DrawLine(grid,left,Y(value),left+w,Y(value));
            g.DrawString(value.ToString(max-min<4?"F1":"F0"),small,Brushes.DimGray,7,Y(value)-7);
        }
        using var avg=new Pen(Color.FromArgb(178,186,195),1.5f);
        using var best=new Pen(Color.FromArgb(31,102,176),2.8f);
        if(Frames.Length>1)
        {
            g.DrawLines(avg,Frames.Select((f,i)=>new PointF(X(i),Y(f.Mean))).ToArray());
            g.DrawLines(best,Frames.Select((f,i)=>new PointF(X(i),Y(f.BestSoFar))).ToArray());
        }
        if(SelectedIteration>0)
        {
            using var cursor=new Pen(Color.FromArgb(206,122,42)){DashStyle=DashStyle.Dash};
            g.DrawLine(cursor,X(Math.Clamp(SelectedIteration-1,0,Frames.Length-1)),top,
                X(Math.Clamp(SelectedIteration-1,0,Frames.Length-1)),top+h);
        }
        g.DrawString("Збіжність   — синя: найкраще   — сіра: середнє в колонії",small,Brushes.Black,12,5);
        g.DrawString("1",small,Brushes.DimGray,left,top+h+5);
        g.DrawString($"Ітерація {Frames.Length}",small,Brushes.DimGray,left+w-100,top+h+5);
    }
}
