using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using JobShop.Core;

Console.OutputEncoding = Encoding.UTF8;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
try
{
    var root = FindRoot();
    var libraryPath = Path.Combine(root, "data", "jobshop1.txt");
    var examples = Examples.Small().Concat(InstanceReader.ReadOrLibrary(libraryPath)).ToList();
    if (args.Length == 0 || args[0] == "--help")
    {
        Console.WriteLine("Job Shop / MMAS\n--self-test\n--experiments [папка результатів]\n--solve ft06 [seed] [папка результатів]\n--json файл.json [seed] [папка результатів]\n--list");
        return 0;
    }
    switch (args[0])
    {
        case "--list":
            foreach (var e in examples) Console.WriteLine($"{e.Name,-22} {e.Jobs.Length,3} робіт x {e.Machines,2} верстатів; LB={e.LowerBound}");
            break;
        case "--self-test":
            Verification.Run(examples);
            break;
        case "--experiments":
            Experiments.Run(examples, args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(root, "results"), libraryPath);
            break;
        case "--solve":
        case "--json":
            if (args.Length < 2) throw new ArgumentException("Вкажіть назву прикладу або файл.");
            var instance = args[0] == "--json" ? InstanceReader.ReadJson(args[1]) :
                examples.SingleOrDefault(x => x.Name == args[1]) ?? throw new ArgumentException("Приклад не знайдено.");
            var seed = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 42;
            var output = args.Length > 3 ? Path.GetFullPath(args[3]) : Path.Combine(root, "results", "manual");
            Directory.CreateDirectory(output);
            var result = new MmasSolver(instance, new AcoParameters(Seed: seed)).Run(
                f => { if (f.Iteration == 1 || f.Iteration % 20 == 0) Console.WriteLine($"Ітерація {f.Iteration,4}: найкраще {f.BestSoFar}, середнє {f.Mean:F2}"); }, captureTrace: true);
            Export.Save(instance, result, output, instance.Name);
            Console.WriteLine($"{instance.Name}: greedy={result.Greedy.Makespan}, MMAS={result.Best.Makespan}, LB={instance.LowerBound}, час={result.ElapsedMs:F2} мс. Розклад коректний.");
            break;
        default: throw new ArgumentException("Невідома команда. Використайте --help.");
    }
    return 0;
}
catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException or System.Text.Json.JsonException or OverflowException)
{
    Console.Error.WriteLine("Помилка: " + ex.Message);
    return 1;
}

static string FindRoot()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "data", "jobshop1.txt"))) return dir.FullName;
    throw new FileNotFoundException("Папку data/jobshop1.txt не знайдено.");
}

internal static class Export
{
    public static void Save(Instance instance, RunResult result, string folder, string name)
    {
        Directory.CreateDirectory(folder);
        Json.Save(Path.Combine(folder, name + ".json"), new { instance, result });
        File.WriteAllLines(Path.Combine(folder, name + "_schedule.csv"),
            new[] { "operation;job;index;machine;start;end;duration" }.Concat(result.Best.Slots.Select(s =>
            {
                var o = instance.Operations[s.OperationId];
                return $"{o.Label};{o.Job+1};{o.Index+1};{o.Machine+1};{s.Start};{s.End};{o.Duration}";
            })), Encoding.UTF8);
        File.WriteAllLines(Path.Combine(folder, name + "_history.csv"),
            new[] { "iteration;iteration_best;best_so_far;mean;tau_min;tau_max;restarted" }.Concat(result.History.Select(f =>
                $"{f.Iteration};{f.IterationBest};{f.BestSoFar};{f.Mean:R};{f.TauMin:R};{f.TauMax:R};{f.Restarted}")), Encoding.UTF8);
    }
}

