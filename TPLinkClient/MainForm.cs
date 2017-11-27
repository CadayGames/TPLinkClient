using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;

namespace TPLinkClient
{
    public partial class MainForm : Form
    {
        public static MainForm mainWindow = null;
        public Thread updateThread = null;

        public LabelUptimeTimer labelUptimeTimer = new LabelUptimeTimer();

        public static bool autoUpdateSilent = false;

        private static bool autoUpdateEnabled = true;
        private static bool cancelAutoUpdate = false;

        public System.Timers.Timer autoUpdateTimer = new System.Timers.Timer {
            AutoReset = true,
            Interval = 10 * 1000,
        };

        public class RouterInfo
        {
            public static string IP = "192.168.1.1";
            public static int Port = 23;
            public static string Username = "admin";
            public static string Password = "admin";
            public static string WANInterface = "pppoe_0_35_3_d";
        }

        public MainForm()
        {
            mainWindow = this;

            InitializeComponent();

            initControlsRecursive(Controls);
        }

        void initControlsRecursive(Control.ControlCollection collection)
        {
            foreach (Control c in collection)
            {
                c.MouseDown += (sender, e) => {
                    if (!(sender as Control).CanSelect)
                        ActiveControl = null;
                };

                initControlsRecursive(c.Controls);
            }
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            UpdateRouterInfo(false);
            autoUpdateTimer.Elapsed += AutoUpdateTimer_Elapsed;
            autoUpdateTimer.Start();
        }

        private void AutoUpdateTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (autoUpdateEnabled)
                if (!cancelAutoUpdate)
                    UpdateRouterInfo(autoUpdateSilent);
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            mainWindow = null;
            updateThread.Abort();
            autoUpdateTimer.Stop();
            labelUptimeTimer.Stop();
        }

        public void UpdateRouterInfo(bool silent)
        {
            if (updateThread != null && updateThread.IsAlive)
                return;

            updateThread = new Thread(() => { UpdateRouterInfoEntry(silent); });
            updateThread.Start();
        }

        private bool iniReaded = false;

        public void ReadIniFile(string filename)
        {
            if (iniReaded)
                return;
            
            if (System.IO.File.Exists(filename))
            {
                string[] lines = System.IO.File.ReadAllLines(filename);
                foreach (string line in lines)
                {
                    string[] varValue = line.Split(new char[]{'='}, 2);
                    if (varValue.Length == 2)
                    {
                        string var = varValue[0].ToLower();
                        string value = varValue[1];
                        switch (var)
                        {
                            case "ip": RouterInfo.IP = value; break;
                            case "port":
                                if (int.TryParse(value, out int valueInt))
                                    RouterInfo.Port = valueInt;
                                break;
                            case "username": RouterInfo.Username = value; break;
                            case "password": RouterInfo.Password = value; break;
                            case "waninterface": RouterInfo.WANInterface = value; break;
                            case "autoupdate":
                                if (bool.TryParse(value, out bool autoUpdate))
                                {
                                    autoUpdateEnabled = autoUpdate;
                                    mainWindow?.UpdateAutoUpdate(autoUpdate);
                                }
                                break;
                        }
                    }
                }
            }

            iniReaded = true;
        }

