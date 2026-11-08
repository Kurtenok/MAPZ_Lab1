using System.Text;
using System.Text.Json;
using JobShop.Core;

namespace JobShop.Desktop;

public sealed class MainForm : Form
{
    private readonly string root;
    private readonly List<Instance> instances;
    private readonly ComboBox instanceBox=new(){Width=175,DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly NumericUpDown ants=Number(1,500,30), iterations=Number(1,5000,200),
        alpha=Number(0,10,1,1), beta=Number(0,10,2,1), rho=Number(0.001m,0.99m,0.05m,3),
        ratio=Number(0.001m,1,0.05m,3), seed=Number(0,int.MaxValue,42), restart=Number(1,5000,40);
    private readonly Button start=new(){Text="Запустити MMAS",AutoSize=true};
    private readonly Button cancel=new(){Text="Зупинити",AutoSize=true,Enabled=false};
    private readonly Button play=new(){Text="▶ Ітерації",AutoSize=true,Enabled=false};
    private readonly Button stepPlay=new(){Text="▶ Кроки",AutoSize=true,Enabled=false};
    private readonly Label status=new(){AutoSize=false,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft};
    private readonly Label frameInfo=new(){AutoSize=true};
    private readonly Label stepInfo=new(){Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft};
    private readonly GanttView gantt=new(){Dock=DockStyle.Fill};
    private readonly GanttView stepGantt=new(){Dock=DockStyle.Fill};
    private readonly ConvergenceView chart=new(){Dock=DockStyle.Fill};
    private readonly TrackBar iterationSlider=new(){Minimum=1,Maximum=200,Value=1,TickStyle=TickStyle.None,Dock=DockStyle.Fill};
    private readonly TrackBar stepSlider=new(){Minimum=0,Maximum=1,Value=0,TickStyle=TickStyle.None,Dock=DockStyle.Fill};
    private readonly DataGridView candidates=new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,
        RowHeadersVisible=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BackgroundColor=Color.White,
        SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false};
    private readonly TextBox inputText=new(){Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Both,
        Font=new Font("Consolas",11),WordWrap=false};
    private readonly TabControl tabs=new(){Dock=DockStyle.Fill};
    private readonly System.Windows.Forms.Timer animation=new(){Interval=130};
    private readonly System.Windows.Forms.Timer stepAnimation=new(){Interval=500};
    private CancellationTokenSource? cts;
    private RunResult? result;
    private readonly List<IterationFrame> frames=[];
    private bool running;

