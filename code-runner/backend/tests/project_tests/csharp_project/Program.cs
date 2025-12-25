using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System;

// 依赖注入容器设置
var services = new ServiceCollection();
services.AddTransient<ICalculatorService, CalculatorService>();
var serviceProvider = services.BuildServiceProvider();

var service = serviceProvider.GetRequiredService<ICalculatorService>();
var result = service.Calculate();

// JSON 序列化
string json = JsonConvert.SerializeObject(result, Formatting.Indented);
Console.WriteLine(json);

public interface ICalculatorService
{
    CalculationResult Calculate();
}

public class CalculatorService : ICalculatorService
{
    public CalculationResult Calculate()
    {
        var random = new Random();
        int a = random.Next(1, 100);
        int b = random.Next(1, 100);
        int sum = a + b;

        return new CalculationResult
        {
            A = a,
            B = b,
            Sum = sum,
            Timestamp = DateTime.Now,
            Language = "C# Project"
        };
    }
}

public class CalculationResult
{
    public int A { get; set; }
    public int B { get; set; }
    public int Sum { get; set; }
    public DateTime Timestamp { get; set; }
    public string Language { get; set; } = "";
}