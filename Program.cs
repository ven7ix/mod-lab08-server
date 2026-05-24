using ScottPlot;
using System.Text;

namespace mod_lab08_server
{
    class Program
    {
        static void Main()
        {
            int channels = 5;
            double mu = 5.0;
            int requestsTotal = 100;

            double[] lambdaValues = [2, 4, 6, 8, 10, 12, 14, 16, 18, 20];

            string resultDir = "result";
            if (!Directory.Exists(resultDir))
            {
                _ = Directory.CreateDirectory(resultDir);
            }

            var results = new List<(double lambda, double P0_exp, double Pn_exp, double Q_exp, double A_exp, double k_exp, double P0_th, double Pn_th, double Q_th, double A_th, double k_th)>();

            Console.WriteLine("Starting expertiments");
            foreach (double lambda in lambdaValues)
            {
                Console.Write($"Lambda = {lambda:F2}: ");
                var sim = RunSingleExperiment(channels, mu, lambda, requestsTotal);
                results.Add(sim);
                Console.WriteLine($" Pn (experiment) = {sim.Pn_exp:F4}, Pn (theory) = {sim.Pn_th:F4}");
            }
            string resultsFile = Path.Combine(resultDir, "output.txt");

            using (StreamWriter writer = new StreamWriter(resultsFile, false, Encoding.UTF8))
            {
                writer.WriteLine("lambda\tP0_exp\tPn_exp\tQ_exp\tA_exp\tk_exp\tP0_th\tPn_th\tQ_th\tA_th\tk_th");
                foreach (var r in results)
                {
                    writer.WriteLine($"{r.lambda:F2}\t{r.P0_exp:F3}\t{r.Pn_exp:F3}\t{r.Q_exp:F3}\t{r.A_exp:F3}\t" + $"{r.k_exp:F3}\t{r.P0_th:F3}\t{r.Pn_th:F3}\t{r.Q_th:F3}\t{r.A_th:F3}\t{r.k_th:F3}");
                }
            }

            double[] lambdas = [.. results.Select(r => r.lambda)];
            double[] p0_exp = [.. results.Select(r => r.P0_exp)];
            double[] p0_th = [.. results.Select(r => r.P0_th)];
            double[] pn_exp = [.. results.Select(r => r.Pn_exp)];
            double[] pn_th = [.. results.Select(r => r.Pn_th)];
            double[] q_exp = [.. results.Select(r => r.Q_exp)];
            double[] q_th = [.. results.Select(r => r.Q_th)];
            double[] a_exp = [.. results.Select(r => r.A_exp)];
            double[] a_th = [.. results.Select(r => r.A_th)];
            double[] k_exp = [.. results.Select(r => r.k_exp)];
            double[] k_th = [.. results.Select(r => r.k_th)];

            CreatePlot(lambdas, p0_exp, p0_th, "Probability", "The probability of downtime (P0)", Path.Combine(resultDir, "p-1.png"), Alignment.LowerLeft);
            CreatePlot(lambdas, pn_exp, pn_th, "Probability", "Probability of failure (Pn)", Path.Combine(resultDir, "p-2.png"));
            CreatePlot(lambdas, q_exp, q_th, "Q", "Relative throughput", Path.Combine(resultDir, "p-3.png"), Alignment.LowerLeft);
            CreatePlot(lambdas, a_exp, a_th, "A (requests per second)", "Absolute throughput", Path.Combine(resultDir, "p-4.png"));
            CreatePlot(lambdas, k_exp, k_th, "k", "Average number of busy channels", Path.Combine(resultDir, "p-5.png"));

            Console.WriteLine("\nThe graphs are saved to a folder 'result'");
        }

        static (double lambda, double P0_exp, double Pn_exp, double Q_exp, double A_exp, double k_exp, double P0_th, double Pn_th, double Q_th, double A_th, double k_th) RunSingleExperiment(int n, double mu, double lambda, int totalRequests)
        {
            var server = new Server(n, mu);
            var client = new Client(server);
            Random rand = new Random();

            DateTime startTime = DateTime.Now;

            for (int id = 1; id <= totalRequests; id++)
            {
                double interarrivalSec = -Math.Log(1 - rand.NextDouble()) / lambda;
                int sleepMs = (int)(interarrivalSec * 1000);
                if (sleepMs > 0)
                {
                    Thread.Sleep(sleepMs);
                }

                client.Send(id);
            }

            server.WaitForCompletion();
            server.FinalizeIdleTime();

            DateTime endTime = DateTime.Now;
            double totalTime = (endTime - startTime).TotalSeconds;

            double Pn_exp = (double)server.rejectedCount / server.requestCount;
            double Q_exp = 1 - Pn_exp;
            double A_exp = server.processedCount / totalTime;
            double k_exp = server.TotalBusyTime / totalTime;
            double P0_exp = server.TotalIdleTime / totalTime;

            double rho = lambda / mu;
            double sum = 0;
            for (int i = 0; i <= n; i++)
            {
                sum += Math.Pow(rho, i) / Factorial(i);
            }

            double P0_th = 1.0 / sum;
            double Pn_th = Math.Pow(rho, n) / Factorial(n) * P0_th;
            double Q_th = 1 - Pn_th;
            double A_th = lambda * Q_th;
            double k_th = rho * Q_th;

            return (lambda, P0_exp, Pn_exp, Q_exp, A_exp, k_exp, P0_th, Pn_th, Q_th, A_th, k_th);
        }

