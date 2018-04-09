namespace SmartSensorSimulator
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Runtime.Loader;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Client.Transport.Mqtt;
    using System.Collections.Generic;
    using Microsoft.Azure.Devices.Shared;
    using Newtonsoft.Json;
    using System.Reactive;
    using System.Reactive.Linq;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;

    class Program
    {
        static readonly Random rand = new Random();

        static void Main(string[] args)
        {
            // The Edge runtime gives us the connection string we need -- it is injected as an environment variable
            string connectionString = Environment.GetEnvironmentVariable("EdgeHubConnectionString");

            // Cert verification is not yet fully functional when using Windows OS for the container
            bool bypassCertVerification = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            if (!bypassCertVerification) InstallCert();
            Init(connectionString, bypassCertVerification).Wait();

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
            WhenCancelled(cts.Token).Wait();
        }

        /// <summary>
        /// Handles cleanup operations when app is cancelled or unloads
        /// </summary>
        public static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        /// <summary>
        /// Add certificate in local cert store for use by client for secure connection to IoT Edge runtime
        /// </summary>
        static void InstallCert()
        {
            string certPath = Environment.GetEnvironmentVariable("EdgeModuleCACertificateFile");
            if (string.IsNullOrWhiteSpace(certPath))
            {
                // We cannot proceed further without a proper cert file
                Console.WriteLine($"Missing path to certificate collection file: {certPath}");
                throw new InvalidOperationException("Missing path to certificate file.");
            }
            else if (!File.Exists(certPath))
            {
                // We cannot proceed further without a proper cert file
                Console.WriteLine($"Missing path to certificate collection file: {certPath}");
                throw new InvalidOperationException("Missing certificate file.");
            }
            X509Store store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(new X509Certificate2(X509Certificate2.CreateFromCertFile(certPath)));
            Console.WriteLine("Added Cert: " + certPath);
            store.Close();
        }


        /// <summary>
        /// Initializes the DeviceClient and sets up the callback to receive
        /// messages containing temperature information
        /// </summary>
        static async Task<IDisposable> Init(string connectionString, bool bypassCertVerification = false)
        {
            Console.WriteLine("Connection String {0}", connectionString);

            MqttTransportSettings mqttSetting = new MqttTransportSettings(TransportType.Mqtt_Tcp_Only);
            // During dev you might want to bypass the cert verification. It is highly recommended to verify certs systematically in production
            if (bypassCertVerification)
            {
                mqttSetting.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
            }
            ITransportSettings[] settings = { mqttSetting };

            // Open a connection to the Edge runtime
            DeviceClient ioTHubModuleClient = DeviceClient.CreateFromConnectionString(connectionString, settings);
            await ioTHubModuleClient.OpenAsync();
            Console.WriteLine("IoT Hub module client initialized.");

            // Read sample sensor data for device 38 from file
            SensorData data = GetSensorData(38); // TODO can we refactor sensor or device ID somehow? module twin or plain configuration file

            int interval = 86400; // TODO

            // Source observables for sample device data
            var source = Observable
                            .Interval(TimeSpan.FromSeconds(1)) // TODO refactor timespan into module twin configuration
                            // .Select(i => data.message.lists[i % data.message.lists.Length]);
                            .SelectMany(async idx => 
                            {
                                return await FetchSensorDataAsync(38, DateTimeOffset.FromUnixTimeSeconds(1515890278 + idx * interval), new TimeSpan(0, 0, interval));
                            })
                            .SelectMany(_ => _); // flatten from List<SensorDataEntry> to SensorDataEntry;

            // React to stream of SensorData readings from the API or file
            return
                source
                    .Select(_ => 
                                new
                                {
                                    detail = GetSensorDetail(_.sensorallocatedID).message,
                                    data = _
                                })
                    .Select(_ => 
                                new BinSensorReading
                                {
                                    sesnorID = _.detail.sensorsID, // 98,
                                    binID = _.detail.currentPinAllocated.projectpinID, // 667,
                                    binName = _.detail.currentPinAllocated.name, // "Random Smart Sensor Simulator Module",
                                    binCategory = _.detail.currentPinAllocated.pinType.pinTypeName, // "Smart Sensor Simulator ",
                                    latitude = _.detail.currentPinAllocated.latitude, // -33.869033,
                                    longitude = _.detail.currentPinAllocated.longitude, //151.208895,
                                    fillLevel = CalculateFillLevel(_.detail.currentPinAllocated.pinType.depthWhenEmpty_cm,  _.detail.currentPinAllocated.pinType.distanceSensorToFillLine_cm, _.data.ultrasound), // TODO fillLevel can sometimes com out as -ve due to ultrsound reading, maybe filter?
                                    temperature = _.data.temperatureValue,
                                    timestampdata = _.data.timestampdata
                                })
                    .Do(_ => Console.WriteLine(JsonConvert.SerializeObject(_))) // DEBUG
                    .Select(JsonConvert.SerializeObject)
                    .Select(messageString => Encoding.UTF8.GetBytes(messageString))
                    .Select(messageBytes => new Message(messageBytes))
                    .SelectMany(async message =>
                    {
                        await ioTHubModuleClient.SendEventAsync("DataOutput", message);
                        return Unit.Default;
                    })
                    .Subscribe(_ => Console.WriteLine($"{DateTime.Now} - Sent message"),
                            ex =>
                            {
                                Console.WriteLine(DateTime.Now + " - Error " + ex.Message);
                                Console.WriteLine(ex.ToString());
                                Console.WriteLine();
                            });
        }

        private static SensorDetail GetSensorDetail(int sensorId)
        {
            using (StreamReader reader = new StreamReader(String.Format("data/sensorDetail.{0}.json", sensorId)))
            {
                return JsonConvert.DeserializeObject<SensorDetail>(reader.ReadToEnd());
            }
        }

        private static SensorData GetSensorData(int sensorId)
        {
            using (StreamReader reader = new StreamReader(String.Format("data/sensorData.{0}.json", sensorId)))
            {
                return JsonConvert.DeserializeObject<SensorData>(reader.ReadToEnd());
            }
            
        }

        private static async Task<List<SensorDataEntry>> FetchSensorDataAsync(int sensorId, DateTimeOffset from, TimeSpan duration)
        {
            long fromUnixTime = from.ToUnixTimeSeconds();
            long toUnixTime = fromUnixTime + Convert.ToInt64(duration.TotalSeconds);

            string baseUrl = "https://dashboard.smartsensor.com.au/";
            string rawSensorDataUrl = $"api/sensors/rawsensordata/{sensorId}?dmin={fromUnixTime}&dmax={toUnixTime}";

            using (var client = new HttpClient())
            {
                string url = baseUrl + rawSensorDataUrl;
                var body = new
                {
                    deviceID = "ABCD-EFGH-IJKL-MNOP",
                    token = "c3eeaab10e3c93f6c9da95027c14a283"
                };
                HttpResponseMessage response = await client.PostAsync(url, new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json"));
                // response.EnsureSuccessStatusCode();
                if (response.IsSuccessStatusCode)
                {
                    string result = await response.Content.ReadAsStringAsync();
                    var rootJson = JsonConvert.DeserializeObject<SensorData>(result);
                    var arr = rootJson.message.lists;
                    var lst = arr.OfType<SensorDataEntry>().ToList();
                    // Console.WriteLine($"API call returned {lst.Count} entries");
                    return lst;
                }
                else
                {
                    return new List<SensorDataEntry>();
                }
            }
        }

        private static int CalculateFillLevel(int depthWhenEmpty, int distanceToFillLine, int ultrasound)
        {
            return Convert.ToInt32(
                Math.Round(
                    Convert.ToDecimal(depthWhenEmpty + distanceToFillLine - ultrasound)
                    /
                    Convert.ToDecimal(depthWhenEmpty) 
                    * 
                    100)
                );
        }
    }
}
