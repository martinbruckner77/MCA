using System;
using System.ServiceProcess;
using System.Configuration.Install;
using System.ComponentModel;

namespace MeshAssistant
{
    [RunInstaller(true)]
    public class MeshServiceInstaller : Installer
    {
        private ServiceInstaller serviceInstaller;
        private ServiceProcessInstaller processInstaller;

        public MeshServiceInstaller()
        {
            processInstaller = new ServiceProcessInstaller();
            serviceInstaller = new ServiceInstaller();

            processInstaller.Account = ServiceAccount.LocalSystem;
            serviceInstaller.StartType = ServiceStartMode.Automatic;
            serviceInstaller.ServiceName = "MeshCentralAssistant";
            serviceInstaller.Description = "MeshCentral Assistant Service for remote management and support";
            
            Installers.Add(serviceInstaller);
            Installers.Add(processInstaller);
        }
    }

    public class MeshService : ServiceBase
    {
        private MainForm mainForm;
        private System.IO.Pipes.NamedPipeServerStream pipeServer;
        private bool stopRequested = false;
        
        public MeshService()
        {
            ServiceName = "MeshCentralAssistant";
        }

        private void StartPipeServer()
        {
            pipeServer = new System.IO.Pipes.NamedPipeServerStream("MeshAssistantControl", System.IO.Pipes.PipeDirection.In);
            pipeServer.BeginWaitForConnection(new AsyncCallback(PipeConnectionCallback), null);
        }

        private void PipeConnectionCallback(IAsyncResult ar)
        {
            try
            {
                pipeServer.EndWaitForConnection(ar);
                var reader = new System.IO.StreamReader(pipeServer);
                string command = reader.ReadLine();
                
                if (command == "STOP_AGENT")
                {
                    System.Diagnostics.Process[] procs = System.Diagnostics.Process.GetProcessesByName("MeshAgent");
                    foreach (System.Diagnostics.Process proc in procs)
                    {
                        try { proc.Kill(); } catch { }
                    }
                }
                
                pipeServer.Dispose();
                if (!stopRequested) { StartPipeServer(); }
            }
            catch { }
        }

        protected override void OnStart(string[] args)
        {
            // Create main form in service context
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            mainForm = new MainForm(args);
            StartPipeServer();
        }

        protected override void OnStop()
        {
            stopRequested = true;
            if (pipeServer != null)
            {
                pipeServer.Dispose();
                pipeServer = null;
            }
            if (mainForm != null)
            {
                mainForm.Dispose();
                mainForm = null;
            }
        }
    }
}