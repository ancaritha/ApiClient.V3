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
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;
using ApiClient.Constants;
using ApiClient.Exception;
using ApiClient.Models;
using ApiClient.OAuth2;
using Common.Logging;

namespace ApiClient
{
    public class ApiClientService
    {
        private const string CustomHeader = "Api-StaleTokenRetry";
        private static readonly ILog _log = LogManager.GetLogger(typeof(ApiClientService));

        private ApiClientSettings _clientSettings;

        public ApiClientSettings ClientSettings
        {
            get => _clientSettings;
            set => _clientSettings = value;
        }

        /// <summary>
        ///     The httpclient which will be used for the api calls through the this instance
        /// </summary>
        public HttpClient HttpClient { get; private set; }

        public ApiClientService(ApiClientSettings clientSettings)
        {
            ClientSettings = clientSettings ?? throw new ArgumentNullException(nameof(clientSettings));
            Initialize();
        }

        private void Initialize()
        {
            HttpClient = new HttpClient();

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            var authenticationHeaderValue = new AuthenticationHeaderValue("Bearer", ClientSettings.AccessToken);
            HttpClient.DefaultRequestHeaders.Authorization = authenticationHeaderValue;

            HttpClient.DefaultRequestHeaders.Add("X-Digikey-Client-Id", ClientSettings.ClientId);
            HttpClient.BaseAddress = DigiKeyUriConstants.BaseAddress;
            HttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public async Task ResetExpiredAccessTokenIfNeeded()
        {
            if (_clientSettings.ExpirationDateTime < DateTime.Now)
            {
                // Let's refresh the token
                var oAuth2Service = new OAuth2Service(_clientSettings);
                var oAuth2AccessToken = await oAuth2Service.RefreshTokenAsync();
                if (oAuth2AccessToken.IsError)
                {
                    // Current Refresh token is invalid or expired 
                    Console.WriteLine("Current Refresh token is invalid or expired ");
                    return;
                }

                // Update the clientSettings
                _clientSettings.UpdateAndSave(oAuth2AccessToken);
                Console.WriteLine("ApiClientService::CheckifAccessTokenIsExpired() call to refresh");
                Console.WriteLine(_clientSettings.ToString());

                // Reset the Authorization header value with the new access token.
                var authenticationHeaderValue = new AuthenticationHeaderValue("Bearer", _clientSettings.AccessToken);
                HttpClient.DefaultRequestHeaders.Authorization = authenticationHeaderValue;
            }
        }

        public async Task<string> ProductDetailsQuery(string keyword)
        {
            var resourcePath = "/Search/v3/Products/MX25L6406EMI-12G?includes=DigiKeyPartNumber%2CQuantityAvailable%2C,Obsolete";

            var request1 = new ProductDetailsRequest
            {
                DigiKeyPartNumber = keyword,
                QuantityAvailable = 1
            };

            await ResetExpiredAccessTokenIfNeeded();
            var postResponse = await GetAsJsonAsync(resourcePath, request1);

            return GetServiceResponse(postResponse).Result;
        }

        public async Task<string> KeywordSearch(string keyword)
        {
            var resourcePath = "/Search/v3/Products/Keyword";

            var request = new KeywordSearchRequest
            {
                Keywords = keyword ?? "P5555-ND",
                RecordCount = 25
            };

            await ResetExpiredAccessTokenIfNeeded();
            var postResponse = await PostAsJsonAsync(resourcePath, request);

            return GetServiceResponse(postResponse).Result;
        }

        public async Task<string> BatchSearch()
        {
            var resourcePath = "/BatchSearch/v3/ProductDetails?excludeMarketPlaceProducts=true";

            var testString = "{\"Products\": [\"P5555-ND\",\"MX25L25645GMI-10G-ND\"]}";

            await ResetExpiredAccessTokenIfNeeded();
            var postResponse = await PostAsJsonAsync(resourcePath, testString);

            return GetServiceResponse(postResponse).Result;
        }

        public async Task<HttpResponseMessage> PostAsJsonAsync<T>(string resourcePath, T postRequest)
        {
            _log.DebugFormat(">ApiClientService::PostAsJsonAsync()");
            try
            {
                var response = await HttpClient.PostAsJsonAsync(resourcePath, postRequest);
                _log.DebugFormat("<ApiClientService::PostAsJsonAsync()");

                //Unauthorized, then there is a chance token is stale
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    var responseBody = await response.Content.ReadAsStringAsync();

                    if (OAuth2Helpers.IsTokenStale(responseBody))
                    {
                        _log.DebugFormat(
                            $"Stale access token detected ({_clientSettings.AccessToken}. Calling RefreshTokenAsync to refresh it");
                        await OAuth2Helpers.RefreshTokenAsync(_clientSettings);
                        _log.DebugFormat($"New Access token is {_clientSettings.AccessToken}");

                        //Only retry the first time.
                        if (!response.RequestMessage.Headers.Contains(CustomHeader))
                        {
                            HttpClient.DefaultRequestHeaders.Add(CustomHeader, CustomHeader);
                            HttpClient.DefaultRequestHeaders.Authorization =
                                new AuthenticationHeaderValue("Authorization", _clientSettings.AccessToken);
                            return await PostAsJsonAsync(resourcePath, postRequest);
                        }
                        else if (response.RequestMessage.Headers.Contains(CustomHeader))
                        {
                            throw new ApiException($"Inside method {nameof(PostAsJsonAsync)} we received an unexpected stale token response - during the retry for a call whose token we just refreshed {response.StatusCode}");
                        }
                    }
                }

                return response;
            }
            catch (HttpRequestException hre)
            {
                _log.DebugFormat($"PostAsJsonAsync<T>: HttpRequestException is {hre.Message}");
                throw;
            }
            catch (ApiException dae)
            {
                _log.DebugFormat($"PostAsJsonAsync<T>: ApiException is {dae.Message}");
                throw;
            }
        }

