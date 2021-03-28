using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Http;
// add for policies
using Polly;
using Microsoft.AspNetCore.Mvc;

namespace Consumer
{
    class Program
    {
        static void Main(string[] args)
        {
            var builder = new HostBuilder()
                .ConfigureServices((hostContext, services) =>
                {
                    // v1 services.AddHttpClient();
                    services.AddTransient<IMyService, MyService1>();

                    // v2 - named HttpClients
                    services.AddHttpClient("GitHub", client =>
                    {
                        // works
                        client.BaseAddress = new Uri("https://github.com/");
                        // doesn't (no header for API key)
                        // client.BaseAddress = new Uri("https://api.github.com/");
                        client.DefaultRequestHeaders.Add("Accept", "text/html"); // returned from HTML pages
                        // client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json"); // not returned from HTML pages
                    });
                    
                    // v3 retry against flaky service
                    services.AddHttpClient("flakyService", client =>
                    {
                        client.BaseAddress = new Uri("http://localhost:5000");
                        client.DefaultRequestHeaders.Add("Accept", "application/json"); // not returned from HTML pages
                    })
                    .AddTransientHttpErrorPolicy(builder => builder.WaitAndRetryAsync(new[]  // v3 add HttpErrorPolicy for retry on 5XX and 408 errors
                    {
                        TimeSpan.FromSeconds(1),
                        TimeSpan.FromSeconds(5),
                        TimeSpan.FromSeconds(10)
                    }));

                    services.AddHttpClient("flakyServiceCB", client =>
                    {
                        client.BaseAddress = new Uri("http://localhost:5000");
                        client.DefaultRequestHeaders.Add("Accept", "application/json"); // not returned from HTML pages
                    }); 
                    
                }).UseConsoleLifetime();

            var host = builder.Build();

            using (var serviceScope = host.Services.CreateScope())
            {
                var services = serviceScope.ServiceProvider;

                DateTime now = DateTime.Now, done;

                try
                {
                    // v1 (hardcoded)
                    MyService1 myService = (MyService1) services.GetRequiredService<IMyService>();
                    myService.URL = "http://www.microsoft.com";
                    Console.WriteLine(myService.GetPage().Result.Substring(0, 500));

                    // v2 (GitHub)
                    IHttpClientFactory _httpClientFactory = services.GetService<IHttpClientFactory>();
                    var client = _httpClientFactory.CreateClient("GitHub"); // pull the registered client out of factory
                    Console.WriteLine(client.GetStringAsync("/bwloomis").Result.Substring(0, 500));

                    // v3 - retry against flaky service
                    client = _httpClientFactory.CreateClient("flakyService"); // pull the registered client out of factory
                    Console.WriteLine(client.GetStringAsync("/weatherforecast?hitCount=2").Result);
                    
                    Console.WriteLine("now trying one that won't work");
                    //now = DateTime.Now;
                    //Console.WriteLine(client.GetStringAsync("/weatherforecast?hitCount=3").Result);
                    //done = DateTime.Now;
                    //TimeSpan elapsed = done - now;
                    //Console.WriteLine($"took {elapsed.TotalMilliseconds} ms");

                    // v4 circuit breaker against flaky service (retry twice)
                    client = _httpClientFactory.CreateClient("flakyServiceCB"); // pull the registered client out of factory
                    Polly.CircuitBreaker.AsyncCircuitBreakerPolicy cbPolicy = Policy
                        .Handle<Exception /* HttpRequestException */>()
                        .CircuitBreakerAsync(
                            exceptionsAllowedBeforeBreaking: 1,
                            durationOfBreak: TimeSpan.FromMinutes(10),  // if not a while, the breaker will reset before failures occur
                            (ex, t) =>
                            {
                                Console.WriteLine("*****************   Circuit broken    ***************************");
                            },
                            () =>
                            {
                                Console.WriteLine("Circuit reset");
                            });

                    Task<string> innerResponse;
                    // first try, succeeds
                    for(int i=1; i<=4; i++)
                    {
                        try
                        {
                            // first try passes in a 6 (succeeds), second try trips the breaker (with a 7), then others revert back to 6
                            int param = (i == 2) ? 7 : 6;
                            if (i == 4) cbPolicy.Reset();

                            // if you do not check CB status, and it is open, then this will not be executed
                            var response = cbPolicy.ExecuteAsync<string>(async () =>
                            {
                                innerResponse = client.GetStringAsync($"/weatherforecast?hitCount={param}");
                                if (innerResponse != null) Console.WriteLine($"innerResponse was: {innerResponse.Result}");
                                return ((innerResponse != null) ? innerResponse.Result : null);
                            });

                            Console.WriteLine($"Circuit breaker is {cbPolicy.CircuitState.ToString()}");
                            Console.WriteLine();
                        }
                        catch(HttpRequestException hre) // typically do these through handlers on the policy, not through try/catch 
                        {
                            // if you are securing an HTTP resource, you'll get a 503 error from time to time on our flaky service
                            Console.WriteLine($"An HTTP request exception occurred: {hre} {hre.InnerException}");
                        }
                        catch (Exception ex)
                        {
                            // this would be the other type of error, aggregate exception
                            Console.WriteLine($"An exception occurred: {ex} {ex.InnerException}");
                        }
                    }

                    // extra credit - add timeouts to calls... 
                }
                catch (Exception ex)
                {
                    done = DateTime.Now;
                    TimeSpan elapsed = done - now;
                    Console.WriteLine($"An error occurred: {ex} {ex.InnerException}");
                    Console.WriteLine($"took {elapsed.TotalMilliseconds} ms");
                }
            }

            Console.ReadLine();
            // host.RunAsync();
        }
                
    }
}
