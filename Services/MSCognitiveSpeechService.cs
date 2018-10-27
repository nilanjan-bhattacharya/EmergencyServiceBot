namespace EmergencyServicesBot
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading.Tasks;
    using System.Web;
    using System.Web.Configuration;
    using System.Web.Script.Serialization;
    using Microsoft.Bing.Speech;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    public class MicrosoftCognitiveSpeechService
    {
        private readonly string subscriptionKey;
        private readonly string speechRecognitionUri;

        public MicrosoftCognitiveSpeechService()
        {
            this.DefaultLocale = "en-US";
            this.subscriptionKey = WebConfigurationManager.AppSettings["MicrosoftSpeechApiKey"];
            this.speechRecognitionUri = Uri.UnescapeDataString(WebConfigurationManager.AppSettings["MicrosoftSpeechRecognitionUri"]);
        }

        public string DefaultLocale { get; set; }

        /// <summary>
        /// Gets text from an audio stream.
        /// </summary>
        /// <param name="audiostream"></param>
        /// <returns>Transcribed text. </returns>
        public async Task<string> GetTextFromAudioAsync(Stream audiostream)
        {
            var requestUri = this.speechRecognitionUri + Guid.NewGuid();

            using (var client = new HttpClient())
            {
                var token = Authentication.Instance.GetAccessToken();
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);

                try
                {
                    using (var binaryContent = new ByteArrayContent(StreamToBytes(audiostream)))
                    {
                        binaryContent.Headers.TryAddWithoutValidation("content-type", "audio/wav; codec=\"audio/pcm\"; samplerate=16000");
                        var response = await client.PostAsync(requestUri, binaryContent);
                        var responseString = await response.Content.ReadAsStringAsync();
                        dynamic data = JsonConvert.DeserializeObject(responseString);

                        if (data != null)
                        {
                            return data.header.name;
                        }
                        else
                        {
                            return string.Empty;
                        }
                    }
                }
                catch (Exception exp)
                {
                    Debug.WriteLine(exp);
                    return string.Empty;
                }
            }
        }

        /// <summary>
        /// Converts Stream into byte[].
        /// </summary>
        /// <param name="input">Input stream</param>
        /// <returns>Output byte[]</returns>
        private static byte[] StreamToBytes(Stream input)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                input.CopyTo(ms);
                return ms.ToArray();
            }
        }

        public static async Task<string> SendToLUISViaAPI(string query)
        {
            var client = new HttpClient();
            var queryString = HttpUtility.ParseQueryString(string.Empty);

            // This app ID is for a public sample app that recognizes requests to turn on and turn off lights
            var luisAppId = WebConfigurationManager.AppSettings["LuisAppId"];
            var subscriptionKey = WebConfigurationManager.AppSettings["LuisAPIKey"];

            // The request header contains your subscription key
            client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", subscriptionKey);

            // The "q" parameter contains the utterance to send to LUIS
            queryString["q"] = query;

            // These optional request parameters are set to their default values
            queryString["timezoneOffset"] = "0";
            queryString["verbose"] = "false";
            queryString["spellCheck"] = "false";
            queryString["staging"] = "false";
            ////https://westus.api.cognitive.microsoft.com/luis/v2.0/apps/
            var uri = "https://westus.api.cognitive.microsoft.com/luis/v2.0/apps/" + luisAppId + "?" + queryString;
            var response = await client.GetAsync(uri);

            var strResponseContent = await response.Content.ReadAsStringAsync();

           // JavaScriptSerializer json_serializer = new JavaScriptSerializer();
           // Intent intentRecieved =(Intent)json_serializer.DeserializeObject(strResponseContent);

            //JObject json = JObject.Parse(strResponseContent);
            //Intent intentRecieved = JsonConvert.DeserializeObject<Intent>(strResponseContent);

            // Display the JSON result from LUIS
            Console.WriteLine(strResponseContent);
            return strResponseContent;
        }
    }
}