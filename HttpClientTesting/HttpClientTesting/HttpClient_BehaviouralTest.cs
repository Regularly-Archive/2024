using Moq.Protected;
using Newtonsoft.Json;
using RichardSzalay.MockHttp;
using Shouldly;
using System.Net;
using System.Net.Http.Json;

namespace HttpClientTesting;

public class HttpClient_BehaviouralTest
{
    [Fact]
    public async Task It_Should_Return_200_OK_After_Requested()
    {
        var payload = JsonConvert.SerializeObject(new { code = 0, success = true });
    
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(payload),
            });

        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(() =>
            {
                return new HttpClient(handlerMock.Object);
            });

        var httpClientFactory = httpClientFactoryMock.Object;
        var httpClient = httpClientFactory.CreateClient("ProductAPI");

        var response = await httpClient.SendAsync(new HttpRequestMessage() { 
            RequestUri = new Uri("http://localhost:8080/api/products"),
            Method = HttpMethod.Post,
        });

        response.ShouldSatisfyAllConditions(
            () => response.StatusCode.ShouldBe(HttpStatusCode.OK),
            () => response.Content.ShouldBeOfType(typeof(StringContent)),
            async () => (await response.Content.ReadAsStringAsync()).ShouldBe(payload)
        );
    }

    [Fact]
    public async Task It_Should_Return_BadRequest_After_Requested()
    {
        var payload = "永远相信美好的事情即将发生";

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent(payload),
            });

        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(() =>
            {
                return new HttpClient(handlerMock.Object);
            });

        var httpClientFactory = httpClientFactoryMock.Object;
        var httpClient = httpClientFactory.CreateClient("ProductAPI");

        var response = await httpClient.SendAsync(new HttpRequestMessage()
        {
            RequestUri = new Uri("http://localhost:8080/api/products"),
            Method = HttpMethod.Post,
        });

        response.ShouldSatisfyAllConditions(
            () => response.StatusCode.ShouldBe(HttpStatusCode.BadRequest),
            () => response.Content.ShouldBeOfType(typeof(StringContent)),
            async () => (await response.Content.ReadAsStringAsync()).ShouldBe(payload)
        );
    }

    [Fact]
    public async Task It_Should_Return_Json_Payload_After_Requested()
    {
        var payload = JsonConvert.SerializeObject(new { code = 0, success = true });

        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, "http://localhost:8080/api/products")
                .Respond("application/json", payload);

        var client = mockHttp.ToHttpClient();
        var response = await client.GetAsync("http://localhost:8080/api/products");

        response.ShouldSatisfyAllConditions(
            () => response.StatusCode.ShouldBe(HttpStatusCode.OK),
            () => response.Content.ShouldBeOfType(typeof(StringContent)),
            async () => (await response.Content.ReadAsStringAsync()).ShouldBe(payload)
        );
    }
}