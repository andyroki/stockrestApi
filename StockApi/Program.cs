using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.ComponentModel;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

// Configure CORS based on environment
builder.Services.AddCors(options =>
{
    options.AddPolicy("ProductionPolicy", policy =>
    {
        policy.WithOrigins("https://game.rokizone.com", "https://dev.rokizone.com","https://tradepast.com")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });

    options.AddPolicy("DevelopmentPolicy", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Make Program class accessible to tests


// Use different CORS policy based on environment
if (app.Environment.IsDevelopment())
{
    app.UseCors("DevelopmentPolicy");
}
else
{
    app.UseCors("ProductionPolicy");
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// GET /api/stocks?symbol=AACBU.US&startDate=20250213&endDate=20250221
app.MapGet("/api/stocks", async (
    [FromQuery][DefaultValue("aapl")] string symbol,
    [FromQuery][DefaultValue("20200213")] string startDate,
    [FromQuery][DefaultValue("20200221")] string endDate,
    [FromServices] IConfiguration config) =>
{
    if (string.IsNullOrEmpty(symbol) || string.IsNullOrEmpty(startDate) || string.IsNullOrEmpty(endDate))
    {
        return Results.BadRequest(new { error = "symbol, startDate, and endDate are required" });
    }

    var dataFolder = config["StockDataFolder"] ?? "./data/stockdata";
    var fileName = $"{symbol.ToLower()}.us.txt";
    var filePath = Path.Combine(dataFolder, fileName);

    if (!File.Exists(filePath))
    {
        return Results.NotFound(new { error = $"Stock data file not found for symbol: {symbol}" });
    }

    try
    {
        var startDateTime = ParseDate(startDate);
        var endDateTime = ParseDate(endDate);

        var stockData = new List<StockDataPoint>();
        var lines = await File.ReadAllLinesAsync(filePath);

        // Skip first line if it contains column headers
        var dataLines = lines.Skip(1);

        foreach (var line in dataLines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var parts = line.Split(',');
            if (parts.Length < 10) continue;

            var dateStr = parts[2];

            // Skip if date parsing fails (in case of header row)
            if (!DateTime.TryParseExact(dateStr, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var lineDate))
                continue;

            // From date: if exact date not found, return from next available day (>=)
            // To date: return exact or before (<=)
            if (lineDate >= startDateTime && lineDate <= endDateTime)
            {
                // Try to parse numeric values, skip line if any fail
                if (!decimal.TryParse(parts[4], out var open) ||
                    !decimal.TryParse(parts[5], out var high) ||
                    !decimal.TryParse(parts[6], out var low) ||
                    !decimal.TryParse(parts[7], out var close) ||
                    !decimal.TryParse(parts[8], out var volume))
                    continue;

                stockData.Add(new StockDataPoint
                {
                    Time = lineDate,
                    Open = open,
                    High = high,
                    Low = low,
                    Close = close,
                    Volume = volume
                });
            }
        }

        if (stockData.Count == 0)
        {
            return Results.NotFound(new { error = "No data found for the specified criteria" });
        }

        return Results.Ok(new StockDataResponse
        {
            Symbol = symbol.ToUpper().Replace(".US", ""),
            StartDate = startDateTime.ToString("yyyy-MM-dd"),
            EndDate = endDateTime.ToString("yyyy-MM-dd"),
            DataPoints = stockData.Count,
            Data = stockData
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
})
.WithName("GetStockData");

// GET /api/polygon/stocks?symbol=AAPL&interval=1day&from=2025-01-01&to=2025-01-31
app.MapGet("/api/polygon/stocks", async (
    [FromQuery][DefaultValue("AAPL")] string symbol,
    [FromQuery][DefaultValue("1day")] string interval,
    [FromQuery][DefaultValue("2025-01-01")] string from,
    [FromQuery][DefaultValue("2025-01-31")] string to,
    [FromServices] IConfiguration config,
    [FromServices] IHttpClientFactory httpClientFactory) =>
{
    if (string.IsNullOrEmpty(symbol) || string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
    {
        return Results.BadRequest(new { error = "symbol, from, and to are required" });
    }

    try
    {
        var apiKey = config["PolygonApiKey"] ?? "D5ehaPJ0ASQqbeex9t2ikIpDWfxKxowb";

        // Convert interval format
        var polygonInterval = interval switch
        {
            "1day" => "1/day",
            "1week" => "1/week",
            "1month" => "1/month",
            _ => "1/day"
        };

        var url = $"https://api.polygon.io/v2/aggs/ticker/{Uri.EscapeDataString(symbol)}/range/{polygonInterval}/{from}/{to}?adjusted=true&sort=asc&limit=50000&apiKey={apiKey}";

        var client = httpClientFactory.CreateClient();
        var response = await client.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            return Results.Problem(detail: $"Network error: {response.StatusCode}", statusCode: (int)response.StatusCode);
        }

        var json = await response.Content.ReadFromJsonAsync<PolygonResponse>();

        if (json?.Results == null || json.Results.Count == 0)
        {
            return Results.NotFound(new { error = $"No OHLC data found for {symbol} in the given range." });
        }

        var ohlcData = json.Results.Select(item => new StockDataPoint
        {
            Time = DateTimeOffset.FromUnixTimeMilliseconds(item.T).DateTime,
            Open = item.O,
            High = item.H,
            Low = item.L,
            Close = item.C,
            Volume = item.V
        }).ToList();

        var startDateTime = ohlcData.Min(d => d.Time);
        var endDateTime = ohlcData.Max(d => d.Time);

        return Results.Ok(new StockDataResponse
        {
            Symbol = symbol.ToUpper().Replace(".US", ""),
            StartDate = startDateTime.ToString("yyyy-MM-dd"),
            EndDate = endDateTime.ToString("yyyy-MM-dd"),
            DataPoints = ohlcData.Count,
            Data = ohlcData
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
})
.WithName("GetPolygonStockData");

// GET /api/stocks/random?months=6
app.MapGet("/api/stocks/random", async (
    [FromQuery][DefaultValue(6)] int months,
    [FromServices] IConfiguration config) =>
{
    try
    {
        var dataFolder = config["StockDataFolder"] ?? "./data/stockdata";

        if (!Directory.Exists(dataFolder))
        {
            return Results.NotFound(new { error = "Stock data folder not found" });
        }

        // Get all .txt files in the folder
        var files = Directory.GetFiles(dataFolder, "*.txt");

        if (files.Length == 0)
        {
            return Results.NotFound(new { error = "No stock data files found" });
        }

        // Select a random file
        var random = new Random();
        var randomFile = files[random.Next(files.Length)];
        var fileName = Path.GetFileNameWithoutExtension(randomFile);
        var symbol = fileName.ToUpper();

        // Read the file and parse all dates
        var lines = await File.ReadAllLinesAsync(randomFile);
        var dataLines = lines.Skip(1).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

        if (dataLines.Count == 0)
        {
            return Results.NotFound(new { error = $"No data found in file: {fileName}" });
        }

        // Parse all available dates
        var availableDates = new List<DateTime>();
        foreach (var line in dataLines)
        {
            var parts = line.Split(',');
            if (parts.Length >= 3 && DateTime.TryParseExact(parts[2], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                availableDates.Add(date);
            }
        }

        if (availableDates.Count == 0)
        {
            return Results.NotFound(new { error = $"No valid dates found in file: {fileName}" });
        }

        // Sort dates to find valid range
        availableDates = availableDates.OrderBy(d => d).ToList();
        var minDate = availableDates.First();
        var maxDate = availableDates.Last();

        // Calculate the date range for the requested months
        var endDate = maxDate.AddMonths(-months);

        if (endDate < minDate)
        {
            endDate = minDate;
        }

        // Select a random start date that allows for 'months' of data
        var validStartDates = availableDates.Where(d => d <= endDate).ToList();

        if (validStartDates.Count == 0)
        {
            // Not enough data for requested months, use earliest available
            var randomStartDate = minDate;
            var randomEndDate = availableDates.Where(d => d <= minDate.AddMonths(months)).LastOrDefault();
            if (randomEndDate == default) randomEndDate = maxDate;

            return await GetStockDataForDateRange(randomFile, symbol, randomStartDate, randomEndDate);
        }

        // Pick a random start date
        var startDate = validStartDates[random.Next(validStartDates.Count)];
        var toDate = startDate.AddMonths(months);

        // Ensure toDate doesn't exceed available data
        if (toDate > maxDate)
        {
            toDate = maxDate;
        }

        return await GetStockDataForDateRange(randomFile, symbol, startDate, toDate);
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
})
.WithName("GetRandomStockData");

app.Run();

async Task<IResult> GetStockDataForDateRange(string filePath, string symbol, DateTime startDate, DateTime endDate)
{
    var stockData = new List<StockDataPoint>();
    var lines = await File.ReadAllLinesAsync(filePath);
    var dataLines = lines.Skip(1);

    foreach (var line in dataLines)
    {
        if (string.IsNullOrWhiteSpace(line)) continue;

        var parts = line.Split(',');
        if (parts.Length < 10) continue;

        var dateStr = parts[2];

        if (!DateTime.TryParseExact(dateStr, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var lineDate))
            continue;

        if (lineDate >= startDate && lineDate <= endDate)
        {
            if (!decimal.TryParse(parts[4], out var open) ||
                !decimal.TryParse(parts[5], out var high) ||
                !decimal.TryParse(parts[6], out var low) ||
                !decimal.TryParse(parts[7], out var close) ||
                !decimal.TryParse(parts[8], out var volume))
                continue;

            stockData.Add(new StockDataPoint
            {
                Time = lineDate,
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = volume
            });
        }
    }

    return Results.Ok(new StockDataResponse
    {
        Symbol = symbol.ToUpper().Replace(".US", ""),
        StartDate = startDate.ToString("yyyy-MM-dd"),
        EndDate = endDate.ToString("yyyy-MM-dd"),
        DataPoints = stockData.Count,
        Data = stockData
    });
}

static DateTime ParseDate(string dateStr)
{
    return DateTime.ParseExact(dateStr, "yyyyMMdd", CultureInfo.InvariantCulture);
}

public record StockDataPoint
{
    public DateTime Time { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
}

public class StockDataResponse
{
    public string Symbol { get; set; } = string.Empty;
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public int DataPoints { get; set; }
    public List<StockDataPoint> Data { get; set; } = new();
}

public record PolygonResponse
{
    [JsonPropertyName("results")]
    public List<PolygonResult> Results { get; set; } = new();
}

public record PolygonResult
{
    [JsonPropertyName("t")]
    public long T { get; set; } // timestamp in milliseconds

    [JsonPropertyName("o")]
    public decimal O { get; set; } // open

    [JsonPropertyName("h")]
    public decimal H { get; set; } // high

    [JsonPropertyName("l")]
    public decimal L { get; set; } // low

    [JsonPropertyName("c")]
    public decimal C { get; set; } // close

    [JsonPropertyName("v")]
    public decimal V { get; set; } // volume
}
public partial class Program { }
