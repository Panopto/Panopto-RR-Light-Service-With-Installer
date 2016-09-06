namespace RRLightProgram
{
    partial class ProjectInstaller
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.ProcessInstaller = new System.ServiceProcess.ServiceProcessInstaller();
            this.ServiceInstaller = new System.ServiceProcess.ServiceInstaller();
            // 
            // ProcessInstaller
            // 
            this.ProcessInstaller.Account = System.ServiceProcess.ServiceAccount.LocalSystem;
            this.ProcessInstaller.Password = null;
            this.ProcessInstaller.Username = null;
            this.ProcessInstaller.AfterInstall += new System.Configuration.Install.InstallEventHandler(this.ProcessInstaller_AfterInstall);
            // 
            // ServiceInstaller
            // 
            this.ServiceInstaller.DelayedAutoStart = true;
            this.ServiceInstaller.Description = "Service to control external light and button device for Panopto Remote Recorder";
            this.ServiceInstaller.DisplayName = "Panopto RR Light Service";
            this.ServiceInstaller.ServiceName = "PanoptoRRLightService";
            this.ServiceInstaller.ServicesDependedOn = new string[] {
        "PanoptoRemoteRecorderService"};
            this.ServiceInstaller.StartType = System.ServiceProcess.ServiceStartMode.Automatic;
            this.ServiceInstaller.AfterInstall += new System.Configuration.Install.InstallEventHandler(this.ServiceInstaller_AfterInstall);
            // 
            // ProjectInstaller
            // 
            this.Installers.AddRange(new System.Configuration.Install.Installer[] {
            this.ProcessInstaller,
            this.ServiceInstaller});

        }

        #endregion

        private System.ServiceProcess.ServiceProcessInstaller ProcessInstaller;
        private System.ServiceProcess.ServiceInstaller ServiceInstaller;
    }
}