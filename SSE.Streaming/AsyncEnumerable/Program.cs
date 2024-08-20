using System.ComponentModel;

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("IEnumerable: ");
        foreach (var item in await GetNumbers(10))
        {
            Console.Write(item);
        }

        Console.WriteLine();
        Console.WriteLine("IAsyncEnumerable: ");
        await foreach (var item in GetNumbersAsync(10))
        {
            Console.Write(item);
        }
    }

    public static async Task<IEnumerable<int>> GetNumbers(int count)
    {
        var numbers = new List<int>();
        for (var i = 0; i < count; i++)
        {
            await Task.Delay(200);
            numbers.Add(i);
        }

        return numbers;
    }

    public static async IAsyncEnumerable<int> GetNumbersAsync(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await Task.Delay(200);
            yield return i;
        }
    }

    public static async Task DoHeartBeatAsync()
    {
        Console.WriteLine("[HeartBeat] 服务运行中，请按 Ctrl + C 键取消...");
        var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await HeartBeatAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[HeartBeat] 服务已停止.");
        }
    }

    public static async Task HeartBeatAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine($"[HeartBeat] {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}");
            await Task.Delay(1000, cancellationToken);
        }
    }

    public static async Task HttpGetWithCancelAsync()
    {
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(2));

        try
        {
            var httpClient = new HttpClient();
            var response = await httpClient.GetStringAsync("https://localhost:7261/api/Chat/Echo?text=你好", cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("请求已取消");
        }

    }
}