        public void UpdateInfoLabels(string status, string ip, string uptime)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => UpdateInfoLabels(status, ip, uptime)));
                return;
            }

            labelStatus.Text = status;
            labelWanIP.Text = ip;

            int.TryParse(uptime, out int uptimeSeconds);

            labelUptimeTimer.Update(uptimeSeconds);
        }

        public void UpdateUptimeLabel(string uptime)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => UpdateUptimeLabel(uptime)));
                return;
            }

            labelUptime.Text = uptime;
        }

        public void SetRefreshButtonEnabled(bool enabled)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => SetRefreshButtonEnabled(enabled)));
                return;
            }

            buttonRefresh.Enabled = enabled;
        }

        public void UpdateStatusBar(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => UpdateStatusBar(text)));
                return;
            }

            statusBarLabel.Text = text;
        }

        public void UpdateAutoUpdate(bool enabled)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => UpdateAutoUpdate(enabled)));
                return;
            }

            chkAutoUpdate.Checked = enabled;
        }

        public static object updateThreadLock = new object();

        public void UpdateRouterInfoEntry(bool silent)
        {
            lock (updateThreadLock)
            {
                mainWindow?.SetRefreshButtonEnabled(false);

                ReadIniFile(Application.ProductName + ".ini");

                try
                {
                    TPLinkTelnet telnet = new TPLinkTelnet(RouterInfo.IP, RouterInfo.Port);
                    telnet.UpdateInfo(RouterInfo.Username, RouterInfo.Password, silent);
                    telnet.Dispose();
                }
                catch (SocketException)
                {
                    if (!silent)
                        SetStatusBarLabel("Brak połączenia sieciowego.");
                }
                
                Thread.Sleep(1000);

                mainWindow?.SetRefreshButtonEnabled(true);
            }
        }

        public static void SetStatusBarLabel(string text)
        {
            mainWindow?.UpdateStatusBar(text);
        }

        public class TPLinkTelnet : IDisposable
        {
            public static int WAIT_DELAY_MS = 20;
            public static int RESPONSE_SIZE = 4096;

            public TcpClient Client;
            public NetworkStream Stream;

            public TPLinkTelnet(string ip, int port)
            {
                Client = new TcpClient(ip, port);

                if (Client != null)
                    Stream = Client.GetStream();
            }
            
            public void Dispose()
            {
                if (Stream != null)
                {
                    Stream.Close();
                    Stream = null;
                }

                if (Client != null)
                {
                    Client.Close();
                    Client = null;
                }
            }
                        
            byte[] responseBytes = new byte[RESPONSE_SIZE];
            string response = string.Empty;
            string responseBytesStr = string.Empty;
            int bytesRead = 0;

            private int ReadResponseBytes(int max_loops = 10)
            {
                for (int i = 0; i < max_loops; i++)
                {
                    if (Stream.DataAvailable)
                    {
                        bytesRead = Stream.Read(responseBytes, 0, RESPONSE_SIZE);

                        if (bytesRead > 0)
                        {
                            responseBytesStr = Encoding.UTF8.GetString(responseBytes, 0, bytesRead);
                            //Console.WriteLine("bytesRead:{0};responseBytesStr:{1};", bytesRead, responseBytesStr);
                            return bytesRead;
                        }
                    }

                    //Console.WriteLine("Waiting for response...");
                    Thread.Sleep(WAIT_DELAY_MS);
                }

                return 0;
            }

            public void ReadMessage()
            {
                if (ReadResponseBytes() > 0)
                    response += responseBytesStr;
            }

            public bool ReadMessageRegex(string regex, int max_loops = 5)
            {
                ClearMessage();

                for (int i = 0; i < max_loops; i++)
                {
                    ReadMessage();

                    if (CheckMessageRegex(regex))
                        return true;
                }

                return false;
            }

            public void ClearMessage()
            {
                response = string.Empty;
                responseBytesStr = string.Empty;
            }

            public void SendMessage(string message)
            {
                byte[] commandBytes = Encoding.UTF8.GetBytes(message + "\r\n");
                Stream.Write(commandBytes, 0, commandBytes.Length);
            }

            public string GetMessageInfo(string var)
            {
                Regex regex = new Regex("^" + var + "=(.+)\r\n", RegexOptions.Multiline);
                Match match = regex.Match(response);
                if (match.Groups.Count >= 2)
                    return match.Groups[1].ToString();
                else
                    return null;
            }

            public bool CheckMessageRegex(string regex)
            {
                if (new Regex(regex).IsMatch(response))
                    return true;
                else
                    return false;
            }

            public void UpdateInfo(string username, string password, bool silent)
            {
                if (!silent)
                    SetStatusBarLabel("Nawiązywanie połączenia...");

                if (!ReadMessageRegex("username:"))
                {
                    Console.WriteLine("Nie znaleziono regex w response. (username)");

                    if (CheckMessageRegex("Authorization failed"))
                    {
                        if (!silent)
                            SetStatusBarLabel("Przekroczono limit nieprawidłowych prób logowania.");
                        return;
                    }

                    if (!silent)
                        SetStatusBarLabel("Nie udało się ustanowić połączenia z routerem.");
                    return;
                }
                
                // type username
                
                SendMessage(username);
                
                if (!ReadMessageRegex("password:"))
                {
                    Console.WriteLine("Nie znaleziono regex w response. (password)");
                    if (!silent)
                        SetStatusBarLabel("Coś poszło nie tak przy logowaniu.");
                    return;
                }

                // type password
                
                SendMessage(password);

                if (!ReadMessageRegex("\\#"))
                {
                    Console.WriteLine("Nie znaleziono regex w response. (# -> password)");

                    if (CheckMessageRegex("Login incorrect"))
                    {
                        if (!silent)
                            SetStatusBarLabel("Dane uwierzytelniające są nieprawidłowe.");

                        cancelAutoUpdate = true;
                        return;
                    }

                    if (!silent)
                        SetStatusBarLabel("Nie udało się zalogować.");

                    return;
                }

                // wan show connection info
                
                SendMessage("wan show connection info " + RouterInfo.WANInterface);

                if (!ReadMessageRegex("\\#"))
                {
                    Console.WriteLine("Nie znaleziono regex w response. (# -> wan show connection info)");
                    if (!silent)
                        SetStatusBarLabel("Nie udało się pobrać informacji o interfejsie WAN.");
                    return;
                }

                // wan info

                String uptime = GetMessageInfo("uptime");
                String connectionStatus = GetMessageInfo("connectionStatus");
                String externalIPAddress = GetMessageInfo("externalIPAddress");

                if (uptime != null && connectionStatus != null && externalIPAddress != null)
                {
                    mainWindow?.UpdateInfoLabels(connectionStatus, externalIPAddress, uptime);

                    //if (!silent)
                    SetStatusBarLabel(string.Format(
                        "Zakończono pobieranie danych ({0}).", DateTime.Now.ToString("HH:mm")));

                    cancelAutoUpdate = false;
                }
                else
                {
                    if (!silent)
                        SetStatusBarLabel("Nie udało się odczytać potrzebnych informacji.");
                }
                
                // logout

                SendMessage("logout");
                
                if (!ReadMessageRegex("Bye"))
                {
                    Console.WriteLine("Nie znaleziono regex w response. (Bye -> logout)");
                }
            }
        }

        public class LabelUptimeTimer
        {
            public int seconds = 0;
            public System.Timers.Timer timer;

            public LabelUptimeTimer()
            {
                timer = new System.Timers.Timer
                {
                    Interval = 1000,
                    AutoReset = true
                };

                timer.Elapsed += Timer_Elapsed;
            }

            public void Start()
            {
                timer.Start();
            }

            public void Stop()
            {
                timer.Stop();
            }

            public void Update(int seconds, bool startIfNeeded = true)
            {
                if (seconds < 0)
                    seconds = 0;

                if (!timer.Enabled && startIfNeeded && seconds != 0)
                    timer.Start();

                if (seconds == 0)
                    timer.Stop();
                
                if (Math.Abs(this.seconds - seconds) > 1 || seconds == 0)
                {
                    this.seconds = seconds;
                    UpdateLabel(seconds);
                }
            }

            private void UpdateLabel(int secs)
            {
                string uptime = string.Format("{0:00}:{1:00}:{2:00}",
                    secs / 3600, (secs / 60) % 60, secs % 60);

                mainWindow?.UpdateUptimeLabel(uptime);
            }

            private void Timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
            {
                UpdateLabel(++seconds);
            }
        }
        
        private void MainForm_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            
        }

        private void buttonRefresh_Click(object sender, EventArgs e)
        {
            UpdateRouterInfo(false);
        }

        private void chkAutoUpdate_CheckedChanged(object sender, EventArgs e)
        {
            autoUpdateEnabled = chkAutoUpdate.Checked;
        }

        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5)
            {
                //Console.WriteLine("Updating...");
                UpdateRouterInfo(false);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                if (ActiveControl != null)
                    ActiveControl = null;
            }
        }

        private void tableMenuCopyIP_Click(object sender, EventArgs e)
        {
            Clipboard.SetText(labelWanIP.Text);
        }

        private void buttonRefresh_MouseEnter(object sender, EventArgs e)
        {
            buttonRefresh.Image = Properties.Resources.refresh_button_hover;
        }

        private void buttonRefresh_MouseLeave(object sender, EventArgs e)
        {
            buttonRefresh.Image = Properties.Resources.refresh_button;
        }
    }
}
