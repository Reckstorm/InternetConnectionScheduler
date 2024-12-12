using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace InternetConnectionScheduler
{
    public class Worker : BackgroundService
    {
        private object _lock { get; set; }
        private bool _running;

        public bool Running
        {
            get { return _running; }
            set
            {
                lock (_lock)
                {
                    _running = value;
                }
            }
        }

        private Rule _rule { get; set; }

        public Worker()
        {
            CreateRulesIfMissing();
            _lock = new object();
            _rule = JsonSerializer.Deserialize<Rule>(GetRules().Result);
            CheckIfBGRunning();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) => RunBlock();

        private async Task<string> GetRules()
        {
            string rule = JsonSerializer.Serialize(new Rule() { Start = TimeOnly.MinValue, End = TimeOnly.MinValue });
            try
            {
                await Task.Run(() =>
                {
                    if (!File.Exists("Rule.txt")) return;
                    string text = File.ReadAllText("Rule.txt");
                    if (!string.IsNullOrEmpty(text))
                        rule = text;
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            return rule;
        }

        private void CheckIfBGRunning()
        {
            if (_rule == null) return;
            Process temp = Process.GetProcesses().FirstOrDefault(p => p.ProcessName.Equals(AppDomain.CurrentDomain.FriendlyName));
            if (temp != null && temp.Id != Environment.ProcessId) temp.Kill();
        }

        public void RunBlock()
        {
            if (!Running)
            {
                Running = true;
                RestartIfRulesChanged();
                Task.Run(async () =>
                {
                    while (Running)
                    {
                        Thread.Sleep(200);
                        TimeOnly now = TimeOnly.Parse(DateTime.Now.ToLongTimeString());
                        if (_rule.Start == _rule.End) continue;
                        try
                        { 
                            //NETSH
                            if (NetworkInterface.GetIsNetworkAvailable() && CheckTime(_rule, now))
                            {
                                var interfaces = GetAllInterfaces();
                                foreach (var iface in interfaces)
                                {
                                    Disable(iface);
                                }
                            }
                            else if (!NetworkInterface.GetIsNetworkAvailable() && !CheckTime(_rule, now))
                            {
                                var interfaces = GetAllInterfaces();
                                foreach (var iface in interfaces)
                                {
                                    Enable(iface);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Error: " + ex.Message);
                        }
                    }
                });
            }
        }

        private Process CreateProcess(string name, string arg)
        {
            Process process = new Process();
            process.StartInfo.FileName = name;
            process.StartInfo.Arguments = arg;
            process.StartInfo.Verb = "runas";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            return process;
        }

        private void Enable(string ifaceName)
        {
            var process = CreateProcess("netsh", "interface set interface \"" + ifaceName + "\" enable");
            process.Start();
            process.WaitForExit();
        }

        private void Disable(string ifaceName)
        {
            var process = CreateProcess("netsh", "interface set interface \"" + ifaceName + "\" disable");
            process.Start();
            Console.WriteLine(process.StandardOutput.ReadToEnd());
            process.WaitForExit();
        }

        private List<string> GetAllInterfaces()
        {
            List<string> interfaces = new List<string>();

            var process = CreateProcess("netsh", "interface show interface");
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            string[] lines = output.Split('\n');

            foreach (var line in lines)
            {
                if (!line.Contains("State") && line.Length > 0)
                {
                    string[] columns = Regex.Split(line.Trim(), @"\s{2,}");
                    if (columns.Length > 3)
                    {
                        string adapterName = columns[^1];
                        interfaces.Add(adapterName);
                    }
                }
            }

            return interfaces;
        }

        private bool CheckTime(Rule p, TimeOnly now) => (now <= p.End && now >= p.Start) || (now >= p.Start && p.Start >= p.End) || (now <= p.End && p.End <= p.Start);

        private async void RestartIfRulesChanged()
        {
            var rule = await GetRules();
            await Task.Run(async () =>
            {
                while (Running)
                {
                    var newRule = await GetRules();
                    if (!string.IsNullOrEmpty(rule) && newRule != rule)
                    {
                        rule = newRule;
                        _rule = JsonSerializer.Deserialize<Rule>(rule);
                        await RestartBlocker();
                    }
                    Thread.Sleep(200);
                }
            });
        }

        private async Task RestartBlocker()
        {
            await Task.Run(async () =>
            {
                await StopBlock();
                RunBlock();
            });
        }

        private async Task StopBlock()
        {
            await Task.Run(() =>
            {
                if (Running)
                {
                    Running = false;
                }
            });
        }

        private void CreateRulesIfMissing()
        {
            Task.Run(() =>
            {
                while (true)
                {
                    if (!File.Exists("Rule.txt"))
                        File.WriteAllText("Rule.txt", JsonSerializer.Serialize(new Rule() { Start = TimeOnly.MinValue, End = TimeOnly.MinValue }));
                    Thread.Sleep(200);
                }
            });

        }
    }
}
