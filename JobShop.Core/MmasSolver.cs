using System.Diagnostics;

namespace JobShop.Core;

public sealed record AcoParameters(int Ants = 30, int Iterations = 200, double Alpha = 1,
    double Beta = 2, double Rho = 0.05, double MinRatio = 0.05, int RestartAfter = 40, int Seed = 42)
{
    public void Validate()
    {
        if (Ants is < 1 or > 500 || Iterations is < 1 or > 5000 ||
            !double.IsFinite(Alpha) || Alpha is < 0 or > 10 || !double.IsFinite(Beta) || Beta is < 0 or > 10 ||
            !double.IsFinite(Rho) || Rho is <= 0 or >= 1 ||
            !double.IsFinite(MinRatio) || MinRatio is <= 0 or > 1 || RestartAfter < 1)
            throw new ArgumentException("Некоректні параметри MMAS.");
    }
}

public sealed record Candidate(int OperationId, int EarliestStart, double Pheromone, double Heuristic, double Probability);
public sealed record ConstructionStep(int Step, int PivotId, int ChosenId, Candidate[] Candidates, Slot Added);
public sealed record IterationFrame(int Iteration, int IterationBest, int BestSoFar, double Mean,
    double TauMin, double TauMax, bool Restarted, Schedule BestSchedule,
    ConstructionStep[]? FirstAntTrace);
public sealed record RunResult(string Instance, AcoParameters Parameters, Schedule Greedy,
    Schedule Best, IterationFrame[] History, double ElapsedMs, int SchedulesEvaluated, int Restarts);

public sealed class MmasSolver
{
    private readonly Instance instance;
    private readonly Operation[] operations;
    private readonly AcoParameters parameters;
    private readonly Random random;
    private readonly double[][] tau;
    private readonly double[] eta;
    private double tauMin;
    private double tauMax;

    public MmasSolver(Instance instance, AcoParameters parameters)
    {
        instance.Validate(); parameters.Validate();
        this.instance = instance; this.parameters = parameters;
        operations = instance.Operations;
        random = new Random(parameters.Seed);
        tau = Enumerable.Range(0, instance.Count + instance.Machines).Select(_ => new double[instance.Count]).ToArray();
        eta = operations.Select(o => 1.0 / (1.0 + o.Duration)).ToArray();
    }

    public RunResult Run(Action<IterationFrame>? progress = null, CancellationToken cancellationToken = default,
        bool captureTrace = false)
    {
        var timer = Stopwatch.StartNew();
        var greedy = Construct(true, null, cancellationToken);
        var best = greedy;
        SetBounds(best.Makespan);
        ResetPheromone();
        var history = new List<IterationFrame>();
        var noImprovement = 0;
        var restarts = 0;
        for (var iteration = 1; iteration <= parameters.Iterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Schedule? iterationBest = null;
            var sum = 0L;
            List<ConstructionStep>? firstTrace = captureTrace ? [] : null;
            for (var ant = 0; ant < parameters.Ants; ant++)
            {
                var solution = Construct(false, ant == 0 ? firstTrace : null, cancellationToken);
                solution.Validate(instance);
                sum += solution.Makespan;
                if (iterationBest is null || solution.Makespan < iterationBest.Makespan) iterationBest = solution;
            }
            if (iterationBest!.Makespan < best.Makespan)
            {
                best = iterationBest;
                noImprovement = 0;
            }
            else noImprovement++;

            SetBounds(best.Makespan);
            UpdatePheromone(best);
            var restarted = noImprovement >= parameters.RestartAfter;
            if (restarted)
            {
                ResetPheromone();
                noImprovement = 0;
                restarts++;
            }
            var frame = new IterationFrame(iteration, iterationBest.Makespan, best.Makespan,
                (double)sum / parameters.Ants, tauMin, tauMax, restarted, best, firstTrace?.ToArray());
            history.Add(frame);
            progress?.Invoke(frame);
        }
        timer.Stop();
        return new RunResult(instance.Name, parameters, greedy, best, history.ToArray(), timer.Elapsed.TotalMilliseconds,
            checked(parameters.Ants * parameters.Iterations), restarts);
    }