    public MainForm()
    {
        root=FindRoot();
        Text="Лабораторна 1 • Варіант 7 • Job Shop та Max–Min Ant System";
        ClientSize=new Size(1180,810);MinimumSize=new Size(1050,740);StartPosition=FormStartPosition.CenterScreen;
        Font=new Font("Segoe UI",10);BackColor=Color.FromArgb(242,245,248);
        instances=Examples.Small().Concat(InstanceReader.ReadOrLibrary(Path.Combine(root,"data","jobshop1.txt"))).ToList();
        var layout=new TableLayoutPanel(){Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(12)};
        layout.RowStyles.Add(new(SizeType.Absolute,42));layout.RowStyles.Add(new(SizeType.Absolute,76));
        layout.RowStyles.Add(new(SizeType.Absolute,39));layout.RowStyles.Add(new(SizeType.Absolute,45));
        layout.RowStyles.Add(new(SizeType.Percent,100));layout.RowStyles.Add(new(SizeType.Absolute,42));
        Controls.Add(layout);
        var top=new FlowLayoutPanel(){Dock=DockStyle.Fill,WrapContents=false};
        top.Controls.Add(Label("Приклад"));top.Controls.Add(instanceBox);
        var load=new Button(){Text="Відкрити JSON",AutoSize=true};top.Controls.Add(load);
        var save=new Button(){Text="Зберегти розклад",AutoSize=true};top.Controls.Add(save);
        var screenshot=new Button(){Text="Знімок вікна",AutoSize=true};top.Controls.Add(screenshot);
        layout.Controls.Add(top,0,0);
        var settings=new FlowLayoutPanel(){Dock=DockStyle.Fill,WrapContents=true,AutoScroll=true};
        foreach(var (label,control) in new (string,NumericUpDown)[]{("Мурахи",ants),("Ітерації",iterations),("α",alpha),
            ("β",beta),("ρ",rho),("τmin / τmax",ratio),("Seed",seed),("Без покращень до рестарту",restart)})
        {
            var group=new FlowLayoutPanel(){AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,
                WrapContents=false,Margin=new Padding(0)};
            group.Controls.Add(Label(label));group.Controls.Add(control);settings.Controls.Add(group);
        }
        layout.Controls.Add(settings,0,1);
        var actions=new FlowLayoutPanel(){Dock=DockStyle.Fill,WrapContents=false};
        actions.Controls.Add(start);actions.Controls.Add(cancel);actions.Controls.Add(play);actions.Controls.Add(stepPlay);
        var restore=new Button(){Text="Параметри з досліду",AutoSize=true};actions.Controls.Add(restore);
        layout.Controls.Add(actions,0,2);
        status.Text="Виберіть приклад. Розклад перевіряється після побудови кожної мурахи.";
        layout.Controls.Add(status,0,3);layout.Controls.Add(tabs,0,4);
        var searchPage=new TabPage("Пошук та результат");
        var searchLayout=new TableLayoutPanel(){Dock=DockStyle.Fill,RowCount=2,ColumnCount=1};
        searchLayout.RowStyles.Add(new(SizeType.Percent,62));searchLayout.RowStyles.Add(new(SizeType.Percent,38));
        searchLayout.Controls.Add(gantt,0,0);searchLayout.Controls.Add(chart,0,1);searchPage.Controls.Add(searchLayout);
        tabs.TabPages.Add(searchPage);
        var stepsPage=new TabPage("Побудова першої мурахи");
        var stepsLayout=new TableLayoutPanel(){Dock=DockStyle.Fill,RowCount=4,ColumnCount=1};
        stepsLayout.RowStyles.Add(new(SizeType.Percent,60));stepsLayout.RowStyles.Add(new(SizeType.Absolute,42));
        stepsLayout.RowStyles.Add(new(SizeType.Absolute,38));stepsLayout.RowStyles.Add(new(SizeType.Percent,40));
        stepsLayout.Controls.Add(stepGantt,0,0);stepsLayout.Controls.Add(stepInfo,0,1);stepsLayout.Controls.Add(stepSlider,0,2);
        stepsLayout.Controls.Add(candidates,0,3);stepsPage.Controls.Add(stepsLayout);tabs.TabPages.Add(stepsPage);
        candidates.Columns.Add("op","Операція");candidates.Columns.Add("start","Готовність");
        candidates.Columns.Add("tau","Феромон τ");candidates.Columns.Add("eta","Евристика η");candidates.Columns.Add("p","Імовірність");
        var dataPage=new TabPage("Вхідні дані");dataPage.Controls.Add(inputText);tabs.TabPages.Add(dataPage);
        var footer=new TableLayoutPanel(){Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};
        footer.ColumnStyles.Add(new(SizeType.Percent,100));footer.ColumnStyles.Add(new(SizeType.Absolute,400));
        footer.Controls.Add(iterationSlider,0,0);footer.Controls.Add(frameInfo,1,0);layout.Controls.Add(footer,0,5);
        foreach(var item in instances)instanceBox.Items.Add(item.Name);
        instanceBox.DrawMode=DrawMode.OwnerDrawFixed;
        instanceBox.DrawItem+=(_,e)=>
        {
            e.DrawBackground();
            if(e.Index>=0)TextRenderer.DrawText(e.Graphics,instanceBox.Items[e.Index]?.ToString() ?? "",Font,e.Bounds,e.ForeColor,
                TextFormatFlags.Left|TextFormatFlags.VerticalCenter);
            e.DrawFocusRectangle();
        };
        instanceBox.SelectedIndexChanged+=(_,_)=>SelectInstance();instanceBox.SelectedIndex=0;
        start.Click+=async(_,_)=>await SolveAsync();cancel.Click+=(_,_)=>cts?.Cancel();
        restore.Click+=(_,_)=>LoadSelectedParameters();load.Click+=(_,_)=>LoadInput();
        save.Click+=(_,_)=>SaveResult();screenshot.Click+=(_,_)=>SaveScreenshot();
        iterationSlider.ValueChanged+=(_,_)=>ShowFrame();stepSlider.ValueChanged+=(_,_)=>ShowStep();
        play.Click+=(_,_)=>{if(running)return;animation.Enabled=!animation.Enabled;play.Text=animation.Enabled?"Ⅱ Ітерації":"▶ Ітерації";};
        stepPlay.Click+=(_,_)=>{tabs.SelectedIndex=1;stepAnimation.Enabled=!stepAnimation.Enabled;};
        animation.Tick+=(_,_)=>{if(iterationSlider.Value==iterationSlider.Maximum)iterationSlider.Value=1;else iterationSlider.Value++;};
        stepAnimation.Tick+=(_,_)=>{if(stepSlider.Value==stepSlider.Maximum)stepSlider.Value=0;else stepSlider.Value++;};
        LoadSelectedParameters();
        FormClosing+=(_,_)=>cts?.Cancel();
    }