        public async Task<HttpResponseMessage> GetAsJsonAsync<T>(string resourcePath, T postRequest)
        {
            _log.DebugFormat(">ApiClientService::PostAsJsonAsync()");
            try
            {
                var response = await HttpClient.GetAsync(resourcePath);
                _log.DebugFormat("<ApiClientService::GetAsync()");

                //Unauthorized, then there is a chance token is stale
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    var responseBody = await response.Content.ReadAsStringAsync();

                    if (OAuth2Helpers.IsTokenStale(responseBody))
                    {
                        _log.DebugFormat(
                            $"Stale access token detected ({_clientSettings.AccessToken}. Calling RefreshTokenAsync to refresh it");
                        await OAuth2Helpers.RefreshTokenAsync(_clientSettings);
                        _log.DebugFormat($"New Access token is {_clientSettings.AccessToken}");

                        //Only retry the first time.
                        if (!response.RequestMessage.Headers.Contains(CustomHeader))
                        {
                            HttpClient.DefaultRequestHeaders.Add(CustomHeader, CustomHeader);
                            HttpClient.DefaultRequestHeaders.Authorization =
                                new AuthenticationHeaderValue("Authorization", _clientSettings.AccessToken);
                            return await GetAsJsonAsync(resourcePath, postRequest);
                        }
                        else if (response.RequestMessage.Headers.Contains(CustomHeader))
                        {
                            throw new ApiException($"Inside method {nameof(PostAsJsonAsync)} we received an unexpected stale token response - during the retry for a call whose token we just refreshed {response.StatusCode}");
                        }
                    }
                }

                return response;
            }
            catch (HttpRequestException hre)
            {
                _log.DebugFormat($"PostAsJsonAsync<T>: HttpRequestException is {hre.Message}");
                throw;
            }
            catch (ApiException dae)
            {
                _log.DebugFormat($"PostAsJsonAsync<T>: ApiException is {dae.Message}");
                throw;
            }
        }


        

        protected async Task<string> GetServiceResponse(HttpResponseMessage response)
        {
            _log.DebugFormat(">ApiClientService::GetServiceResponse()");
            var postResponse = string.Empty;
            var postHeader = string.Empty;

            if (response.IsSuccessStatusCode)
            {
                if (response.Content != null)
                {

                    string[] LimitRemaining = (string[])response.Headers.GetValues("X-RateLimit-Remaining");

                    Console.WriteLine("LimitRemaining: {0}", LimitRemaining);

                    //postHeader = await response.Headers
                    postResponse = await response.Content.ReadAsStringAsync();
                }
            }
            else
            {
                var errorMessage = await response.Content.ReadAsStringAsync();
                Console.WriteLine("Response");
                Console.WriteLine("  Status Code : {0}", response.StatusCode);
                Console.WriteLine("  Content     : {0}", errorMessage);
                Console.WriteLine("  Reason      : {0}", response.ReasonPhrase);
                var resp = new HttpResponseMessage(response.StatusCode)
                {
                    Content = response.Content,
                    ReasonPhrase = response.ReasonPhrase
                };
                throw new HttpResponseException(resp);
            }

            _log.DebugFormat("<ApiClientService::GetServiceResponse()");
            return postResponse;
        }
    }
}
