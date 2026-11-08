using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JobShop.Core;

public sealed record Operation(int Id, int Job, int Index, int Machine, int Duration)
{
    public string Label => $"J{Job + 1}.O{Index + 1}";
}

public sealed record Instance(string Name, int Machines, Operation[][] Jobs)
{
    public Operation[] Operations => Jobs.SelectMany(x => x).ToArray();
    public int Count => Jobs.Sum(x => x.Length);
    public int LowerBound => Math.Max(Jobs.Max(j => j.Sum(o => o.Duration)),
        Enumerable.Range(0, Machines).Max(m => Operations.Where(o => o.Machine == m).Sum(o => o.Duration)));

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Machines is < 1 or > 100 || Jobs.Length is < 1 or > 200)
            throw new ArgumentException("Назва, кількість робіт або верстатів некоректні.");
        if (Name.Length > 100 || Name is "." or ".." || Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            Name.Contains('/') || Name.Contains('\\'))
            throw new ArgumentException("Назва прикладу не повинна містити шлях або недопустимі символи назви файлу.");
        if (Jobs.Any(j => j.Length == 0) || Count > 2000)
            throw new ArgumentException("Потрібна хоча б одна операція в кожній роботі; максимум 2000 операцій.");
        var total = 0L;
        var id = 0;
        for (var j = 0; j < Jobs.Length; j++)
        for (var k = 0; k < Jobs[j].Length; k++)
        {
            var o = Jobs[j][k];
            if (o.Id != id++ || o.Job != j || o.Index != k || o.Machine < 0 || o.Machine >= Machines || o.Duration < 0)
                throw new ArgumentException("Некоректний номер верстата, тривалість або індекс операції.");
            total += o.Duration;
        }
        if (total == 0 || total > int.MaxValue / 2) throw new ArgumentException("Сумарна тривалість має бути додатною і не перевищувати допустиму межу.");
    }

    public static Instance Create(string name, int machines, int[][] pairs)
    {
        var id = 0;
        var jobs = pairs.Select((row, j) =>
        {
            if (row.Length == 0 || row.Length % 2 != 0) throw new ArgumentException("Очікуються пари [верстат, тривалість].");
            return Enumerable.Range(0, row.Length / 2)
                .Select(k => new Operation(id++, j, k, row[2 * k], row[2 * k + 1])).ToArray();
        }).ToArray();
        var result = new Instance(name, machines, jobs);
        result.Validate();
        return result;
    }
}

public sealed record Slot(int OperationId, int Start, int End);
public sealed record Schedule(Slot[] Slots, int Makespan, int[] DispatchOrder)
{
    public void Validate(Instance instance)
    {
        if (Slots.Length != instance.Count || DispatchOrder.Length != instance.Count ||
            Slots.Select(s => s.OperationId).Distinct().Count() != instance.Count ||
            !DispatchOrder.Order().SequenceEqual(Enumerable.Range(0, instance.Count)))
            throw new InvalidDataException("Операції пропущено або повторено.");
        var byId = new Slot[instance.Count];
        var operations = instance.Operations;
        foreach (var s in Slots)
        {
            if (s.OperationId < 0 || s.OperationId >= instance.Count || s.Start < 0 ||
                (long)s.End - s.Start != operations[s.OperationId].Duration)
                throw new InvalidDataException("Некоректна тривалість операції.");
            byId[s.OperationId] = s;
        }
        foreach (var job in instance.Jobs)
            for (var k = 1; k < job.Length; k++)
                if (byId[job[k].Id].Start < byId[job[k - 1].Id].End)
                    throw new InvalidDataException("Порушено порядок операцій роботи.");
        for (var m = 0; m < instance.Machines; m++)
        {
            var row = Slots.Where(s => operations[s.OperationId].Machine == m && s.End > s.Start).OrderBy(s => s.Start).ToArray();
            for (var i = 1; i < row.Length; i++)
                if (row[i].Start < row[i - 1].End) throw new InvalidDataException("Перетин операцій на верстаті.");
        }
        if (Makespan != Slots.Max(s => s.End)) throw new InvalidDataException("Помилковий makespan.");
    }
}

public static class InstanceReader
{
    public static List<Instance> ReadOrLibrary(string path)
    {
        var lines = File.ReadAllLines(path);
        var instances = new List<Instance>();
        for (var i = 0; i < lines.Length; i++)
        {
            var match = Regex.Match(lines[i], @"^\s*instance\s+(\w+)\s*$");
            if (!match.Success) continue;
            var name = match.Groups[1].Value;
            while (++i < lines.Length && !Regex.IsMatch(lines[i], @"^\s*\d+\s+\d+\s*$")) { }
            if (i == lines.Length) throw new InvalidDataException($"Немає розміру {name}.");
            var size = Numbers(lines[i]);
            if (size[0] < 1 || size[0] > 200 || size[1] < 1 || size[1] > 100)
                throw new InvalidDataException("Завеликий або некоректний розмір.");
            var pairs = new int[size[0]][];
            for (var j = 0; j < pairs.Length; j++)
            {
                if (++i >= lines.Length) throw new InvalidDataException($"Неповний приклад {name}.");
                pairs[j] = Numbers(lines[i]);
                if (pairs[j].Length != size[1] * 2) throw new InvalidDataException($"Неповний рядок {name}.");
            }
            instances.Add(Instance.Create(name, size[1], pairs));
        }
        if (instances.Count == 0) throw new InvalidDataException("Не знайдено прикладів OR-Library.");
        return instances;
    }

    public static Instance ReadJson(string path)
    {
        var dto = JsonSerializer.Deserialize<Input>(File.ReadAllText(path), Json.Options)
            ?? throw new InvalidDataException("Порожній JSON.");
        if (dto.Jobs is null || dto.Jobs.Any(j => j is null || j.Any(p => p is null || p.Length != 2)))
            throw new InvalidDataException("jobs повинен містити пари [верстат, тривалість].");
        return Instance.Create(dto.Name ?? "custom", dto.Machines, dto.Jobs.Select(j => j.SelectMany(p => p).ToArray()).ToArray());
    }

    private static int[] Numbers(string line) => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        .Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToArray();
    private sealed record Input(string? Name, int Machines, int[][][] Jobs);
}

public static class Examples
{
    public static List<Instance> Small() =>
    [
        Instance.Create("method_2x2", 2, [[0,3,1,2], [1,2,0,4]]),
        Instance.Create("single_job", 3, [[0,3,1,2,2,4]]),
        Instance.Create("single_machine", 1, [[0,3],[0,2],[0,4]]),
        Instance.Create("parallel_jobs", 2, [[0,3],[1,5]]),
        Instance.Create("return_machine", 2, [[0,2,1,3,0,2],[1,2,0,1]]),
        Instance.Create("bottleneck", 2, [[0,8,1,1],[1,1,0,7],[0,6,1,1]]),
        Instance.Create("ties_3x3", 3, [[0,2,1,2,2,2],[1,2,2,2,0,2],[2,2,0,2,1,2]])
    ];
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    public static void Save<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Options));
}
