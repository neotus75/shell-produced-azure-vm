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

namespace shell_produced_azure_vm
{
    class Program
    {
        static void CreateVMs(IAzure azure)
        {
            Console.Write("Number of VMs to be created: ");
            int vmCount = int.Parse(Console.ReadLine());

            Console.Write("Enter Admin Login: ");
            string userName = Console.ReadLine();

            Console.Write("Enter Admin Password: ");
            string password = Console.ReadLine();

            Console.Write("Enter Root Domain Name: ");
            string domainName = Console.ReadLine();

            try
            {
                if (azure.ResourceGroups.Contain(resourceGroupName))
                {
                    Console.WriteLine($"Resource Group {resourceGroupName} already exits.  Deleting the resource group...");
                    azure.ResourceGroups.DeleteByName(resourceGroupName);
                    Console.WriteLine($"{resourceGroupName} deleted...");
                }

                // create resource group
                Console.WriteLine($"Creating Resource Group: {resourceGroupName}...");
                var resourceGroup = azure.ResourceGroups
                    .Define(resourceGroupName)
                    .WithRegion(deploymentRegion)
                    .Create();

                // create DNS Zone
                Console.WriteLine($"Creating Azure DNS Zone: {domainName}...");
                var dnsZone = azure.DnsZones
                    .Define(domainName)
                    .WithExistingResourceGroup(resourceGroupName)
                    .Create();

                // create Network Security Group
                Console.WriteLine($"Creating Network Security Group: {networkSecurityGroupName}...");
                var networkSecurityGroups = azure.NetworkSecurityGroups
                     .Define(networkSecurityGroupName)
                         .WithRegion(deploymentRegion)
                         .WithExistingResourceGroup(resourceGroup)

                     .DefineRule("ALLOW-RDP")
                         .AllowInbound()
                         .FromAnyAddress()
                         .FromAnyPort()
                         .ToAnyAddress()
                         .ToPort(3389)
                         .WithProtocol(SecurityRuleProtocol.Tcp)
                         .WithPriority(100)
                         .WithDescription("Allow Windows RDP")
                     .Attach()
                     
                     .DefineRule("ALLOW-WINRM-HTTP")
                        .AllowInbound()
                        .FromAnyAddress()
                        .FromAnyPort()
                        .ToAnyAddress()
                        .ToPort(5985)
                        .WithProtocol(SecurityRuleProtocol.Tcp)
                        .WithPriority(110)
                        .WithDescription("Allow WinRM HTTP Port")
                    .Attach()

                    .DefineRule("ALLOW-WINRM-HTTPS")
                        .AllowInbound()
                        .FromAnyAddress()
                        .FromAnyPort()
                        .ToAnyAddress()
                        .ToPort(5986)
                        .WithProtocol(SecurityRuleProtocol.Tcp)
                        .WithPriority(120)
                        .WithDescription("Allow WinRM HTTPS Port")
                    .Attach()

                    .DefineRule("ALLOW-SSH")
                        .AllowInbound()
                        .FromAnyAddress()
                        .FromAnyPort()
                        .ToAnyAddress()
                        .ToPort(22)
                        .WithProtocol(SecurityRuleProtocol.Tcp)
                        .WithPriority(130)
                        .WithDescription("Allow SSH Port for Linux Hosts")
                    .Attach()

                    .DefineRule("ALLOW-CHAT-SERVER")
                        .AllowInbound()
                        .FromAnyAddress()
                        .FromAnyPort()
                        .ToAnyAddress()
                        .ToPort(56675)
                        .WithProtocol(SecurityRuleProtocol.Tcp)
                        .WithPriority(140)
                        .WithDescription("Allow custom Chat Server Port")
                    .Attach()

                    .Create();

                Console.WriteLine($"Creating Network (Virtual NET): {vnetName} with Subnet: {subnetName}...");
                var vnet = azure.Networks
                    .Define(vnetName)
                    .WithRegion(deploymentRegion)
                    .WithExistingResourceGroup(resourceGroupName)
                    .WithAddressSpace("192.168.1.0/24")
                    .WithSubnet(subnetName, "192.168.1.0/24")
                    .Create();

                Console.WriteLine($"Creating Storage Account: {storageAccountName}...");
                var storage = azure.StorageAccounts
                    .Define(storageAccountName)
                    .WithRegion(deploymentRegion)
                    .WithExistingResourceGroup(resourceGroup);

                Console.WriteLine("Preparing to create Windows VMs...");
                var machines = new List<ICreatable<IVirtualMachine>>();
                var fronts = new List<IHasNetworkInterfaces>();

                for (int i = 1; i <= vmCount; i++)
                {
                    var vmname = $"PS-{i.ToString("D2")}";

                    Console.WriteLine($"Creating Public IP Address: pip-{i.ToString("D2")}...");
                    var pip = azure.PublicIPAddresses
                        .Define($"pip-{i.ToString("D2")}")
                        .WithRegion(deploymentRegion)
                        .WithExistingResourceGroup(resourceGroup)
                        .WithStaticIP()
                        .WithLeafDomainLabel($"{vmname}")
                        .WithSku(PublicIPSkuType.Standard)
                        .Create();
                    
                    Console.WriteLine($"Creating Network Interface: nic-{i.ToString("D2")}...");
                    var nic = azure.NetworkInterfaces
                        .Define($"nic-{i.ToString("D2")}")
                        .WithRegion(deploymentRegion)
                        .WithExistingResourceGroup(resourceGroup)
                        .WithExistingPrimaryNetwork(vnet)
                        .WithSubnet(subnetName)
                        .WithPrimaryPrivateIPAddressDynamic()
                        .WithExistingPrimaryPublicIPAddress(pip)
                        .WithExistingNetworkSecurityGroup(networkSecurityGroups)
                        .WithIPForwarding()
                        .Create();

                    var vm = azure.VirtualMachines
                        .Define(vmname)
                        .WithRegion(deploymentRegion)
                        .WithExistingResourceGroup(resourceGroup)
                        .WithExistingPrimaryNetworkInterface(nic)
                        .WithLatestWindowsImage("MicrosoftWindowsServer", "WindowsServer", "2016-Datacenter")
                        .WithAdminUsername(userName)
                        .WithAdminPassword(password)
                        .WithComputerName(vmname)
                        .WithSize(VirtualMachineSizeTypes.StandardDS3V2)
                        .WithNewStorageAccount(storage);
                    
                    machines.Add(vm);
                }

                // add the virtual machines to load balancer
                var frontEndVms = azure.VirtualMachines.Create(machines.ToArray());
               
                foreach (var instance in frontEndVms)
                {
                    fronts.Add(instance);
                }

                // always create 2 linux VMs 
                Console.WriteLine("Preparing to create 2 Linux VMs...");
                machines.Clear();

                for (int i = 1; i <= 2; i++)
                {
                    var vmname = $"LX-{i.ToString("D2")}";

                    Console.WriteLine($"Creating Public IP Address: pip-lx-{i.ToString("D2")}...");
                    var pip = azure.PublicIPAddresses
                        .Define($"pip-lx-{i.ToString("D2")}")
                        .WithRegion(deploymentRegion)
                        .WithExistingResourceGroup(resourceGroup)
                        .WithStaticIP()
                        .WithLeafDomainLabel($"{vmname}")
                        .WithSku(PublicIPSkuType.Standard)
                        .Create();

                    Console.WriteLine($"Creating Network Interface: nic-lx-{i.ToString("D2")}...");
                    var nic = azure.NetworkInterfaces
                        .Define($"nic-lx-{i.ToString("D2")}")
                        .WithRegion(deploymentRegion)
                        .WithExistingResourceGroup(resourceGroup)
                        .WithExistingPrimaryNetwork(vnet)
                        .WithSubnet(subnetName)
                        .WithPrimaryPrivateIPAddressDynamic()
                        .WithExistingPrimaryPublicIPAddress(pip)
                        .WithExistingNetworkSecurityGroup(networkSecurityGroups)
                        .WithIPForwarding()
                        .Create();

                    var vm = azure.VirtualMachines
                        .Define(vmname)
                        .WithRegion(deploymentRegion)
                        .WithExistingResourceGroup(resourceGroup)
                        .WithExistingPrimaryNetworkInterface(nic)
                        .WithPopularLinuxImage(KnownLinuxVirtualMachineImage.UbuntuServer16_04_Lts)
                        .WithRootUsername(userName)
                        .WithRootPassword(password)
                        .WithComputerName(vmname)
                        .WithSize(VirtualMachineSizeTypes.StandardDS3V2)
                        .WithNewStorageAccount(storage);

                    machines.Add(vm);
                }

                var startTime = DateTimeOffset.Now.UtcDateTime;

                Console.WriteLine($"Creating {vmCount + 2} virtual machines in parallel...");
                var linuxVms = azure.VirtualMachines.Create(machines.ToArray());
                Console.WriteLine($"Creating {vmCount + 2} virtual machines in parallel completed...");

                // update DNS entries...
                Console.WriteLine($"Updating DNS Entries in {domainName}...");
              

                foreach (var instance in frontEndVms)
                {
                    dnsZone = dnsZone.Update()
                        .DefineARecordSet(instance.Name)
                        .WithIPv4Address(instance.GetPrimaryPublicIPAddress().IPAddress)
                        .Attach()
                        .Apply();

                    Console.WriteLine("\n" + instance.Id);
                }
                var endTime = DateTimeOffset.Now.UtcDateTime;

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
                        .Attach()
                    .WithSku(LoadBalancerSkuType.Standard)
                    .Create();

                alb.Update().UpdateBackend("backend-pool").WithExistingVirtualMachines(fronts).Parent().Apply();

                Console.WriteLine($"Created VM: took {(endTime - startTime).TotalSeconds} seconds");
            }
            catch(Exception ex)
            {
                try
                {
                    Console.WriteLine(ex);
                    azure.ResourceGroups.DeleteByName(resourceGroupName);
                    Console.WriteLine($"Deleted resource group : {resourceGroupName}");
                }
                catch(Exception)
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

        private const string resourceGroupName = "shell-produced-resources";
        private const string vnetName = "sprvnet";
        private const string subnetName = "sprsubnet";
        private const string networkSecurityGroupName = "sprnsg";
        private const string storageAccountName = "sprstorageaccount";
        private static Region deploymentRegion = Region.KoreaCentral;
    }
}
