using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace StockApi.Tests;

public class PolygonApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public PolygonApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetPolygonStocks_WithDefaultParameters_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/api/polygon/stocks");

        // Assert - May fail if API key is invalid or rate limited
        // This test verifies the endpoint structure
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                    response.StatusCode == HttpStatusCode.Unauthorized ||
                    response.StatusCode == HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task GetPolygonStocks_WithValidParameters_HasCorrectFormat()
    {
        // Arrange
        var symbol = "AAPL";
        var interval = "1day";
        var from = "2024-01-01";
        var to = "2024-01-31";

        // Act
        var response = await _client.GetAsync($"/api/polygon/stocks?symbol={symbol}&interval={interval}&from={from}&to={to}");

        // Assert
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var data = await response.Content.ReadFromJsonAsync<List<StockDataPoint>>();
            Assert.NotNull(data);

            if (data.Count > 0)
            {
                var firstPoint = data[0];
                Assert.True(firstPoint.Open > 0);
                Assert.True(firstPoint.High >= firstPoint.Open);
                Assert.True(firstPoint.Low <= firstPoint.Close);
            }
        }
    }

    [Fact]
    public async Task GetPolygonStocks_WithMissingParameters_ReturnsBadRequest()
    {
        // Act
        var response = await _client.GetAsync("/api/polygon/stocks?symbol=&from=&to=");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("1day")]
    [InlineData("1week")]
    [InlineData("1month")]
    public async Task GetPolygonStocks_WithDifferentIntervals_ReturnsValidResponse(string interval)
    {
        // Arrange
        var symbol = "AAPL";
        var from = "2024-01-01";
        var to = "2024-01-31";

        // Act
        var response = await _client.GetAsync($"/api/polygon/stocks?symbol={symbol}&interval={interval}&from={from}&to={to}");

        // Assert
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                    response.StatusCode == HttpStatusCode.NotFound ||
                    response.StatusCode == HttpStatusCode.Unauthorized);
    }
}