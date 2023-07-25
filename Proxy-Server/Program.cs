using System.Net;
using StackExchange.Redis;

public class ProxyServer
{
    private readonly ConnectionMultiplexer redisConnection;
    private readonly HttpClient            httpClient;

    public ProxyServer()
    {
        var connectionOptions = ConfigurationOptions.Parse("localhost,abortConnect=false");
        this.redisConnection = ConnectionMultiplexer.Connect(connectionOptions);

        this.httpClient = new HttpClient();
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
            _ = this.HandleClientRequestAsync(context);
        }
    }

    private async Task HandleClientRequestAsync(HttpListenerContext context)
    {
        var request  = context.Request;
        var response = context.Response;

        try
        {
            var cacheKey   = request.Url!.ToString();
            var cache      = this.redisConnection.GetDatabase();
            var cachedData = await cache.StringGetAsync(cacheKey);

            if (!cachedData.IsNull)
            {
                response.ContentType = response.Headers["Content-Type"];
                response.ContentLength64 = cachedData.Length();
                await response.OutputStream.WriteAsync(cachedData);
            }
            else
            {
                var destinationUri     = new Uri("http://103.214.9.148");
                var destinationRequest = new HttpRequestMessage(new HttpMethod(request.HttpMethod), destinationUri);
                foreach (var headerKey in request.Headers.AllKeys)
                {
                    destinationRequest.Headers.TryAddWithoutValidation(headerKey, request.Headers[headerKey]);
                }

                var destinationResponse = await this.httpClient.SendAsync(destinationRequest);
                var responseData        = await destinationResponse.Content.ReadAsByteArrayAsync();

                await cache.StringSetAsync(cacheKey, responseData, expiry: TimeSpan.FromMinutes(5));

                foreach (var header in destinationResponse.Headers)
                {
                    response.Headers[header.Key] = string.Join(",", header.Value);
                }

                response.Headers["Content-Type"] = destinationResponse.Content.Headers.ContentType?.ToString();

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