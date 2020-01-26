﻿using Microsoft.Azure.Management.Compute.Fluent;
using Microsoft.Azure.Management.Compute.Fluent.Models;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.Network.Fluent.Models;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core.ResourceActions;
using System;
using System.Collections.Generic;

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

            try
            {
                if (azure.ResourceGroups.Contain(resourceGroupName))
                {
                    Console.WriteLine($"Resource Group {resourceGroupName} already exits.  Deleting the resource group...");
                    azure.ResourceGroups.DeleteByName(resourceGroupName);
                    Console.WriteLine($"{resourceGroupName} deleted...");
                }

                Console.WriteLine($"Creating Resource Grouop: {resourceGroupName}...");
                var resourceGroup = azure.ResourceGroups
                    .Define(resourceGroupName)
                    .WithRegion(deploymentRegion)
                    .Create();

                Console.WriteLine($"Creating Network Security Grouop: {networkSecurityGroupName}...");
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

                var machines = new List<ICreatable<IVirtualMachine>>();

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

                var startTime = DateTimeOffset.Now.UtcDateTime;

                Console.WriteLine($"Creating {vmCount} virtual machines in parallel...");
                var vms = azure.VirtualMachines.Create(machines.ToArray());

                foreach (var instance in vms)
                {
                    Console.WriteLine(instance.Id);
                }
                var endTime = DateTimeOffset.Now.UtcDateTime;

                Console.WriteLine($"Created VM: took {(endTime - startTime).TotalSeconds} seconds");
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex);
                azure.ResourceGroups.DeleteByName(resourceGroupName);
                Console.WriteLine($"Deleted resource group : {resourceGroupName}");
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
                 
                var credentials = SdkContext.AzureCredentialsFactory.FromFile(@"D:\Documents\클라우드\OneDrive - Microsoft\프로젝트\암호파일\azure-service-principal.auth");
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
