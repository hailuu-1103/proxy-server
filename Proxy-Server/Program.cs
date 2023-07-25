using System.Net;
using StackExchange.Redis;

public class ProxyServer
{
    private readonly ConnectionMultiplexer redisConnection;
    private readonly HttpClient            httpClient;

    public ProxyServer()
    {
        // Replace "localhost" with your Redis server address if needed
        var connectionOptions = ConfigurationOptions.Parse("localhost,abortConnect=false");
        redisConnection = ConnectionMultiplexer.Connect(connectionOptions);

        // Initialize HttpClient
        httpClient = new HttpClient();
    }

    public async Task StartProxyServer(string listenAddress, int listenPort)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://{listenAddress}:{listenPort}/");
        listener.Start();

        Console.WriteLine($"Proxy server is running on http://{listenAddress}:{listenPort}");

        while (true)
        {
            var context = await listener.GetContextAsync();
            _ = HandleClientRequestAsync(context);
        }
    }

    private async Task HandleClientRequestAsync(HttpListenerContext context)
    {
        var request  = context.Request;
        var response = context.Response;

        try
        {
            // Check if the data exists in Redis (using request URL as cache key)
            var cacheKey   = request.Url.ToString();
            var cache      = redisConnection.GetDatabase();
            var cachedData = await cache.StringGetAsync(cacheKey);

            if (!cachedData.IsNull)
            {
                // Serve the cached response to the client
                response.ContentType = response.Headers["Content-Type"]; // Set the content type of the cached response
                response.ContentLength64 = cachedData.Length();
                await response.OutputStream.WriteAsync(cachedData);
            }
            else
            {
                // Fetch data from the destination server
                var destinationUri     = new Uri("http://103.214.9.148"); // Replace with your destination server URL
                var destinationRequest = new HttpRequestMessage(new HttpMethod(request.HttpMethod), destinationUri);
                foreach (var headerKey in request.Headers.AllKeys)
                {
                    destinationRequest.Headers.TryAddWithoutValidation(headerKey, request.Headers[headerKey]);
                }

                var destinationResponse = await httpClient.SendAsync(destinationRequest);
                var responseData        = await destinationResponse.Content.ReadAsByteArrayAsync();

                // Cache the response in Redis with an expiration time
                await cache.StringSetAsync(cacheKey, responseData, expiry: TimeSpan.FromMinutes(5));

                // Copy destination response headers to the proxy server response
                foreach (var header in destinationResponse.Headers)
                {
                    response.Headers[header.Key] = string.Join(",", header.Value);
                }

                response.Headers["Content-Type"] = destinationResponse.Content.Headers.ContentType?.ToString();

                // Serve the response back to the client
                response.ContentType     = response.Headers["Content-Type"];
                response.ContentLength64 = responseData.Length;
                await response.OutputStream.WriteAsync(responseData);
            }

            response.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.Close();
        }
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        var proxyServer = new ProxyServer();
        proxyServer.StartProxyServer("localhost", 80).Wait();
    }
}