/*
Copyright 2009-2022 Intel Corporation

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

using System;
using System.IO;
using System.Net;
using System.Linq;
using System.Drawing;
using System.Reflection;
using System.Collections;
using System.Diagnostics;
using System.Net.Security;
using System.Windows.Forms;
using System.ServiceProcess;
using System.Security.Principal;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Drawing.Drawing2D;

namespace MeshAssistant
{
    public partial class MainForm : Form
    {
        public bool debug = false;
        public string[] args;
        public int timerSlowDown = 0;
        public bool doclose = false;
        public bool helpRequested = false;
        public bool autoConnect = false;
        public MeshAgent agent = null; // This is a monitored agent
        public MeshCentralAgent mcagent = null; // This is the built-in agent
        public int queryNumber = 0;
        public SnapShotForm snapShotForm = null;
        public RequestHelpForm requestHelpForm = null;
        public SessionsForm sessionsForm = null;
        public MeInfoForm meInfoForm = null;
        public ConsoleForm consoleForm = null;
        public PrivacyBarForm privacyBar = null;
        public UpdateForm updateForm = null;
        public EventsForm eventsForm = null;
        public BrowserForm browserForm = null;
        public GuestSharingForm guestSharingForm = null;
        public bool isAdministrator = false;
        public bool forceExit = false;
        public bool noUpdate = false;
        public ArrayList pastConsoleCommands = new ArrayList();
        public Dictionary<string, string> agents = null;
        public string selectedAgentName = null;
        public string currentAgentName = null;
        public NotifyForm notifyForm = null;
        public ConsentForm consentForm = null;
        public int embeddedMshLength = 0;
        public string selfExecutableHashHex = null;
        public string updateHash;
        public string updateUrl;
        public string updateServerHash;
        public List<PrivacyBarForm> privacyBars = null;
        private bool startVisible = false;
        public List<LogEventStruct> userEvents = new List<LogEventStruct>();
        public bool SystemTrayApp = true;
        public Image CustomizationLogo = null;
        public string CustomizationTitle = null;
        public string autoHelpRequest = null;
        public bool allowUseOfProxy = true;

        public struct LogEventStruct
        {
            public LogEventStruct(DateTime time, string userid, string msg) { this.time = time; this.userid = userid; this.msg = msg; }
            public DateTime time;
            public string userid;
            public string msg;
        }

        private void LoadEventsFromFile()
        {
            string[] events = null;
            try { events = File.ReadAllLines("events.log"); } catch (Exception) { }
            if (events == null) return;
            foreach (string e in events)
            {
                int i = e.IndexOf(", ");
                if (i == -1) continue;
                int j = e.IndexOf(", ", i + 2);
                if (j == -1) continue;
                string time = e.Substring(0, i);
                string userid = e.Substring(i + 2, j - i - 2);
                string msg = e.Substring(j + 2);
                userEvents.Add(new LogEventStruct(DateTime.Parse(time), userid, msg));
                if (userEvents.Count > 1000) { userEvents.RemoveAt(0); }
            }
        }

        public void Log(string msg) {
            if (debug) { try { File.AppendAllText("debug.log", DateTime.Now.ToString("HH:mm:tt.ffff: ") + msg + "\r\n"); } catch (Exception) { } }
        }

        public void Event(string userid, string msg)
        {
            DateTime now = DateTime.Now;
            LogEventStruct e = new LogEventStruct(now, userid, msg);
            userEvents.Add(e);
            AddEventToForm(e);
            try { File.AppendAllText("events.log", now.ToString("yyyy-MM-ddTHH:mm:sszzz") + ", " + userid + ", " + msg + "\r\n"); } catch (Exception) { }
            Log(string.Format("Event ({0}): {1}", userid, msg));
        }

        delegate void AddEventToFormHandler(LogEventStruct e);

        public void AddEventToForm(LogEventStruct e)
        {
            if (eventsForm == null) return;
            if (this.InvokeRequired) { this.Invoke(new AddEventToFormHandler(AddEventToForm), e); return; }
            eventsForm.addEvent(e);
        }

        private bool RemoteCertificateValidationCallbackGlobal(object sender, X509Certificate certificate, X509Chain chain, System.Net.Security.SslPolicyErrors sslPolicyErrors)
        {
            if (mcagent.state < 2)
            {
                // If we are not connected at all, accept the TLS cert since we are going to be doing secondary auth.
                mcagent.WebSocket.tlsCert = new X509Certificate2(certificate);
                return true;
            }
            else
            {
                // We are connected, all further connections need to have the same TLS cert as the main control connection.
                if ((mcagent.ServerTlsHashStr != null) && ((mcagent.ServerTlsHashStr == certificate.GetCertHashString()) || (mcagent.ServerTlsHashStr == webSocketClient.GetMeshKeyHash(certificate)) || (mcagent.ServerTlsHashStr == webSocketClient.GetMeshCertHash(certificate)))) { return true; }
            }
            return false;
        }

        public MainForm(string[] args)
        {
            // Set TLS 1.2
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.ServerCertificateValidationCallback = new System.Net.Security.RemoteCertificateValidationCallback(RemoteCertificateValidationCallbackGlobal);

            // Perform self update operations if any.
            this.args = args;
            string update = null;
            string delete = null;
            foreach (string arg in this.args) 
            {
                if (arg.ToLower() == "-debug") { debug = true; }
                if ((arg.Length == 8) && (arg.ToLower() == "-visible")) { startVisible = true; }
                if ((arg.Length == 9) && (arg.ToLower() == "-noupdate")) { noUpdate = true; }
                if (arg.Length > 8 && arg.Substring(0, 8).ToLower() == "-update:") { update = arg.Substring(8); }
                if (arg.Length > 8 && arg.Substring(0, 8).ToLower() == "-delete:") { delete = arg.Substring(8); }
                if (arg.Length > 6 && arg.Substring(0, 6).ToLower() == "-help:") { autoHelpRequest = arg.Substring(6); }
                if (arg.Length > 11 && arg.Substring(0, 11).ToLower() == "-agentname:") { selectedAgentName = arg.Substring(11); }
                if ((arg.Length == 8) && (arg.ToLower() == "-connect")) { autoConnect = true; }
                if ((arg.Length == 8) && (arg.ToLower() == "-noproxy")) { allowUseOfProxy = false; }
            }
            if (debug) { try { File.AppendAllText("debug.log", "\r\n\r\n"); } catch (Exception) { } }
            Log("***** Starting MeshCentral Assistant *****");
            Log("Version " + Assembly.GetExecutingAssembly().GetName().Version.ToString());

            if (update != null)
            {
                Log("Performing update");

                // New args
                ArrayList args2 = new ArrayList();
                foreach (string a in args) { if (a.StartsWith("-update:") == false) { args2.Add(a); } }

                // Remove ".update.exe" and copy
                System.Threading.Thread.Sleep(1000);
                File.Copy(Assembly.GetEntryAssembly().Location, update, true);
                System.Threading.Thread.Sleep(1000);
                Process.Start(update, string.Join(" ", (string[])args2.ToArray(typeof(string))) + " -delete:" + Assembly.GetEntryAssembly().Location);
                this.forceExit = true;
                Application.Exit();
                return;
            }

            if (delete != null) {
                Log("Performing update delete");
                try { System.Threading.Thread.Sleep(1000); File.Delete(delete); } catch (Exception) { }
            }

            // Set TLS 1.2
            Log("Set TLS 1.2");
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            Log("InitializeComponent()");
            InitializeComponent();
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            Translate.TranslateControl(this);
            Translate.TranslateContextMenu(this.mainContextMenuStrip);
            Translate.TranslateContextMenu(this.dialogContextMenuStrip);
            this.Opacity = 0;


            // If there is an embedded .msh file, write it out to "meshagent.msh"
            Log("Checking for embedded MSH file");
            string msh = ExeHandler.GetMshFromExecutable(Process.GetCurrentProcess().MainModule.FileName, out embeddedMshLength);
            if (msh == null) { msh = MeshCentralAgent.LoadMshFileStr(); }
            //if (msh != null) { try { File.WriteAllText(MeshCentralAgent.getSelfFilename(".msh"), msh); } catch (Exception ex) { MessageBox.Show(ex.ToString()); Application.Exit(); return; } }
            selfExecutableHashHex = ExeHandler.HashExecutable(Assembly.GetEntryAssembly().Location);

            // Check if the built-in agent will be activated
            Log("Check for built-in agent");
            currentAgentName = null;
            List<ToolStripItem> subMenus = new List<ToolStripItem>();
            string currentAgentSelection = Settings.GetRegValue("SelectedAgent", null);

            int mshCheckErr = MeshCentralAgent.checkMshStr(msh);
            if (mshCheckErr == 2)
            {
                forceExit = true;
                MessageBox.Show(string.Format(Properties.Resources.SignedExecutableServerLockError, Program.LockToHostname), Properties.Resources.MeshCentralAssistant, MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
                return;
            }
            else if (mshCheckErr == 0)
            {
                Log("Starting built-in agent");
                mcagent = new MeshCentralAgent(this, msh, "MeshCentralAssistant", selfExecutableHashHex, debug);
                mcagent.autoConnect = autoConnect;
                if (allowUseOfProxy == false) { mcagent.allowUseOfProxy = allowUseOfProxy; }
                mcagent.onStateChanged += Mcagent_onStateChanged;
                mcagent.onNotify += Mcagent_onNotify;
                mcagent.onSelfUpdate += Agent_onSelfUpdate;
                mcagent.onSessionChanged += Mcagent_onSessionChanged;
                mcagent.onUserInfoChange += Mcagent_onUserInfoChange;
                mcagent.onRequestConsent += Mcagent_onRequestConsent;
                mcagent.onSelfSharingStatus += Mcagent_onSelfSharingStatus;
                mcagent.onLogEvent += Mcagent_onLogEvent;
                currentAgentSelection = "~"; // If a built-in agent is present, always default to that on start.
                currentAgentName = "~";
                ToolStripMenuItem m = new ToolStripMenuItem();
                m.Name = "AgentSelector-~";
                m.Text = Translate.T(Properties.Resources.DirectConnect);
                m.Checked = ((currentAgentName != null) && (currentAgentName.Equals("~")));
                m.Click += agentSelection_Click;
                subMenus.Add(m);
            } else {
                MeshCentralAgent.getMshCustomization(msh, out CustomizationTitle, out CustomizationLogo);
            }
            UpdateTitle();

            // Configure system tray
            if (SystemTrayApp == false)
            {
                mainNotifyIcon.Visible = false;
                this.ShowInTaskbar = true;
                this.MinimizeBox = true;
                startVisible = true;
                this.Height += 60;
                this.Width = (this.Width * 160) / 100;
                this.FormBorderStyle = FormBorderStyle.FixedDialog;
            }
            else
            {
                // Get the list of agents on the system
                Log("Get list of background agents");
                bool directConnectSeperator = false;
                agents = MeshAgent.GetAgentInfo(selectedAgentName);
                string[] agentNames = agents.Keys.ToArray();
                if (agents.Count > 0)
                {
                    Log(string.Format("Found {0} background agent(s)", agents.Count));
                    if ((currentAgentName == null) || (currentAgentName != "~"))
                    {
                        currentAgentName = agentNames[0]; // Default
                        for (var i = 0; i < agentNames.Length; i++) { if (agentNames[i] == currentAgentSelection) { currentAgentName = agentNames[i]; } }
                    }
                    if ((agentNames.Length > 1) || ((agentNames.Length > 0) && (mcagent != null)))
                    {
                        for (var i = 0; i < agentNames.Length; i++)
                        {
                            if ((mcagent != null) && (!directConnectSeperator)) { subMenus.Add(new ToolStripSeparator()); directConnectSeperator = true; }
                            ToolStripMenuItem m = new ToolStripMenuItem();
                            m.Name = "AgentSelector-" + agentNames[i];
                            m.Text = agentNames[i];
                            m.Checked = (agentNames[i] == currentAgentName);
                            m.Click += agentSelection_Click;
                            subMenus.Add(m);
                        }
                    }
                }
                agentSelectToolStripMenuItem.DropDownItems.AddRange(subMenus.ToArray());
                agentSelectToolStripMenuItem.Visible = (subMenus.Count > 1);
                mainNotifyIcon.Visible = true;
            }

            // Load events
            LoadEventsFromFile();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            Log("MainForm_Load()");
            this.WindowState = FormWindowState.Normal;
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(Screen.PrimaryScreen.WorkingArea.Width - this.Width, Screen.PrimaryScreen.WorkingArea.Height - this.Height);
            this.Visible = startVisible;
            this.Opacity = 1;
            connectToAgent();
        }

        private void Mcagent_onLogEvent(DateTime time, string userid, string msg)
        {
            if (forceExit) return;
            LogEventStruct e = new LogEventStruct(time, userid, msg);
            userEvents.Add(e);
            AddEventToForm(e);
        }

        private void Mcagent_onRequestConsent(MeshCentralTunnel tunnel, string msg, int protocol, string userid)
        {
            if (forceExit) return;
            if (this.InvokeRequired) { this.Invoke(new MeshCentralAgent.onRequestConsentHandler(Mcagent_onRequestConsent), tunnel, msg, protocol, userid); return; }
            if ((msg == null) && (consentForm != null) && (consentForm.tunnel == tunnel))
            {
                Log("Closing consent form");
                consentForm.Close();
                consentForm = null;
            }
            else if ((consentForm == null) && (msg != null))
            {
                if ((userid != null) && (ConsentForm.autoConsent.ContainsKey(userid)))
                {
                    DateTime autoAcceptTime = ConsentForm.autoConsent[userid];
                    if (autoAcceptTime > DateTime.Now) { Log("Consent auto-accepted"); tunnel.ConsentAccepted(); return; } // Auto accept user consent
                }

                Log("Opening consent form");
                string realname = "Guest";
                Image userImage = null;
                if (userid != null)
                {
                    realname = userid.Split('/')[2];
                    if ((mcagent.usernames != null) && mcagent.usernames.ContainsKey(userid) && (mcagent.usernames[userid] != null)) { realname = mcagent.usernames[userid]; }
                    if ((mcagent.userrealname != null) && mcagent.userrealname.ContainsKey(userid) && (mcagent.userrealname[userid] != null)) { realname = mcagent.userrealname[userid]; }
                    if ((mcagent.userimages != null) && mcagent.userimages.ContainsKey(userid) && (mcagent.userimages[userid] != null)) { userImage = mcagent.userimages[userid]; }
                }
                consentForm = new ConsentForm(this);
                consentForm.userid = userid;
                consentForm.tunnel = tunnel;
                consentForm.Message = msg;
                consentForm.UserName = realname;
                consentForm.UserImage = userImage;
                consentForm.Show(this);
                consentForm.Focus();
            }
        }

        private void Mcagent_onSelfSharingStatus(bool allowed, string url)
        {
            if (forceExit) return;
            if (this.InvokeRequired) { this.Invoke(new MeshCentralAgent.onSelfSharingStatusHandler(Mcagent_onSelfSharingStatus), allowed, url); return; }
            if (guestSharingForm != null) { guestSharingForm.UpdateInfo(); }
        }

        public delegate void ShowNotificationHandler(string userid, string title, string message);

        public void ShowNotification(string userid, string title, string message)
        {
            if (forceExit) return;
            if (this.InvokeRequired) { this.Invoke(new ShowNotificationHandler(ShowNotification), userid, title, message); return; }
            Log("Show notification");
            string realname = userid.Split('/')[2];
            if ((mcagent.usernames != null) && mcagent.usernames.ContainsKey(userid) && (mcagent.usernames[userid] != null)) { realname = mcagent.usernames[userid]; }
            if ((mcagent.userrealname != null) && mcagent.userrealname.ContainsKey(userid) && (mcagent.userrealname[userid] != null)) { realname = mcagent.userrealname[userid]; }
            Image userImage = null;
            if ((mcagent.userimages != null) && mcagent.userimages.ContainsKey(userid) && (mcagent.userimages[userid] != null)) { userImage = mcagent.userimages[userid]; }
            if (notifyForm == null) { notifyForm = new NotifyForm(this); notifyForm.Show(this); }
            notifyForm.userid = userid;
            notifyForm.Message = message;
            notifyForm.UserName = realname;
            notifyForm.UserImage = userImage;
            if ((title != null) && (title.Length > 0)) { notifyForm.Title = title; }
            notifyForm.Focus();
        }

        private void Mcagent_onSessionChanged()
        {
            if (forceExit) return;
            if (InvokeRequired) { Invoke(new MeshCentralAgent.onSessionChangedHandler(Mcagent_onSessionChanged)); return; }
            Log("onSessionChanged");
            updateBuiltinAgentStatus();
        }

        private void Mcagent_onUserInfoChange(string userid, int change)
        {
            if (forceExit) return;
            if (InvokeRequired) { Invoke(new MeshCentralAgent.onUserInfoChangeHandler(Mcagent_onUserInfoChange), userid, change); return; }
            Log(string.Format("onUserInfoChange {0}, {1}", userid, change));
            updateBuiltinAgentStatus();

            // If the notification or consent dialog is showing, check if we can update the real name and/or image
            if ((notifyForm != null) && (notifyForm.userid == userid))
            {
                if ((mcagent.userimages != null) && mcagent.userimages.ContainsKey(userid) && (mcagent.userimages[userid] != null)) { notifyForm.UserImage = mcagent.userimages[userid]; }
                if ((mcagent.userrealname != null) && mcagent.userrealname.ContainsKey(userid) && (mcagent.userrealname[userid] != null)) { notifyForm.UserName = mcagent.userrealname[userid]; }
                else if ((mcagent.usernames != null) && mcagent.usernames.ContainsKey(userid) && (mcagent.usernames[userid] != null)) { notifyForm.UserName = mcagent.usernames[userid]; }
            }
            if ((consentForm != null) && (consentForm.userid == userid))
            {
                if ((mcagent.userimages != null) && mcagent.userimages.ContainsKey(userid) && (mcagent.userimages[userid] != null)) { consentForm.UserImage = mcagent.userimages[userid]; }
                if ((mcagent.userrealname != null) && mcagent.userrealname.ContainsKey(userid) && (mcagent.userrealname[userid] != null)) { consentForm.UserName = mcagent.userrealname[userid]; }
                else if ((mcagent.usernames != null) && mcagent.usernames.ContainsKey(userid) && (mcagent.usernames[userid] != null)) { notifyForm.UserName = mcagent.usernames[userid]; }
            }
        }

        private void Mcagent_onNotify(string userid, string title, string msg)
        {
            if (forceExit) return;
            if (InvokeRequired) { Invoke(new MeshCentralAgent.onNotifyHandler(Mcagent_onNotify), userid, title, msg); return; }
            ShowNotification(userid, title, msg);
            //MessageBox.Show(msg, title);
            //mainNotifyIcon.BalloonTipText = title + " - " + msg;
            //mainNotifyIcon.ShowBalloonTip(2000);
        }

        private void Mcagent_onStateChanged(int state)
        {
            if (forceExit) return;
            if (InvokeRequired) { Invoke(new MeshCentralAgent.onStateChangedHandler(Mcagent_onStateChanged), state); return; }
            Log(string.Format("Mcagent_onStateChanged {0}", state));
            if (state == 0) { PrivacyBarClose(); }
            updateBuiltinAgentStatus();
        }

        private void agentSelection_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem menu = (ToolStripMenuItem)sender;
            if (currentAgentName == menu.Name.Substring(14)) return;
            currentAgentName = menu.Name.Substring(14);
            foreach (Object obj in agentSelectToolStripMenuItem.DropDownItems) {
                if (obj.GetType() == typeof(ToolStripMenuItem))
                {
                    ToolStripMenuItem submenu = (ToolStripMenuItem)obj;
                    submenu.Checked = (submenu.Name.Substring(14) == currentAgentName);
                }
            }
            UpdateTitle();
            Log(string.Format("agentSelection_Click {0}", currentAgentName));
            connectToAgent();
        }

        private string[] getSessionUserIdList()
        {
            ArrayList r = new ArrayList();
            if (mcagent.DesktopSessions != null) { foreach (string u in mcagent.DesktopSessions.Keys) { if (!r.Contains(u)) { r.Add(u); } } }
            if (mcagent.TerminalSessions != null) { foreach (string u in mcagent.TerminalSessions.Keys) { if (!r.Contains(u)) { r.Add(u); } } }
            if (mcagent.FilesSessions != null) { foreach (string u in mcagent.FilesSessions.Keys) { if (!r.Contains(u)) { r.Add(u); } } }
            if (mcagent.TcpSessions != null) { foreach (string u in mcagent.TcpSessions.Keys) { if (!r.Contains(u)) { r.Add(u); } } }
            if (mcagent.UdpSessions != null) { foreach (string u in mcagent.UdpSessions.Keys) { if (!r.Contains(u)) { r.Add(u); } } }
            return (string[])r.ToArray(typeof(string));
        }

        private string[] getSessionUserIdList2()
        {
            ArrayList r = new ArrayList();
            if (agent.DesktopSessions != null) { foreach (string u in agent.DesktopSessions.Keys) { if (!r.Contains(u)) { r.Add(u); } } }
            if (agent.TerminalSessions != null) { foreach (string u in agent.TerminalSessions.Keys) { if (!r.Contains(u)) { r.Add(u); } } }
            if (agent.FilesSessions != null) { foreach (string u in agent.FilesSessions.Keys) { if (!r.Contains(u)) { r.Add(u); } } }
            if (agent.TcpSessions != null) { foreach (string u in agent.TcpSessions.Keys) { if (!r.Contains(u)) { r.Add(u); } } }
            if (agent.UdpSessions != null) { foreach (string u in agent.UdpSessions.Keys) { if (!r.Contains(u)) { r.Add(u); } } }
            return (string[])r.ToArray(typeof(string));
        }


        public Image RoundCorners(Image StartImage, int CornerRadius, Color BackgroundColor)
        {
            CornerRadius *= 2;
            Bitmap RoundedImage = new Bitmap(StartImage.Width, StartImage.Height);
            using (Graphics g = Graphics.FromImage(RoundedImage))
            {
                g.Clear(BackgroundColor);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Brush brush = new TextureBrush(StartImage);
                GraphicsPath gp = new GraphicsPath();
                gp.AddArc(0, 0, CornerRadius, CornerRadius, 180, 90);
                gp.AddArc(0 + RoundedImage.Width - CornerRadius, 0, CornerRadius, CornerRadius, 270, 90);
                gp.AddArc(0 + RoundedImage.Width - CornerRadius, 0 + RoundedImage.Height - CornerRadius, CornerRadius, CornerRadius, 0, 90);
                gp.AddArc(0, 0 + RoundedImage.Height - CornerRadius, CornerRadius, CornerRadius, 90, 90);
                g.FillPath(brush, gp);
                return RoundedImage;
            }
        }

        private void updateBuiltinAgentStatus()
        {
            if (mcagent == null) { updateSoftwareToolStripMenuItem1.Visible = updateSoftwareToolStripMenuItem.Visible = false; return; }
            helpRequested = (mcagent.HelpRequest != null);
            if (mcagent.state != 3)
            {
                updateSoftwareToolStripMenuItem1.Visible = updateSoftwareToolStripMenuItem.Visible = false; // If not connected, don't offer auto-update option.
                if (guestSharingForm != null) { guestSharingForm.Close(); }
            }

            if (mcagent.autoConnect)
            {
                // In auto connect mode, we can only request help when connected.
                if (mcagent.state == 0) { stateLabel.Text = Translate.T(Properties.Resources.Connecting); requestHelpButton.Text = Translate.T(Properties.Resources.RequestHelp); helpRequested = false; }
                if (mcagent.state == 1) { stateLabel.Text = Translate.T(Properties.Resources.Connecting); requestHelpButton.Text = Translate.T(Properties.Resources.RequestHelp); }
                if (mcagent.state == 2) { stateLabel.Text = Translate.T(Properties.Resources.Authenticating); requestHelpButton.Text = Translate.T(Properties.Resources.RequestHelp); }
                if (mcagent.state == 3) {
                    if (helpRequested) {
                        stateLabel.Text = Translate.T(Properties.Resources.HelpRequested);
                        requestHelpButton.Text = Translate.T(Properties.Resources.CancelHelpRequest);
                    } else {
                        stateLabel.Text = Translate.T(Properties.Resources.Connected);
                        requestHelpButton.Text = Translate.T(Properties.Resources.RequestHelp);
                    }
                }
                Agent_onSessionChanged();
                requestHelpButton.Enabled = (mcagent.state == 3);

                // Update context menu
                requestHelpToolStripMenuItem.Enabled = true;
                requestHelpToolStripMenuItem.Visible = (mcagent.state == 3) && !helpRequested;
                cancelHelpRequestToolStripMenuItem.Visible = ((mcagent.state == 3) && helpRequested);

                // Update image
                if (mcagent.state == 3) {
                    if (helpRequested) { setUiImage(uiImage.Question); } else { setUiImage(uiImage.Green); }
                } else {
                    setUiImage(uiImage.Red);
                }
            }
            else
            {
                // When not in auto-connect mode, we only connect when requesting help.
                if (mcagent.state == 0) { stateLabel.Text = Translate.T(Properties.Resources.Disconnected); requestHelpButton.Text = Translate.T(Properties.Resources.RequestHelp); }
                if (mcagent.state == 1) { stateLabel.Text = Translate.T(Properties.Resources.Connecting); requestHelpButton.Text = Translate.T(Properties.Resources.CancelHelpRequest); }
                if (mcagent.state == 2) { stateLabel.Text = Translate.T