using Microsoft.Azure.Management.Compute.Fluent;
using Microsoft.Azure.Management.Compute.Fluent.Models;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.Network.Fluent.Models;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core.ResourceActions;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Management.Network.Fluent;

namespace code_produced_load_balancer
{
    class Program
    {
        static void CreateVMs(IAzure azure)
        {
            try
            {
                Console.WriteLine($"Creating Resource Group: {resourceGroupName}...");
                var resourceGroup = azure.ResourceGroups
                    .Define(resourceGroupName)
                    .WithRegion(deploymentRegion)
                    .Create();

                Console.WriteLine($"Creating Public IP Address: pip-alb-01...");

                var albip = azure.PublicIPAddresses
                    .Define($"alb-pip-01")
                    .WithRegion(deploymentRegion)
                    .WithExistingResourceGroup(resourceGroup)
                    .WithStaticIP()
                    .WithLeafDomainLabel("alb-pip-01")
                    .WithSku(PublicIPSkuType.Standard)
                    .Create();

                var alb = azure.LoadBalancers
                    .Define("alb-01")
                    .WithRegion(deploymentRegion)
                    .WithExistingResourceGroup(resourceGroupName)

                    .DefineLoadBalancingRule("alb-rule")
                    .WithProtocol(TransportProtocol.Tcp)
                    .FromExistingPublicIPAddress(albip)
                    .FromFrontendPort(56675)
                    .ToBackend("backend-pool")
                    .WithLoadDistribution(LoadDistribution.Default)
                    .WithProbe("alb-probe")
                    .Attach()

                    .DefineTcpProbe("alb-probe")
                    .WithPort(56675)
                    .WithIntervalInSeconds(300)
                    .WithNumberOfProbes(3)
                    .Attach()
                    .DefineBackend("backend-pool")
                    .WithExistingVirtualMachines()
                    .WithExistingVirtualMachines()
                    .WithExistingVirtualMachines()
                    .Attach()
                    .WithSku(LoadBalancerSkuType.Standard)
                    .Create();
            }
            catch (Exception ex)
            {
                try
                {
                    Console.WriteLine(ex);
                    azure.ResourceGroups.DeleteByName(resourceGroupName);
                    Console.WriteLine($"Deleted resource group : {resourceGroupName}");
                }
                catch (Exception)
                {
                    Console.WriteLine("Did not create any resources in Azure. No clean up is needed.");
                }
            }
        }
        static void Main(string[] args)
        {
            try
            {
                /* 
                 * TO CREATE AUTH FILE
                 * 1) az login
                 * 2) az account set --subscription <name or id>
                 * 3) az ad sp create-for-rbac --sdk-auth > azure-service-principal.auth
                 */

                var credentials = SdkContext.AzureCredentialsFactory.FromFile(@"C:\Users\Patrick Shim\Documents\클라우드\OneDrive - Microsoft\프로젝트\암호파일\azure-service-principal.auth");
                var azure = Azure.Configure()
                    .WithLogLevel(HttpLoggingDelegatingHandler.Level.Basic)
                    .Authenticate(credentials)
                    .WithDefaultSubscription();
                Console.WriteLine("Selected Subscription: {0}", azure.SubscriptionId);
                CreateVMs(azure);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        private const string resourceGroupName = "code-produced-resources";
        private const string vnetName = "cprvnet";
        private const string subnetName = "cprsubnet";
        private const string networkSecurityGroupName = "cprnsg";
        private const string storageAccountName = "cprstorageaccount";
        private static Region deploymentRegion = Region.KoreaCentral;
    }
}
