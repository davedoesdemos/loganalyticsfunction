using System;
using System.IO;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Web;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using System.Text;

namespace LogAnalyticsFunction
{
    public static class Function1
    {
        //https://docs.microsoft.com/en-us/rest/api/loganalytics/create-request
        [FunctionName("LogsBlobToLA")]
        public static void Run([BlobTrigger("logs/{name}", Connection = "storageString")]Stream myBlob, string name, ILogger log, ExecutionContext context)
        {
            //https://www.koskila.net/how-to-access-azure-function-apps-settings-from-c/
            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            var logAnalyticsURL = config["logAnalyticsURL"];
            var workspaceID = config["workspaceID"];

            String outputJSON = "[\n";
            log.LogInformation($"C# Blob trigger function Processed blob\n Name:{name} \n Size: {myBlob.Length} Bytes");
            using (var reader = new StreamReader(myBlob))
            {
                //write out the first line so we can include the comma at the start of following lines
                var line = reader.ReadLine();
                outputJSON = outputJSON + line;

                //Read the rest of the file
                while (!reader.EndOfStream)
                {
                    //Read our lines one by one and split into values
                    line = reader.ReadLine();
                    //Add the line to the output
                    outputJSON = outputJSON + ",\n" + line;
                }

                //Close the JSON
                outputJSON = outputJSON + "]";

                log.LogInformation("Added: " + outputJSON);
            }

            //upload the data

            //Get the current time
            //https://docs.microsoft.com/en-us/dotnet/api/system.datetime.now?view=netframework-4.8
            DateTime timestamp = DateTime.Now;

            //Set up variables with required date formats for current time
            //Note you may want to take the time generated from the actual log time, perhaps the time of the Blob creation, or a line in the file?
            //You may also want to decontruct the file into lines and send each one as a log event with it's correct time
            //https://docs.microsoft.com/en-us/dotnet/standard/base-types/standard-date-and-time-format-strings
            String xMSDate = timestamp.ToString("R");
            String timeGenerated = timestamp.ToString("O");

            //create the string for the signature then encode using Signature=Base64(HMAC-SHA256(UTF8(StringToSign)))
            String stringToSign = "POST" + "\n" + outputJSON.Length.ToString() + "\n" + "application/json" + "\nx-ms-date:" + xMSDate + "\n" + "/api/logs";
            HMACSHA256 hmac = new HMACSHA256(Encoding.UTF8.GetBytes(stringToSign));
            var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));

            using (var client = new HttpClient())
            using (var request = new HttpRequestMessage())
            {
                //Set the method to POST
                request.Method = HttpMethod.Post;
                //set the URI
                request.RequestUri = new Uri(logAnalyticsURL);

                //Add the authorization header Authorization: SharedKey <WorkspaceID>:<Signature>
                request.Headers.Add("Authorization", "SharedKey " + workspaceID + ":" + signature);
                //request.Headers.Add("Content-Type", "application/json");
                request.Headers.Add("Log-Type", "Lustydemo");
                request.Headers.Add("x-ms-date", xMSDate);
                request.Headers.Add("time-generated-field", timeGenerated);

                request.Properties.Add("CustomerID", workspaceID);
                request.Properties.Add("Resource", "/api/logs");
                request.Properties.Add("API Version", "2016-04-01");

                //Add the request body
                StringContent contentString = new StringContent(outputJSON);
                request.Content = contentString;

                //Send request, get response
                log.LogInformation(logAnalyticsURL);
                var response = client.SendAsync(request).Result;
                log.LogInformation(response.ToString());
            }
        }
    }
}
