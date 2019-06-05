using System;
using System.Net.Http;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Newtonsoft.Json.Linq;

namespace ClearFhirService
{
    class Program
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private static IConfiguration _configuration;

        static async Task Main(string[] args)
        {

            ConfigurationBuilder configurationBuilder = new ConfigurationBuilder();
            _configuration = configurationBuilder.AddCommandLine(args).AddEnvironmentVariables().Build();

            Ensure.That(_configuration["FhirServerUrl"]).IsNotNullOrWhiteSpace();
            Ensure.That(_configuration["Authority"]).IsNotNullOrWhiteSpace();
            Ensure.That(_configuration["ClientId"]).IsNotNullOrWhiteSpace();
            Ensure.That(_configuration["ClientSecret"]).IsNotNullOrWhiteSpace();
            //Ensure.That(_configuration["Audience"]).IsNotNullOrWhiteSpace();

            try
            {
                AuthenticationContext authContext;
                ClientCredential clientCredential;
                AuthenticationResult authResult;

                Uri fhirServerUrl = new Uri(_configuration["FhirServerUrl"]);
                string authority = _configuration["Authority"];
                string audience = string.IsNullOrEmpty(_configuration["Audience"]) ? _configuration["FhirServerUrl"] : _configuration["Audience"];
                string clientId = _configuration["ClientId"];
                string clientSecret = _configuration["ClientSecret"];


                

            }
            catch (Exception e)
            {
                throw;
            }

        }
    }
}
