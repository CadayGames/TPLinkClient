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
        public static MainForm mainWindow;
        public TPLinkTelnet telnet;
        public Thread updateThread;
        public LabelUptimeTimer labelUptimeTimer;

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
            
            labelUptimeTimer = new LabelUptimeTimer();
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            UpdateRouterInfo();
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            updateThread.Abort();
            labelUptimeTimer.Stop();
        }

        public static object updateThreadLock = new object();

        public void UpdateRouterInfo()
        {
            if (updateThread != null && updateThread.IsAlive)
                return;

            updateThread = new Thread(UpdateRouterInfoEntry);
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
                        }
                    }
                }
            }

            iniReaded = true;
        }

        public void UpdateRouterInfoEntry()
        {
            lock (updateThreadLock)
            {
                ReadIniFile(Application.ProductName + ".ini");

                try
                {
                    telnet = new TPLinkTelnet(RouterInfo.IP, RouterInfo.Port);
                    telnet.UpdateInfo(RouterInfo.Username, RouterInfo.Password);
                    telnet.client.Close();
                }
                catch (SocketException)
                {
                    SetStatusBarLabel("Brak połączenia sieciowego.");
                }
                
                Thread.Sleep(1000);
            }
        }

        public static void SetStatusBarLabel(string text)
        {
            mainWindow.Invoke((MethodInvoker) delegate 
            {
                mainWindow.statusBarLabel.Text = text;
            });
        }

        public class TPLinkTelnet
        {
            public static int WAIT_DELAY_MS = 20;
            public static int RESPONSE_SIZE = 4096;

            public TcpClient client = null;
            public NetworkStream ns = null;

            public TPLinkTelnet(string ip, int port)
            {
                client = new TcpClient(ip, port);
                ns = client.GetStream();
            }
            
            byte[] responseBytes = new byte[RESPONSE_SIZE];
            string response = string.Empty;
            string responseBytesStr = string.Empty;
            int bytesRead = 0;

            private int ReadResponseBytes(int max_loops = 10)
            {
                for (int i = 0; i < max_loops; i++)
                {
                    if (ns.DataAvailable)
                    {
                        bytesRead = ns.Read(responseBytes, 0, RESPONSE_SIZE);

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
                ns.Write(commandBytes, 0, commandBytes.Length);
            }

            public string GetMessageInfo(string var)
            {
                Regex regex = new Regex("^" + var + "=(.+)\r\n", RegexOptions.Multiline);
                Match match = regex.Match(response);
                if (match.Groups.Count >= 2)
                    return match.Groups[1].ToString();
                else
                    return string.Empty;
            }

            public bool CheckMessageRegex(string regex)
            {
                if (new Regex(regex).IsMatch(response))
                    return true;
                else
                    return false;
            }

            public void UpdateInfo(string username, string password)
            {
                SetStatusBarLabel("Nawiązywanie połączenia...");

                if (!ReadMessageRegex("username:"))
                {
                    Console.WriteLine("Nie znaleziono regex w response. (username)");

                    if (CheckMessageRegex("Authorization failed"))
                    {
                        SetStatusBarLabel("Przekroczono limit nieprawidłowych prób logowania.");
                        return;
                    }

                    SetStatusBarLabel("Nie udało się ustanowić połączenia z routerem.");
                    return;
                }
                
                // type username
                
                SendMessage(username);
                
                if (!ReadMessageRegex("password:"))
                {
                    Console.WriteLine("Nie znaleziono regex w response. (password)");
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
                        SetStatusBarLabel("Dane uwierzytelniające są nieprawidłowe.");
                        return;
                    }

                    SetStatusBarLabel("Nie udało się zalogować.");

                    return;
                }

                // wan show connection info
                
                SendMessage("wan show connection info " + RouterInfo.WANInterface);

                if (!ReadMessageRegex("\\#"))
                {
                    Console.WriteLine("Nie znaleziono regex w response. (# -> wan show connection info)");
                    SetStatusBarLabel("Nie udało się pobrać informacji o interfejsie WAN.");
                    return;
                }

                // wan info

                String uptime = GetMessageInfo("uptime");
                String connectionStatus = GetMessageInfo("connectionStatus");
                String externalIPAddress = GetMessageInfo("externalIPAddress");
                
                mainWindow.Invoke((MethodInvoker) delegate {
                    mainWindow.labelStatus.Text = connectionStatus;
                    mainWindow.labelWanIP.Text = externalIPAddress;
                });

                int.TryParse(uptime, out int uptimeSeconds);

                mainWindow.labelUptimeTimer.Update(uptimeSeconds);

                SetStatusBarLabel("Zakończono pobieranie danych.");

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

            public void Update(int seconds)
            {
                if (!timer.Enabled)
                    timer.Start();

                if (Math.Abs(this.seconds - seconds) > 1)
                {
                    this.seconds = seconds;
                    UpdateLabel();
                }
            }

            private void UpdateLabel()
            {
                String uptime = string.Format("{0:00}:{1:00}:{2:00}",
                    seconds / 3600, (seconds / 60) % 60, seconds % 60);

                mainWindow.Invoke((MethodInvoker)delegate {
                    mainWindow.labelUptime.Text = uptime;
                });
            }

            private void Timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
            {
                seconds++;
                UpdateLabel();
            }
        }
        
        private void MainForm_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            if (e.KeyCode == Keys.F5)
            {
                //Console.WriteLine("Updating...");
                UpdateRouterInfo();
            }
        }
    }
}
