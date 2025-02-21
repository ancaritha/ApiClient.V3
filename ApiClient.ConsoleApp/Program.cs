//-----------------------------------------------------------------------
//
// THE SOFTWARE IS PROVIDED "AS IS" WITHOUT ANY WARRANTIES OF ANY KIND, EXPRESS, IMPLIED, STATUTORY, 
// OR OTHERWISE. EXPECT TO THE EXTENT PROHIBITED BY APPLICABLE LAW, DIGI-KEY DISCLAIMS ALL WARRANTIES, 
// INCLUDING, WITHOUT LIMITATION, ANY IMPLIED WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE, 
// SATISFACTORY QUALITY, TITLE, NON-INFRINGEMENT, QUIET ENJOYMENT, 
// AND WARRANTIES ARISING OUT OF ANY COURSE OF DEALING OR USAGE OF TRADE. 
// 
// DIGI-KEY DOES NOT WARRANT THAT THE SOFTWARE WILL FUNCTION AS DESCRIBED, 
// WILL BE UNINTERRUPTED OR ERROR-FREE, OR FREE OF HARMFUL COMPONENTS.
// 
//-----------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ApiClient.Models;
using ApiClient.OAuth2;
using Common.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ApiClient.ConsoleApp
{
    public class Program
    {
        private static readonly ILog _log = LogManager.GetLogger(typeof(Program));

        static void Main()
        {
            var prog = new Program();

            prog.CallKeywordSearch();

            // This will keep the console window up until a key is pressed in the console window.
            Console.WriteLine("\n\nPress any key to exit...");
            Console.ReadKey();
        }

        private async Task CallKeywordSearch()
        {
            var settings = ApiClientSettings.CreateFromConfigFile();
            _log.DebugFormat(settings.ToString());
            Console.WriteLine(settings.ToString());
            try
            {
                if (settings.ExpirationDateTime < DateTime.Now)
                {
                    // Let's refresh the token
                    var oAuth2Service = new OAuth2Service(settings);
                    var oAuth2AccessToken = await oAuth2Service.RefreshTokenAsync();
                    if (oAuth2AccessToken.IsError)
                    {
                        // Current Refresh token is invalid or expired 
                        _log.DebugFormat("Current Refresh token is invalid or expired ");
                        Console.WriteLine("Current Refresh token is invalid or expired ");
                        return;
                    }

                    settings.UpdateAndSave(oAuth2AccessToken);

                    _log.DebugFormat("After call to refresh");
                    _log.DebugFormat(settings.ToString());

                    Console.WriteLine("After call to refresh");
                    Console.WriteLine(settings.ToString());
                }

                var client = new ApiClientService(settings);
                //var response = await client.KeywordSearch("P5555-ND");
                //var response = await client.KeywordSearch("MX25L6406EMI-12G");
                var response = await client.ProductDetailsQuery("MX25L6406EMI-12G");
                //var response = await client.ProductDetailsQuery("P5555-ND");
                //var response = await client.BatchSearch();


                // In order to pretty print the json object we need to do the following
                var jsonFormatted = JToken.Parse(response).ToString(Formatting.Indented);

                Console.WriteLine($"Reponse is {jsonFormatted} ");
            }
            catch (System.Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
    }
}
