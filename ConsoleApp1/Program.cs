using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Dasync.Collections;
using Polly;
using Polly.Timeout;
using PowerArgs;
using Serilog;
using Serilog.Events;

namespace ConsoleApp1
{
    class Program
    {
        static int _produceIdx;
        static BlockingCollection< string> _process = new BlockingCollection<string>(1000);
        private static MyArgs myArgs;
        private static System.Timers.Timer timer=new System.Timers.Timer();
        static async Task Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.RollingFile($"{DateTime.Now:yyyyMMddHHmmss}.txt",LogEventLevel.Information)
                .WriteTo.Console()
                .CreateLogger();
            try
            {
                myArgs = Args.Parse<MyArgs>(args);
                if (myArgs == null) return;
                if (myArgs.Help) return;
                if (myArgs.Urls==null&&myArgs.Hosts==null)
                {
                    return;
                }
            }
            catch (ArgException ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ArgUsage.GenerateUsageFromTemplate<MyArgs>());
            }
            timer.Interval = 1000;
            timer.Enabled = true;
            timer.Elapsed += (o, eventArgs) =>
            {
                Log.Debug("TEST: " + _produceIdx);
            };
            timer.Start();
            try
            {
                var tasks = Produce();
                var task = Customer();
                foreach (var task1 in tasks)
                {
                    await task1;
                }
                _process.CompleteAdding();
                await task;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            timer.Stop();
            Console.WriteLine("Finished");
            Console.Read();
        }


