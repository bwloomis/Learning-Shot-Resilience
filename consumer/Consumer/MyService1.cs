using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Consumer
{
    public interface IMyService
    {
        Task<string> GetPage();
    }

    public class MyService1 : IMyService
    {
        public string URL { get; set; }

        private readonly IHttpClientFactory _clientFactory;

        public MyService1(IHttpClientFactory clientFactory)
        {
            _clientFactory = clientFactory;
        }

        public MyService1(IHttpClientFactory clientFactory, string url)
        {
            _clientFactory = clientFactory;
            URL = url;
        }

        public async Task<string> GetPage()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, URL);
            var client = _clientFactory.CreateClient();
            var response = await client.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync();
            }
            else
            {
                return $"StatusCode: {response.StatusCode}";
            }
        }
    }
}
