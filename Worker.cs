using System.Diagnostics;
using System.Management;
using System.Text.Json;

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
        private void Enable(ManagementObject obj) => obj.InvokeMethod("Enable", null);
        private void Disable(ManagementObject obj) => obj.InvokeMethod("Disable", null);

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
                        TimeOnly now = TimeOnly.Parse(DateTime.Now.ToLongTimeString());
                        var query = new SelectQuery("Win32_NetworkAdapter");
                        if (_rule.Start == _rule.End) continue;
                        try
                        {
                            using (var searcher = new ManagementObjectSearcher(query))
                            {
                                if (CheckTime(_rule, now))
                                {
                                    foreach (ManagementObject obj in searcher.Get())
                                    {
                                        if (obj["NetEnabled"] != null)
                                            Disable(obj);
                                    }
                                }
                                else
                                {
                                    foreach (ManagementObject obj in searcher.Get())
                                    {
                                        if (obj["NetEnabled"] != null && !(bool)obj["NetEnabled"])
                                            Enable(obj);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Error: " + ex.Message);
                        }
                        Thread.Sleep(200);
                    }
                });
            }
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