    private Instance Current=>instances[instanceBox.SelectedIndex];
    private static Label Label(string text)=>new(){Text=text,AutoSize=true,Margin=new Padding(4,8,4,3)};
    private static NumericUpDown Number(decimal min,decimal max,decimal value,int decimals=0)=>new()
        {Minimum=min,Maximum=max,Value=value,DecimalPlaces=decimals,Increment=decimals==3?0.01m:1,Width=decimals==0?74:66,Margin=new Padding(0,4,8,3)};

    private void SelectInstance()
    {
        if(running)return;
        animation.Stop();stepAnimation.Stop();frames.Clear();result=null;play.Enabled=stepPlay.Enabled=false;
        iterationSlider.Value=1;stepSlider.Value=0;frameInfo.Text="";stepInfo.Text="";
        status.Text=$"Обрано {Current.Name}: {Current.Jobs.Length} робіт, {Current.Machines} верстатів. Натисніть «Запустити MMAS».";
        gantt.Instance=stepGantt.Instance=Current;gantt.Schedule=stepGantt.Schedule=null;
        candidates.Rows.Clear();chart.Frames=[];gantt.Invalidate();stepGantt.Invalidate();chart.Invalidate();
        inputText.Text=$"{Current.Name}\r\nРобіт: {Current.Jobs.Length}; верстатів: {Current.Machines}; операцій: {Current.Count}\r\nНижня межа LB = {Current.LowerBound}\r\nМашини у JSON/OR-Library нумеруються з 0, у вікні — з 1.\r\n\r\n"+
            string.Join("\r\n",Current.Jobs.Select((job,j)=>$"J{j+1}: "+string.Join(" → ",job.Select(o=>$"(M{o.Machine+1}, {o.Duration})"))));
    }

    private AcoParameters Parameters()=>new((int)ants.Value,(int)iterations.Value,(double)alpha.Value,(double)beta.Value,
        (double)rho.Value,(double)ratio.Value,(int)restart.Value,(int)seed.Value);

    private async Task SolveAsync()
    {
        if(running)return;
        animation.Stop();stepAnimation.Stop();frames.Clear();result=null;
        var instance=Current;var parameters=Parameters();parameters.Validate();
        cts=new CancellationTokenSource();running=true;start.Enabled=false;cancel.Enabled=true;instanceBox.Enabled=false;
        play.Enabled=stepPlay.Enabled=false;iterationSlider.Maximum=parameters.Iterations;
        IProgress<IterationFrame> progress=new Progress<IterationFrame>(f=>
        {
            if(IsDisposed)return;
            frames.Add(f);iterationSlider.Value=f.Iteration;
            status.Text=$"Ітерація {f.Iteration}/{parameters.Iterations}    Cmax = {f.BestSoFar}    LB = {instance.LowerBound}    "+
                $"τ ∈ [{f.TauMin:G4}; {f.TauMax:G4}]"+(f.Restarted?"    Феромон переініціалізовано":"");
            chart.Frames=frames.ToArray();ShowFrame();
        });
        try
        {
            result=await Task.Run(()=>new MmasSolver(instance,parameters).Run(progress.Report,cts.Token,captureTrace:true));
            frames.Clear();frames.AddRange(result.History);chart.Frames=result.History;
            iterationSlider.Maximum=frames.Count;iterationSlider.Value=frames.Count;ShowFrame();
            status.Text=$"Cmax = {result.Best.Makespan}    Жадібний SPT = {result.Greedy.Makespan}    LB = {instance.LowerBound}    "+
                $"Час = {result.ElapsedMs:F1} мс    Розклад коректний    Перезапусків: {result.Restarts}";
            play.Enabled=stepPlay.Enabled=true;
        }
        catch(OperationCanceledException){status.Text="Обчислення зупинено. Можна запустити новий пошук.";}
        catch(Exception ex){status.Text="Помилка: "+ex.Message;}
        finally
        {
            running=false;start.Enabled=true;cancel.Enabled=false;instanceBox.Enabled=true;
            cts.Dispose();cts=null;
        }
    }

    private void ShowFrame()
    {
        if(frames.Count==0)return;
        var f=frames[Math.Min(iterationSlider.Value-1,frames.Count-1)];
        gantt.Instance=Current;gantt.Schedule=f.BestSchedule;gantt.VisibleIds=null;gantt.Invalidate();
        chart.SelectedIteration=f.Iteration;chart.Invalidate();
        frameInfo.Text=$"Ітерація {f.Iteration}    Найкраще {f.BestSoFar}    Середнє {f.Mean:F1}";
        stepSlider.Maximum=f.FirstAntTrace?.Length ?? 0;
        if(stepSlider.Value>stepSlider.Maximum)stepSlider.Value=stepSlider.Maximum;
        ShowStep();
    }