internal static class Verification
{
    public static void Run(List<Instance> all)
    {
        var checks = 0;
        void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); checks++; }
        Check(all.Count == 89, "Очікуються 82 OR-Library та 7 малих прикладів.");
        foreach (var instance in all) instance.Validate();
        foreach (var instance in Examples.Small().Append(Instance.Create("zero_duration",2,[[0,0,1,3],[1,1,0,2]])))
        {
            var exact = Exact(instance);
            var parameters = new AcoParameters(Ants: 15, Iterations: 60, RestartAfter: 8);
            var run = new MmasSolver(instance, parameters).Run(captureTrace: true);
            Check(run.Best.Makespan == exact, $"Не збігся точний оптимум {instance.Name}.");
            Check(run.Best.Makespan >= instance.LowerBound, "Розв'язок нижче LB.");
            run.Best.Validate(instance);
            var repeat = new MmasSolver(instance, parameters).Run();
            Check(run.Best.Slots.SequenceEqual(repeat.Best.Slots), "Seed не відтворює результат.");
            var previous = run.Greedy.Makespan;
            foreach (var f in run.History)
            {
                Check(f.BestSoFar <= previous && f.TauMin > 0 && f.TauMax >= f.TauMin, "Порушення інваріантів MMAS.");
                previous = f.BestSoFar;
                foreach (var step in f.FirstAntTrace!)
                {
                    Check(Math.Abs(step.Candidates.Sum(x => x.Probability) - 1) < 1e-12, "Імовірності не нормовані.");
                    Check(step.Candidates.Any(x => x.OperationId == step.ChosenId), "Вибір поза кандидатною множиною.");
                    Check(step.Candidates.All(x => x.Pheromone > 0 && double.IsFinite(x.Probability)), "Некоректна вага.");
                }
            }
            Check(run.Restarts > 0, "Рестарт не перевірено.");
            Console.WriteLine($"PASS {instance.Name,-18} exact={exact}, MMAS={run.Best.Makespan}");
        }
        ExpectFailure(() => Instance.Create("bad", 2, [[2,3]]));
        ExpectFailure(() => Instance.Create("bad", 1, [[0,0]]));
        ExpectFailure(() => Instance.Create("bad", 1, [[0,-1]]));
        ExpectFailure(() => Instance.Create("bad", 1, [[0]]));
        ExpectFailure(() => Instance.Create("../bad", 1, [[0,1]]));
        ExpectFailure(() => new AcoParameters(Rho: 0).Validate());
        ExpectFailure(() => new AcoParameters(Beta: double.NaN).Validate());
        var small = Examples.Small()[0];
        ExpectFailure(() => new Schedule([new(0,0,3),new(1,0,2),new(2,0,2),new(3,0,4)],4,[0,1,2,3]).Validate(small));
        ExpectFailure(() => new Schedule([new(0,0,3),new(0,3,5),new(2,0,2),new(3,3,7)],7,[0,1,2,3]).Validate(small));
        var token = new CancellationToken(true);
        try { new MmasSolver(small, new()).Run(cancellationToken: token); throw new InvalidOperationException("Не працює скасування."); }
        catch (OperationCanceledException) { checks++; }
        Console.WriteLine($"PASS: {checks} перевірок інваріантів, 9 перевірок відхилення помилок; усі 82 записи OR-Library прочитані.");
    }

    private static void ExpectFailure(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException) { return; }
        throw new InvalidOperationException("Некоректні дані не відхилено.");
    }

    public static int Exact(Instance instance)
    {
        if (instance.Count > 10) throw new ArgumentException("Точний перебір лише для малих прикладів.");
        var next = new int[instance.Jobs.Length];
        var jobs = new int[instance.Jobs.Length];
        var machines = new int[instance.Machines];
        var best = int.MaxValue;
        void Search(int depth, int makespan)
        {
            if (makespan >= best) return;
            if (depth == instance.Count) { best = makespan; return; }
            for (var j = 0; j < next.Length; j++)
            {
                if (next[j] == instance.Jobs[j].Length) continue;
                var o = instance.Jobs[j][next[j]];
                var oldJob = jobs[j]; var oldMachine = machines[o.Machine];
                var finish = Math.Max(oldJob, oldMachine) + o.Duration;
                jobs[j] = machines[o.Machine] = finish; next[j]++;
                Search(depth + 1, Math.Max(makespan, finish));
                next[j]--; jobs[j] = oldJob; machines[o.Machine] = oldMachine;
            }
        }
        Search(0,0);
        return best;
    }
}

