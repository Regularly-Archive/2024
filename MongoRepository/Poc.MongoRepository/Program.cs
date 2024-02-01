using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MongoRepository.Abstration;
using MongoRepository.Configuration;
using MongoRepository.Core;
using MongoRepository.Extensions;
using Poc.MongoRepository;
using Poc.MongoRepository.Models;
using System;

class Program
{
    static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration(builder =>
            {
                builder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            })
            .ConfigureLogging(builder =>
            {
                builder.AddConsole();
            })
            .ConfigureServices((context, services) =>
            {
                services.AddMongoRepository(context.Configuration);
                services.AddScoped<IOrderRepository, OrderRepository>();
            })
            .Build();

        var order = new Order()
        {
            OrderNo = "OR20240131",
            Amount = 11,
            OrderTime = DateTime.UtcNow,
            Address = new Address()
            {
                City = "西安市",
                Region = "雁塔区",
                PostalCode = "710001"
            }
        };

        var serviceProvider = host.Services.CreateScope().ServiceProvider;
        var repository = serviceProvider.GetService<IMongoDbRepository<Order>>();

        var unitOfWork = serviceProvider.GetService<IMongoDbUnitOfWork>();
        var session = await unitOfWork.BeginTransactionAsync();
        await repository.UpsetAsync(x => x.OrderNo == order.OrderNo, order, session);
        await unitOfWork.SaveChangesAsync(session);

        await host.RunAsync();
    }
}