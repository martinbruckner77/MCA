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
        
        public MeshService()
        {
            ServiceName = "MeshCentralAssistant";
        }

        protected override void OnStart(string[] args)
        {
            // Create main form in service context
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            mainForm = new MainForm(args);
        }

        protected override void OnStop()
        {
            if (mainForm != null)
            {
                mainForm.Dispose();
                mainForm = null;
            }
        }
    }
}