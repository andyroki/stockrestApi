using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace StockApi.Tests;

public class StockApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public StockApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetStocks_WithValidParameters_ReturnsOk()
    {
        // Arrange
        var symbol = "TEST.US";
        var startDate = "20250101";
        var endDate = "20250131";

        // Act
        var response = await _client.GetAsync($"/api/stocks?symbol={symbol}&startDate={startDate}&endDate={endDate}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await response.Content.ReadFromJsonAsync<List<StockDataPoint>>();
        Assert.NotNull(data);
        Assert.NotEmpty(data);
    }

    [Fact]
    public async Task GetStocks_WithMissingParameters_ReturnsBadRequest()
    {
        // Act
        var response = await _client.GetAsync("/api/stocks");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetStocks_WithNonExistentSymbol_ReturnsNotFound()
    {
        // Arrange
        var symbol = "NONEXISTENT.US";
        var startDate = "20250101";
        var endDate = "20250131";

        // Act
        var response = await _client.GetAsync($"/api/stocks?symbol={symbol}&startDate={startDate}&endDate={endDate}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetStocks_WithDateRange_ReturnsCorrectData()
    {
        // Arrange
        var symbol = "TEST.US";
        var startDate = "20250115";
        var endDate = "20250120";

        // Act
        var response = await _client.GetAsync($"/api/stocks?symbol={symbol}&startDate={startDate}&endDate={endDate}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await response.Content.ReadFromJsonAsync<List<StockDataPoint>>();
        Assert.NotNull(data);

        // Verify all dates are within range
        foreach (var point in data)
        {
            Assert.True(point.Time >= new DateTime(2025, 1, 15));
            Assert.True(point.Time <= new DateTime(2025, 1, 20));
        }
    }

    [Fact]
    public async Task GetStocks_SkipsHeaderLine_ReturnsValidData()
    {
        // Arrange
        var symbol = "TEST.US";
        var startDate = "20250101";
        var endDate = "20250131";

        // Act
        var response = await _client.GetAsync($"/api/stocks?symbol={symbol}&startDate={startDate}&endDate={endDate}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await response.Content.ReadFromJsonAsync<List<StockDataPoint>>();
        Assert.NotNull(data);

        // Verify no data point has invalid values (which would happen if header was parsed)
        foreach (var point in data)
        {
            Assert.True(point.Open > 0);
            Assert.True(point.High > 0);
            Assert.True(point.Low > 0);
            Assert.True(point.Close > 0);
        }
    }
}

public class StockDataPoint
{
    public DateTime Time { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
}