        static List<Task> Produce()
        {
            List<Task> tasks=new List<Task>();
            if (myArgs.Urls != null)
            {
                var args = myArgs.Urls.Split(',');
                var t= Task.Run(() =>
                {
                    foreach (var arg in args)
                    {
                        if (arg.StartsWith("file:/"))
                        {
                            var filepath = arg[6..];
                            Console.WriteLine(filepath);
                            if (File.Exists(filepath))
                            {
                                var toBeAdd = File.ReadAllLines(filepath);
                                foreach (string url in toBeAdd)
                                {
                                    _process.Add(url);
                                }
                            }
                        }
                        else
                        {
                            _process.Add(arg);
                        }
                    }
                });
                tasks.Add(t);
            }
            if (myArgs.Hosts!=null&&myArgs.Ports!=null)
            {
                List<string> portList=new List<string>();
                var ports = myArgs.Ports.Split(',');
                foreach (var port in ports)
                {
                    if (port.StartsWith("file:/"))
                    {
                        var filepath = port[6..];
                        Console.WriteLine(filepath);
                        if (File.Exists(filepath))
                        {
                            var toBeAdd = File.ReadAllLines(filepath);
                            foreach (string url in toBeAdd)
                            {
                                portList.Add(url);
                            }
                        }
                    }
                    else if (port.Contains("-"))
                    {
                        var subPorts = port.Split('-');
                        if (subPorts.Length==2)
                        {
                            var subMinPortBool = int.TryParse(subPorts[0], out var subMinPort);
                            var subMaxPortBool = int.TryParse(subPorts[1], out var subMaxPort);
                            if (subMinPortBool&&subMaxPortBool&&subMinPort<=subMaxPort)
                            {
                                for (int i = subMinPort; i <= subMaxPort; i++)
                                {
                                    portList.Add(i.ToString());
                                }
                            }
                        }
                    }
                    else
                    {
                        portList.Add(port);   
                    }
                }

                var t = Task.Run(() =>
                {
                    var hosts = myArgs.Hosts.Split(',');
                    foreach (var host in hosts)
                    {
                        if (host.StartsWith("file:/"))
                        {
                            var filepath = host[6..];
                            Console.WriteLine(filepath);
                            if (File.Exists(filepath))
                            {
                                var toBeAdd = File.ReadAllLines(filepath);
                                foreach (string url in toBeAdd)
                                {
                                    foreach (string port in portList)
                                    {
                                        _process.Add($"{url}:{port}");
                                    }
                                }
                            }
                        }
                        else if (host.Contains("-"))
                        {
                            var subHosts = host.Split('-');
                            if (subHosts.Length == 2)
                            {
                                
                                var subMinHostIpAddressBool=IPAddress.TryParse(subHosts[0], out var subMinHostIpAddress);
                                var subMaxHostIpAddressBool = IPAddress.TryParse(subHosts[1], out var subMaxHostIpAddress);
                                if (subMaxHostIpAddressBool&&subMinHostIpAddressBool)
                                {
                                    var subMinHostBool = IPNetwork.TryToBigInteger(subMinHostIpAddress, out var minInteger);
                                    var subMaxHostBool = IPNetwork.TryToBigInteger(subMaxHostIpAddress, out var maxInteger);
                                    if (subMinHostBool&& subMaxHostBool&& minInteger<=maxInteger)
                                    {
                                        for (BigInteger i = (BigInteger) minInteger; i <= maxInteger; i++)
                                        {
                                            foreach (string port in portList)
                                            {
                                                _process.Add($"{i.ToIp()}:{port}");
                                            }

                                        }
                                    }

                                }


                              
  
                               
                            }
                        }
                        else if (host.Contains("/"))
                        {
                                var ipBool = IPNetwork.TryParse(host, out var ip);
                                if (ipBool)
                                {
                                    foreach (IPAddress address in ip.ListIPAddress())
                                    {
                                        foreach (string port in portList)
                                        {
                                            _process.Add($"{address.MapToIPv4()}:{port}");
                                        }
                                    }
                                }

                        }
                        else
                        {
                            foreach (string port in portList)
                            {
                                _process.Add($"{host}:{port}");
                            }
                        }
                    }
                });
                tasks.Add(t);
            }
            return tasks;


        }
        static Task Customer()
        {
            #region http
            HttpClientHandler clientHandler = new HttpClientHandler();
            clientHandler.AutomaticDecompression = DecompressionMethods.All;
            clientHandler.AllowAutoRedirect = true;
            clientHandler.ServerCertificateCustomValidationCallback += (a, b, c, d) => true;
            HttpClient client = new HttpClient(clientHandler);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.1)");
            var customer= _process.GetConsumingEnumerable().ParallelForEachAsync(async s =>
            {
                Interlocked.Increment(ref _produceIdx);
                var fallbackPolicyStream = Policy<HttpResponseMessage>.Handle<Exception>()
                    .FallbackAsync(
                        async (result, ctx, token) => await Task.FromResult<HttpResponseMessage>(null),
                        (result, ctx) => Task.CompletedTask);
                var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(myArgs.TimeoutSeconds, TimeoutStrategy.Pessimistic);
                var warpStream = Policy.WrapAsync(fallbackPolicyStream, timeoutPolicy);
                Context policyContext = new Context();
                policyContext["url"] = s;
                var res = await warpStream.ExecuteAsync(async ctx => await client.GetAsync("http://" + ctx["url"]), policyContext);
                if (res != null)
                {
                    var resBytes = await res.Content.ReadAsByteArrayAsync();
                    if (resBytes.Length == 0) resBytes = new byte[0];
                    var detector = UtfUnknown.CharsetDetector.DetectFromBytes(resBytes);
                    Encoding enc = Encoding.UTF8;
                    if (detector.Detected != null && detector.Detected.Confidence < 0.8 && detector.Detected.EncodingName != "utf-8") enc = Encoding.GetEncoding(936);
                    Log.Information($"http://{s.PadRight(25)}{Convert.ToInt32(res.StatusCode).ToString().PadRight(5)}{GetTitle(enc.GetString(resBytes))}");

                }

                var res2 = await warpStream.ExecuteAsync(async ctx => await client.GetAsync("https://" + ctx["url"]), policyContext);
                if (res2 == null) return;
                var resBytes2 = await res2.Content.ReadAsByteArrayAsync();
                if (resBytes2.Length == 0) resBytes2 = new byte[0];
                var detector2 = UtfUnknown.CharsetDetector.DetectFromBytes(resBytes2);
                var enc2 = Encoding.UTF8;
                if (detector2.Detected != null && detector2.Detected.Confidence < 0.8 && detector2.Detected.EncodingName != "utf-8") enc2 = Encoding.GetEncoding(936);
                Log.Information($"https://{s.PadRight(24)}{Convert.ToInt32(res2.StatusCode).ToString().PadRight(5)}{GetTitle(enc2.GetString(resBytes2))}");


            }, myArgs.Threads);
           
            return customer;

            #endregion
        }

        static string GetTitle(string html)
        {
            var m= Regex.Match(html, "title>(?<title>.*?)</title", RegexOptions.IgnoreCase);
            return m.Success ? HttpUtility.HtmlDecode(m.Groups["title"].Value).Replace('\u00A0','\u0020') : null;
        }
    }
    public class MyArgs
    {
        [HelpHook, ArgShortcut("-?"), ArgDescription("Shows this help")]
        public bool Help { get; set; }

        [ArgRequired(PromptIfMissing = true), ArgDescription("<null>,192.168.0.1:80,192.168.0.1:81,127.0.0.1,file:/c:\\example.txt")]
        public string Urls { get; set; }

        [ArgRequired(PromptIfMissing = true),ArgDescription("<null>,192.168.0.1/24,127.0.0.1,file:/c:\\example.txt")]
        public string Hosts { get; set; }

        [ArgRequired(PromptIfMissing = true), ArgDescription("<null>,80-90,88,999,file:/c:\\example.txt")]
        public string Ports { get; set; }

        [ArgRequired(PromptIfMissing = true), ArgDescription("seconds:3")]
        public int TimeoutSeconds { get; set; }
        [ArgRequired(PromptIfMissing = true), ArgDescription("Threads:8")]
        public int Threads { get; set; }
    }
}
