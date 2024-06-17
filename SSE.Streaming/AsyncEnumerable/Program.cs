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
}
