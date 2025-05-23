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
        private System.Threading.Timer checkTimer;
        
        public MeshService()
        {
            ServiceName = "MeshCentralAssistant";
            checkTimer = new System.Threading.Timer(CheckMessages, null, 1000, 1000);
        }

        private void CheckMessages(object state)
        {
            try
            {
                // Check for control messages from GUI instances
                string[] messages = System.IO.Directory.GetFiles(System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MeshCentralAssistant"), "*.ctrl");
                
                foreach (string msg in messages)
                {
                    try
                    {
                        string command = System.IO.File.ReadAllText(msg);
                        if (command == "KILL_AGENT")
                        {
                            // Find and kill MeshAgent processes
                            foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcessesByName("MeshAgent"))
                            {
                                try { p.Kill(); } catch { }
                            }
                        }
                        System.IO.File.Delete(msg);
                    }
                    catch { }
                }
            }
            catch { }
        }

        protected override void OnStart(string[] args)
        {
            // Create main form in service context
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            mainForm = new MainForm(args);

            // Create control message directory
            try
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MeshCentralAssistant");
                if (!System.IO.Directory.Exists(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                }
            }
            catch { }
        }

        protected override void OnStop()
        {
            if (checkTimer != null)
            {
                checkTimer.Dispose();
                checkTimer = null;
            }

            if (mainForm != null)
            {
                mainForm.Dispose();
                mainForm = null;
            }
        }
    }
}