        static double Factorial(int n)
        {
            double f = 1;
            for (int i = 2; i <= n; i++)
            {
                f *= i;
            }

            return f;
        }

        static void CreatePlot(double[] x, double[] yExp, double[] yTh, string yLabel, string title, string filename, Alignment legendLocation = Alignment.LowerRight)
        {
            Plot plot = new();
            plot.Title(title);
            plot.XLabel("Lambda (input flow rate, requests per second)");
            plot.YLabel(yLabel);

            var exp = plot.Add.Scatter(x, yExp);
            exp.LegendText = "Experiment";

            var th = plot.Add.Scatter(x, yTh);
            th.LegendText = "Theory";

            plot.Legend.Alignment = legendLocation;

            _ = plot.SavePng(filename, 800, 500);
        }
    }

    struct PoolRecord
    {
        public Thread thread;
        public bool in_use;
    }

    class Server(int channels, double serviceRate)
    {
        private readonly PoolRecord[] pool = new PoolRecord[channels];
        private readonly object threadLock = new();
        public int requestCount = 0;
        public int processedCount = 0;
        public int rejectedCount = 0;

        private readonly double serviceRate = serviceRate;
        private int activeChannels = 0;
        private DateTime lastStateChange = DateTime.Now;
        private readonly Random rand = new();

        public double TotalBusyTime { get; private set; } = 0;
        public double TotalIdleTime { get; private set; } = 0;
        public double TotalTime => TotalBusyTime + TotalIdleTime;

        public void Proc(object? sender, ProcEventArgs e)
        {
            lock (threadLock)
            {
                requestCount++;
                for (int i = 0; i < pool.Length; i++)
                {
                    if (!pool[i].in_use)
                    {
                        pool[i].in_use = true;
                        pool[i].thread = new Thread(new ParameterizedThreadStart(Answer));
                        pool[i].thread.Start(e.Id);
                        processedCount++;
                        UpdateChannelCount(1);
                        return;
                    }
                }
                rejectedCount++;
            }
        }

        private void Answer(object? arg)
        {
            double serviceTimeSec = ExponentialRandom(1.0 / serviceRate);
            Thread.Sleep((int)(serviceTimeSec * 1000));

            lock (threadLock)
            {
                TotalBusyTime += serviceTimeSec;
                for (int i = 0; i < pool.Length; i++)
                    if (pool[i].thread == Thread.CurrentThread)
                        pool[i].in_use = false;
                UpdateChannelCount(-1);
            }
        }

        private void UpdateChannelCount(int delta)
        {
            DateTime now = DateTime.Now;
            double elapsed = (now - lastStateChange).TotalSeconds;
            lastStateChange = now;

            if (activeChannels == 0)
            {
                TotalIdleTime += elapsed;
            }
            activeChannels += delta;
        }

        public void FinalizeIdleTime()
        {
            lock (threadLock)
            {
                DateTime now = DateTime.Now;
                double elapsed = (now - lastStateChange).TotalSeconds;
                if (activeChannels == 0)
                {
                    TotalIdleTime += elapsed;
                }
                lastStateChange = now;
            }
        }

        private double ExponentialRandom(double mean)
        {
            double u = rand.NextDouble();
            return -mean * Math.Log(1 - u);
        }

        public void WaitForCompletion()
        {
            while (true)
            {
                lock (threadLock)
                {
                    if (activeChannels == 0)
                    {
                        break;
                    }
                }

                Thread.Sleep(10);
            }
        }
    }

    public class ProcEventArgs : EventArgs
    {
        public int Id { get; set; }
    }

    class Client
    {
        public event EventHandler<ProcEventArgs>? Request;

        public Client(Server server)
        {
            Request += server.Proc;
        }

        public void Send(int id)
        {
            ProcEventArgs args = new ProcEventArgs
            {
                Id = id
            };

            OnProc(args);
        }

        protected virtual void OnProc(ProcEventArgs e)
        {
            Request?.Invoke(this, e);
        }
    }
}