internal static class Experiments
{
    public static void Run(List<Instance> all, string folder, string libraryPath)
    {
        Directory.CreateDirectory(folder);
        var baseline = new AcoParameters();
        var settings = new (string Name, AcoParameters P)[]
        {
            ("base", baseline), ("alpha0",baseline with {Alpha=0}), ("alpha2",baseline with {Alpha=2}),
            ("beta0",baseline with {Beta=0}), ("beta5",baseline with {Beta=5}),
            ("rho002",baseline with {Rho=0.02}), ("rho010",baseline with {Rho=0.1}),
            ("ratio001",baseline with {MinRatio=0.01}), ("ratio010",baseline with {MinRatio=0.1}),
            ("ants10",baseline with {Ants=10}), ("ants60",baseline with {Ants=60})
        };
        var reference = new Dictionary<string,int> { ["ft06"]=55,["ft10"]=930,["la01"]=666,["la02"]=655,["la03"]=597 };
        var tuning = new List<object>();
        var scores = new List<(string Name,AcoParameters P,double Gap)>();
        foreach (var setting in settings)
        {
            var gapSum = 0.0;
            foreach (var name in new[] { "la02", "ft10" })
            {
                var instance = all.Single(x=>x.Name==name);
                var result = new MmasSolver(instance, setting.P).Run();
                var gap = 100.0 * (result.Best.Makespan - reference[name]) / reference[name];
                gapSum += gap;
                Export.Save(instance,result,Path.Combine(folder,"tuning"),setting.Name+"_"+name);
                tuning.Add(new { configuration=setting.Name, instance=name, parameters=setting.P,
                    greedy=result.Greedy.Makespan, best=result.Best.Makespan, reference=reference[name], gap,
                    result.ElapsedMs, result.SchedulesEvaluated, result.Restarts });
                Console.WriteLine($"{setting.Name,-10} {name}: Cmax={result.Best.Makespan}, gap={gap:F2}%, {result.ElapsedMs:F1} ms");
            }
            scores.Add((setting.Name,setting.P,gapSum/2));
        }
        var chosen = scores.OrderBy(s=>s.Gap).ThenBy(s=>s.P.Ants).ThenBy(s=>s.Name,StringComparer.Ordinal).First();
        Json.Save(Path.Combine(folder,"tuning_summary.json"),tuning);
        Json.Save(Path.Combine(folder,"selected_parameters.json"),new {configuration=chosen.Name,meanGap=chosen.Gap,parameters=chosen.P});
        var summary = new List<object>();
        foreach (var instance in Examples.Small().Concat(all.Where(x=>new[]{"ft06","la01","la03","ft10","la02"}.Contains(x.Name))))
        {
            var result = new MmasSolver(instance,chosen.P).Run(captureTrace: instance.Count <= 100);
            Export.Save(instance,result,folder,instance.Name);
            var target = reference.TryGetValue(instance.Name,out var r) ? r : Verification.Exact(instance);
            summary.Add(new {instance=instance.Name,jobs=instance.Jobs.Length,instance.Machines,operations=instance.Count,
                lowerBound=instance.LowerBound,reference=target,greedy=result.Greedy.Makespan,best=result.Best.Makespan,
                gap=100.0*(result.Best.Makespan-target)/target,result.ElapsedMs,result.Restarts,
                role=instance.Name is "ft10" or "la02" ? "tuning" : "validation"});
            Console.WriteLine($"FINAL {instance.Name,-18}: {result.Best.Makespan} (reference {target}), valid");
        }
        Json.Save(Path.Combine(folder,"summary.json"),summary);
        Json.Save(Path.Combine(folder,"metadata.json"),new {algorithm="MMAS best-so-far / conflict set / SPT heuristic",seed=42,
            iterations=200,tuningInstances=new[]{"la02","ft10"},runsPerConfiguration=1,
            runtime=RuntimeInformation.FrameworkDescription,os=RuntimeInformation.OSDescription,
            logicalProcessors=Environment.ProcessorCount,source="https://people.brunel.ac.uk/~mastjjb/jeb/orlib/files/jobshop1.txt",
            sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(libraryPath))),timestamp=DateTimeOffset.UtcNow});
        Console.WriteLine($"Обрано {chosen.Name}, середній gap={chosen.Gap:F2}%. Результати: {folder}");
    }
}
