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
            try
            {
                pipeServer = new System.IO.Pipes.NamedPipeServerStream("MeshAssistantControl", System.IO.Pipes.PipeDirection.In);
                pipeServer.BeginWaitForConnection(new AsyncCallback(PipeConnectionCallback), null);
            }
            catch (Exception) { }
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
                    // Kill MeshAgent.exe process
                    foreach (var process in System.Diagnostics.Process.GetProcessesByName("MeshAgent"))
                    {
                        try { process.Kill(); } catch (Exception) { }
                    }
                }
                
                pipeServer.Dispose();
                if (!stopRequested) { StartPipeServer(); }
            }
            catch (Exception) 
            {
                try { pipeServer.Dispose(); } catch (Exception) { }
                if (!stopRequested) { StartPipeServer(); }
            }
        }

        protected override void OnStart(string[] args)
        {
            stopRequested = false;
            StartPipeServer();

            // Create main form in service context
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            mainForm = new MainForm(args);
        }

        protected override void OnStop()
        {
            stopRequested = true;
            try { pipeServer.Dispose(); } catch (Exception) { }

            if (mainForm != null)
            {
                mainForm.Dispose();
                mainForm = null;
            }
        }
    }
}