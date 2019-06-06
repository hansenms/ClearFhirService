using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using EnsureThat;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Polly;

namespace ClearFhirService
{

    public class FhirAuthenticator
    {

        private static AuthenticationContext AuthContext { get; set; }
        private static ClientCredential ClientCredential { get; set; }
        private static String Audience { get; set; }

        public FhirAuthenticator(string authority,
                                 string clientId,
                                 string clientSecret,
                                 string audience)
        {
            AuthContext = new AuthenticationContext(authority);
            ClientCredential = new ClientCredential(clientId, clientSecret);
            Audience = audience;
        }

        public AuthenticationResult GetAuthenticationResult()
        {
            return AuthContext.AcquireTokenAsync(Audience, ClientCredential).Result;
        }
    }

    public class FhirQueryDeleter
    {

        private string ServerUrl { get; set; }
        private FhirAuthenticator FhirAuth { get; set; }
        private ActionBlock<string> QueryQueue { get; set; }

        public FhirQueryDeleter(string serverUrl, FhirAuthenticator fhirAuthenticator, int parallel = 4)
        {
            ServerUrl = serverUrl;
            FhirAuth = fhirAuthenticator;

            QueryQueue = new ActionBlock<string>(s => AppendQueryWorker(s),
                new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = parallel
                });
        }

        public void AppendQuery(string query)
        {
            QueryQueue.Post(query);
            QueryQueue.Completion.Wait();
        }

        public async Task AppendQueryWorker(string query)
        {
            var randomGenerator = new Random();
            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(ServerUrl);
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + FhirAuth.GetAuthenticationResult().AccessToken);

                HttpResponseMessage getResult = getResult = await client.GetAsync(query);

                JObject bundle = JObject.Parse(await getResult.Content.ReadAsStringAsync());
                JArray entries = (JArray)bundle["entry"];

                JArray links = (JArray)bundle["link"];
                string nextQuery = "";
                for (int i = 0; i < links.Count; i++)
                {
                    string link_type = (string)(bundle["link"][i]["relation"]);
                    string link_url = (string)(bundle["link"][i]["url"]);

                    if (link_type == "next")
                    {
                        Uri nextUri = new Uri(link_url);
                        nextQuery = nextUri.PathAndQuery;
                        break;
                    }
                }

                if (!String.IsNullOrEmpty(nextQuery))
                {
                    QueryQueue.Post(nextQuery);
                }

                if (entries != null)
                {
                    for (int i = 0; i < entries.Count; i++)
                    {
                        JObject resource = (JObject)entries[i]["resource"];
                        string resourceType = resource["resourceType"].ToString();
                        string resourceId = resource["id"].ToString();
                        string deleteQuery = $"/{resourceType}/{resourceId}?hardDelete=true";

                        var pollyDelays =
                            new[]
                            {
                                TimeSpan.FromMilliseconds(2000 + randomGenerator.Next(50)),
                                TimeSpan.FromMilliseconds(3000 + randomGenerator.Next(50)),
                                TimeSpan.FromMilliseconds(5000 + randomGenerator.Next(50)),
                                TimeSpan.FromMilliseconds(8000 + randomGenerator.Next(50))
                            };


                        HttpResponseMessage deleteResult = await Policy
                            .HandleResult<HttpResponseMessage>(response => !response.IsSuccessStatusCode)
                            .WaitAndRetryAsync(pollyDelays, (result, timeSpan, retryCount, context) =>
                            {
                                Console.WriteLine($"Request failed with {result.Result.StatusCode}. Waiting {timeSpan} before next retry. Retry attempt {retryCount}");
                            })
                            .ExecuteAsync(() =>
                            {
                                return client.DeleteAsync(deleteQuery);
                            });

                        if (!deleteResult.IsSuccessStatusCode)
                        {
                            Console.WriteLine("Delete failed: " + deleteQuery);
                            throw new Exception("Failed to delete resource");
                        }
                        else
                        {
                            Console.WriteLine("DELETE " + deleteQuery);
                        }
                    }
                }

                if (String.IsNullOrEmpty(nextQuery))
                {
                    QueryQueue.Complete();
                }
            }
        }
    }

    class Program
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private static IConfiguration _configuration;

        static void Main(string[] args)
        {

            ConfigurationBuilder configurationBuilder = new ConfigurationBuilder();
            _configuration = configurationBuilder.AddCommandLine(args).AddEnvironmentVariables().Build();

            Ensure.That(_configuration["FhirServerUrl"]).IsNotNullOrWhiteSpace();
            Ensure.That(_configuration["Authority"]).IsNotNullOrWhiteSpace();
            Ensure.That(_configuration["ClientId"]).IsNotNullOrWhiteSpace();
            Ensure.That(_configuration["ClientSecret"]).IsNotNullOrWhiteSpace();

            try
            {

                string authority = _configuration["Authority"];
                string audience = string.IsNullOrEmpty(_configuration["Audience"]) ? _configuration["FhirServerUrl"] : _configuration["Audience"];
                string clientId = _configuration["ClientId"];
                string clientSecret = _configuration["ClientSecret"];

                FhirAuthenticator auth = new FhirAuthenticator(authority, clientId, clientSecret, audience);
                FhirQueryDeleter deleter = new FhirQueryDeleter(_configuration["FhirServerUrl"], auth);
                deleter.AppendQuery("/");
            }
            catch (Exception)
            {
                Console.WriteLine("Failed to run export");
                throw;
            }

        }
    }
}