    private void ShowStep()
    {
        if(frames.Count==0)return;
        var f=frames[Math.Min(iterationSlider.Value-1,frames.Count-1)];var trace=f.FirstAntTrace;
        if(trace is null || trace.Length==0)return;
        var count=Math.Min(stepSlider.Value,trace.Length);
        var partial=trace.Take(count).Select(s=>s.Added).ToArray();
        stepGantt.Instance=Current;
        stepGantt.Schedule=new Schedule(trace.Select(s=>s.Added).ToArray(),trace.Max(s=>s.Added.End),trace.Select(s=>s.ChosenId).ToArray());
        stepGantt.VisibleIds=partial.Select(s=>s.OperationId).ToArray();stepGantt.Highlight=count>0?trace[count-1].ChosenId:-1;
        stepGantt.Invalidate();candidates.Rows.Clear();
        var index=Math.Min(count,trace.Length-1);var step=trace[index];
        var operations=Current.Operations;
        foreach(var c in step.Candidates)
        {
            var row=candidates.Rows.Add(operations[c.OperationId].Label,c.EarliestStart,c.Pheromone.ToString("G6"),
                c.Heuristic.ToString("G6"),$"{100*c.Probability:F2}%");
            if(c.OperationId==step.ChosenId)candidates.Rows[row].DefaultCellStyle.BackColor=Color.FromArgb(221,236,250);
        }
        stepInfo.Text=count==trace.Length?$"Перша мураха: усі {count} операцій розміщено; Cmax = {trace.Max(s=>s.Added.End)}.":
            $"Розміщено {count}/{trace.Length}. Наступний вибір: {operations[step.ChosenId].Label}; опорна операція: {operations[step.PivotId].Label}.";
    }

    private void LoadSelectedParameters()
    {
        if(running)return;
        var path=Path.Combine(root,"results","selected_parameters.json");
        if(!File.Exists(path))return;
        using var json=JsonDocument.Parse(File.ReadAllText(path));
        var p=json.RootElement.GetProperty("parameters").Deserialize<AcoParameters>(Json.Options)!;
        ants.Value=p.Ants;iterations.Value=p.Iterations;alpha.Value=(decimal)p.Alpha;beta.Value=(decimal)p.Beta;
        rho.Value=(decimal)p.Rho;ratio.Value=(decimal)p.MinRatio;restart.Value=p.RestartAfter;seed.Value=p.Seed;
    }

    private void LoadInput()
    {
        if(running)return;
        using var dialog=new OpenFileDialog(){Filter="Job Shop JSON|*.json"};
        if(dialog.ShowDialog()!=DialogResult.OK)return;
        try
        {
            var instance=InstanceReader.ReadJson(dialog.FileName);
            instances.Add(instance);instanceBox.Items.Add(instance.Name);instanceBox.SelectedIndex=instances.Count-1;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException or OverflowException)
        {MessageBox.Show(ex.Message,"Некоректні дані",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
    }

    private void SaveResult()
    {
        if(result is null || running){MessageBox.Show("Спочатку завершіть пошук.");return;}
        using var dialog=new SaveFileDialog(){Filter="JSON|*.json",FileName=Current.Name+"_result.json"};
        if(dialog.ShowDialog()!=DialogResult.OK)return;
        try
        {
            Json.Save(dialog.FileName,new {instance=Current,result});
            var csv=Path.ChangeExtension(dialog.FileName,"csv");
            File.WriteAllLines(csv,new[]{"operation;machine;start;end"}.Concat(result.Best.Slots.Select(s=>
            {var o=Current.Operations[s.OperationId];return $"{o.Label};{o.Machine+1};{s.Start};{s.End}";})),Encoding.UTF8);
            status.Text="Збережено JSON з історією пошуку та CSV з розкладом.";
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){MessageBox.Show(ex.Message,"Помилка збереження");}
    }

    private void SaveScreenshot()
    {
        using var dialog=new SaveFileDialog(){Filter="PNG|*.png",FileName="jobshop.png"};
        if(dialog.ShowDialog()!=DialogResult.OK)return;
        try{CaptureWindow(dialog.FileName);}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {MessageBox.Show(ex.Message,"Помилка збереження знімка");}
    }
    private void CaptureWindow(string path)
    {
        Refresh();using var image=new Bitmap(Width,Height);DrawToBitmap(image,new Rectangle(0,0,Width,Height));
        image.Save(path,System.Drawing.Imaging.ImageFormat.Png);
    }
    private static string FindRoot()
    {
        foreach(var start in new[]{Environment.CurrentDirectory,AppContext.BaseDirectory})
        for(var dir=new DirectoryInfo(start);dir is not null;dir=dir.Parent)
            if(File.Exists(Path.Combine(dir.FullName,"data","jobshop1.txt")))return dir.FullName;
        throw new FileNotFoundException("Папку data/jobshop1.txt не знайдено.");
    }
}