    private Schedule Construct(bool greedy, List<ConstructionStep>? trace, CancellationToken cancellationToken)
    {
        var next = new int[instance.Jobs.Length];
        var jobReady = new int[instance.Jobs.Length];
        var machineReady = new int[instance.Machines];
        var last = Enumerable.Range(instance.Count, instance.Machines).ToArray();
        var slots = new Slot[instance.Count];
        var order = new int[instance.Count];
        var ready = new List<Operation>(instance.Jobs.Length);
        for (var step = 0; step < instance.Count; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ready.Clear();
            for (var j = 0; j < next.Length; j++)
                if (next[j] < instance.Jobs[j].Length) ready.Add(instance.Jobs[j][next[j]]);
            int Start(Operation o) => Math.Max(jobReady[o.Job], machineReady[o.Machine]);
            var pivot = ready.MinBy(o => (Start(o) + o.Duration, o.Id))!;
            var finish = Start(pivot) + pivot.Duration;
            var choices = ready.Where(o => o.Machine == pivot.Machine && (o.Id == pivot.Id || Start(o) < finish)).ToArray();
            var logs = choices.Select(o => parameters.Alpha * Math.Log(tau[last[o.Machine]][o.Id] > 0
                ? tau[last[o.Machine]][o.Id] : 1) + parameters.Beta * Math.Log(eta[o.Id])).ToArray();
            var maxLog = logs.Max();
            var weights = logs.Select(w => Math.Exp(w - maxLog)).ToArray();
            var total = weights.Sum();
            var index = 0;
            if (greedy)
                index = Array.IndexOf(choices, choices.MinBy(o => (o.Duration, o.Id))!);
            else
            {
                var draw = random.NextDouble() * total;
                index = choices.Length - 1;
                for (var k = 0; k < choices.Length; k++)
                    if ((draw -= weights[k]) < 0) { index = k; break; }
            }
            var chosen = choices[index];
            var slot = new Slot(chosen.Id, Start(chosen), Start(chosen) + chosen.Duration);
            if (trace is not null)
                trace.Add(new ConstructionStep(step + 1, pivot.Id, chosen.Id,
                    choices.Select((o, k) => new Candidate(o.Id, Start(o), tau[last[o.Machine]][o.Id],
                        eta[o.Id], weights[k] / total)).ToArray(), slot));
            slots[chosen.Id] = slot;
            order[step] = chosen.Id;
            jobReady[chosen.Job] = slot.End;
            machineReady[chosen.Machine] = slot.End;
            last[chosen.Machine] = chosen.Id;
            next[chosen.Job]++;
        }
        var schedule = new Schedule(slots, machineReady.Max(), order);
        schedule.Validate(instance);
        return schedule;
    }

    private void SetBounds(int makespan)
    {
        tauMax = 1.0 / (parameters.Rho * makespan);
        tauMin = parameters.MinRatio * tauMax;
    }

    private void ResetPheromone()
    {
        foreach (var (from, to) in Edges()) tau[from][to] = tauMax;
    }

    private void UpdatePheromone(Schedule elite)
    {
        foreach (var (from, to) in Edges()) tau[from][to] *= 1 - parameters.Rho;
        for (var m = 0; m < instance.Machines; m++)
        {
            var previous = instance.Count + m;
            foreach (var s in elite.Slots.Where(s => operations[s.OperationId].Machine == m).OrderBy(s => s.Start))
            {
                tau[previous][s.OperationId] += 1.0 / elite.Makespan;
                previous = s.OperationId;
            }
        }
        foreach (var (from, to) in Edges()) tau[from][to] = Math.Clamp(tau[from][to], tauMin, tauMax);
    }

    private IEnumerable<(int From, int To)> Edges()
    {
        foreach (var o in operations)
        {
            yield return (instance.Count + o.Machine, o.Id);
            foreach (var from in operations)
                if (from.Machine == o.Machine && from.Id != o.Id) yield return (from.Id, o.Id);
        }
    }
}
