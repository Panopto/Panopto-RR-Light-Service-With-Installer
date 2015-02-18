using System.ComponentModel;
using System.Configuration.Install;

namespace RRLightProgram
{
    [RunInstaller(true)]
    public partial class ProjectInstaller : System.Configuration.Install.Installer
    {
        public ProjectInstaller()
        {
            InitializeComponent();
        }

        private void ServiceInstaller_AfterInstall(object sender, InstallEventArgs e)
        {
        }

        private void ProcessInstaller_AfterInstall(object sender, InstallEventArgs e)
        {
        }
    }
}