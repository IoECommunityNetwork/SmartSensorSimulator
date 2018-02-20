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

            // Use observables to trigger a periodic 
            return
                Observable.Interval(new TimeSpan(0, 0, 5))
                    .Select(i =>
                                new BinSensorReading
                                {
                                    sesnorID = 99,
                                    binID = 666,
                                    binName = "Random Smart Sensor Simulator Module",
                                    binCategory = "Smart Sensor Simulator ",
                                    latitude = -33.869033,
                                    longitude = 151.208895,
                                    fillLevel = rand.Next(0,101),
                                    temperature = rand.Next(0,101),
                                    timestampdata = Convert.ToInt32(DateTimeOffset.Now.ToUnixTimeSeconds())
                                }
                    )
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
    }
}
