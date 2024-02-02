using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Newtonsoft.Json;
using Shouldly;
using WeatherAPI;

namespace HttpClientTesting;

public class HttpClient_IntegrationTest : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HttpClient_IntegrationTest(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task It_Should_Return_Weather_Forecast_When_Call_Weather_API()
    {
        using (var client = _factory.CreateClient())
        {
            var response = await client.GetAsync("/WeatherForecast");
            response.ShouldSatisfyAllConditions(
                () => response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK),
                async () => (await response.Content.ReadAsStringAsync()).ShouldNotBeNullOrEmpty()
            );
        }
    }
}