using System.Collections;
using System.Threading;
using UnityEngine;

namespace reromanlee.ConsoleContainer.Samples
{
    /// <summary>
    /// Showcase component: spins up several named <see cref="ConsoleInstance"/>s
    /// and logs to them continuously so the Console Viewer
    /// (<c>Tools ▸ Console Viewer</c>) fills up with realistic, per-context
    /// traffic.
    ///
    /// Drop it on any GameObject and press Play, then open the viewer and switch
    /// the instance dropdown between "Networking", "AI", "Worker (bg thread)" and
    /// "All Instances". The worker instance logs from a background thread to
    /// demonstrate that logging is safe from any context.
    /// </summary>
    [AddComponentMenu("ConsoleContainer/Console Container Demo")]
    public sealed class ConsoleContainerDemo : MonoBehaviour
    {
        private ConsoleInstance networking;
        private ConsoleInstance ai;
        private ConsoleInstance worker;

        private Thread workerThread;
        private CancellationTokenSource workerCancellation;

        private void OnEnable()
        {
            // Naming each instance surfaces it in the viewer's dropdown.
            networking = new ConsoleInstance("Networking");
            ai = new ConsoleInstance("AI");
            worker = new ConsoleInstance("Worker (bg thread)");

            StartCoroutine(SimulateNetworking());
            StartCoroutine(SimulateAI());

            // Log from a background thread to showcase thread-safety. The captured
            // instance and token keep this run independent of any later re-enable.
            workerCancellation = new CancellationTokenSource();
            ConsoleInstance capturedWorker = worker;
            CancellationToken token = workerCancellation.Token;
            workerThread = new Thread(() => SimulateWorker(capturedWorker, token))
            {
                IsBackground = true,
                Name = "ConsoleContainerDemoWorker"
            };
            workerThread.Start();
        }

        private void OnDisable()
        {
            StopAllCoroutines();

            workerCancellation?.Cancel();
            if (workerThread != null && workerThread.IsAlive)
            {
                workerThread.Join(500);
            }

            workerCancellation?.Dispose();
            workerCancellation = null;
            workerThread = null;

            // Dispose clears each instance and detaches it from the viewer.
            networking?.Dispose();
            ai?.Dispose();
            worker?.Dispose();
        }

        private IEnumerator SimulateNetworking()
        {
            int packet = 0;
            while (true)
            {
                yield return new WaitForSeconds(Random.Range(0.4f, 1.2f));
                packet++;

                float roll = Random.value;
                if (roll < 0.7f)
                {
                    // object overload => source becomes "ConsoleContainerDemo".
                    networking.CreateText(this, "Received packet", packet.ToString(), "from server");
                }
                else if (roll < 0.9f)
                {
                    networking.CreateWarning(this, "High latency:", Random.Range(120, 350).ToString(), "ms");
                }
                else
                {
                    // string overload => source becomes "Socket".
                    networking.CreateError("Socket", "Packet", packet.ToString(), "dropped, retransmitting");
                }
            }
        }

        private IEnumerator SimulateAI()
        {
            string[] states = { "Idle", "Patrol", "Chase", "Attack", "Flee" };
            string current = states[0];

            while (true)
            {
                yield return new WaitForSeconds(Random.Range(0.6f, 1.6f));

                string next = states[Random.Range(0, states.Length)];
                ai.CreateText(this, "Agent state", current, "->", next);

                if (next == "Flee")
                {
                    ai.CreateWarning(this, "No safe path found, recomputing");
                }

                current = next;
            }
        }

        private static void SimulateWorker(ConsoleInstance instance, CancellationToken token)
        {
            System.Random rng = new System.Random();
            int job = 0;

            while (!token.IsCancellationRequested)
            {
                // Wait, but wake immediately when cancellation is requested.
                if (token.WaitHandle.WaitOne(rng.Next(500, 1500)))
                {
                    break;
                }

                job++;
                if (rng.Next(100) < 75)
                {
                    instance.CreateText("BackgroundJob", "Processed job", job.ToString());
                }
                else
                {
                    instance.CreateWarning("BackgroundJob", "Job", job.ToString(), "took longer than expected");
                }
            }
        }
    }
}
