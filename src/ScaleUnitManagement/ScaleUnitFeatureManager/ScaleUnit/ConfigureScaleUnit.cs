using System.Threading.Tasks;
using ScaleUnitManagement.ScaleUnitFeatureManager.Common;
using ScaleUnitManagement.ScaleUnitFeatureManager.Utilities;
using ScaleUnitManagement.Utilities;

namespace ScaleUnitManagement.ScaleUnitFeatureManager.ScaleUnit
{
    public class ConfigureScaleUnit : IScaleUnitStep
    {
        public string Label()
        {
            return "Configure Scale unit";
        }

        public float Priority()
        {
            return 2F;
        }

        public Task Run()
        {
            ScaleUnitInstance scaleUnit = Config.FindScaleUnitWithId(ScaleUnitContext.GetScaleUnitId());

            if (scaleUnit.EnvironmentType == EnvironmentType.VHD || Config.UseSingleOneBox())
            {
                // Update hosts file
                using (var hosts = new Hosts())
                {
                    hosts.AddMapping(scaleUnit.IpAddress, scaleUnit.DomainSafe());
                    hosts.AddMapping(Config.HubScaleUnit().IpAddress, Config.HubScaleUnit().DomainSafe());
                }

                IISAdministrationHelper.CreateSite(
                    siteName: scaleUnit.SiteName(),
                    siteRoot: scaleUnit.SiteRoot(),
                    bindingInformation: scaleUnit.IpAddress + ":443:" + scaleUnit.DomainSafe(),
                    certSubject: scaleUnit.DomainSafe(),
                    appPoolName: scaleUnit.AppPoolName());
            }

            using (var webConfig = new WebConfig())
            {
                //SharedWebConfig.Configure(webConfig);

                if (scaleUnit.EnvironmentType == EnvironmentType.VHD || Config.UseSingleOneBox())
                {
                    webConfig.UpdateXElementIfExists("Infrastructure.FullyQualifiedDomainName", scaleUnit.DomainSafe());
                    webConfig.UpdateXElementIfExists("Infrastructure.HostName", scaleUnit.DomainSafe());
                    webConfig.UpdateXElementIfExists("Infrastructure.HostedServiceName", scaleUnit.ScaleUnitUrlName());

                    string scaleUnitUrl = scaleUnit.Endpoint() + "/";
                    webConfig.UpdateXElementIfExists("Infrastructure.HostUrl", scaleUnitUrl);
                    webConfig.UpdateXElementIfExists("Infrastructure.SoapServicesUrl", scaleUnitUrl);

                    webConfig.UpdateXElementIfExists("DataAccess.Database", scaleUnit.AxDbName);

                    webConfig.AddValidAudiences(scaleUnit);
                }
            }

            WifServiceConfig.Update();

            if (Config.UseSingleOneBox())
            {
                CreateScaleUnitBatchService(scaleUnit);
            }

            return Task.CompletedTask;
        }

        private static void CreateScaleUnitBatchService(ScaleUnitInstance scaleUnit)
        {
            CheckForAdminAccess.ValidateCurrentUserIsProcessAdmin();

            string cmd = $@"
                if (Get-Service '{scaleUnit.BatchServiceName()}' -ErrorAction SilentlyContinue) {{
                    . $env:systemroot\system32\sc.exe delete {scaleUnit.BatchServiceName()};
                    Write-Host 'Waiting 10 seconds for Service Deletion to propagate...'
                    Start-Sleep -Seconds 10
                }}

                $secpasswd = (new-object System.Security.SecureString);
                $creds = New-Object System.Management.Automation.PSCredential ('NT AUTHORITY\NETWORK SERVICE', $secpasswd);
                New-Service -Name '{scaleUnit.BatchServiceName()}' -BinaryPathName '{scaleUnit.DynamicsBatchExePath()} -service {scaleUnit.WebConfigPath()}' -credential $creds -startupType Manual;
            ";

            var ce = new CommandExecutor(cmd);
            ce.RunCommand();
        }
    }
}
