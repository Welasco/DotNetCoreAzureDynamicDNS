using System;
using System.Collections.Generic;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.IO;
using DotNetCoreAzureDynamicDNS.Model;
using System.Net.Http;
using System.Threading;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.Azure.Management.Dns.Fluent;
using System.Net.Sockets;
using System.Net;

namespace DotNetCoreAzureDynamicDNS
{
    // Reference:
    // https://github.com/Azure-Samples/dns-dotnet-host-and-manage-your-domains/blob/master/Program.cs
    // https://azure.microsoft.com/en-us/resources/samples/dns-dotnet-host-and-manage-your-domains/
    // https://github.com/Azure/azure-sdk-for-net/tree/master/sdk/dns/Microsoft.Azure.Management.Dns
    // https://github.com/Azure-Samples/dns-dotnet-host-and-manage-your-domains/blob/master/Common/Utilities.cs 
    // https://docs.microsoft.com/en-us/dotnet/api/overview/azure/dns/management?view=azure-dotnet
    class DynamicDNS
    {

        private readonly ILogger _logger;
        public AppSettings _appsetting { get; set; }
        HttpClient httpClient = new HttpClient();
        AzureCredentials cred;
        private bool _checkARecordExist = false;
        private string oldpublicIp = String.Empty;

        public DynamicDNS(ILogger<DynamicDNS> logger)
        {
            _logger = logger;

            IConfiguration configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddEnvironmentVariables(prefix: "AZUREDYNDNS_")
                .Build();
            var appsetting = new AppSettings();
            configuration.Bind(appsetting);

            _appsetting = appsetting;

        }

        public void Run()
        {
            _logger.LogInformation(DateTime.Now + " Run - Running IP verification loop");


            while (true)
            {
                updateDNS();
                int waittime = _appsetting.updateinterval * 60000;
                _logger.LogInformation(DateTime.Now + " Run - Holding loop for " + _appsetting.updateinterval + " minutes");
                Thread.Sleep(waittime);
            }
        }

        public void updateDNS()
        {
            _logger.LogInformation(DateTime.Now + " updateDNS - Checking if Public IP was changed");
            // string oldpublicIp = _appsetting.PublicIPAddress;
            string currentpublicIp = GetCurrentPublicIP();

            _logger.LogInformation(DateTime.Now + " updateDNS - Old IP: " + oldpublicIp + " current IP: " + currentpublicIp);

            if (String.IsNullOrEmpty(oldpublicIp))
            {
                _logger.LogInformation(DateTime.Now + " updateDNS - First execution detected. Old IP is Empty");
            }

            if (oldpublicIp != currentpublicIp)
            {
                _logger.LogInformation(DateTime.Now + " updateDNS - IP Address changed since last verification. Updating DNS Record");
                updateDNSARecord(_appsetting.AzureSettings.AzureDNSRecord, currentpublicIp);
                oldpublicIp = currentpublicIp;
                _logger.LogInformation(DateTime.Now + " updateDNS - Public IP address changed. Public IP is: " + currentpublicIp);
            }
            else
            {
                _logger.LogInformation(DateTime.Now + " updateDNS - Public IP address not changed since last verification. Public IP still: " + oldpublicIp);
            }
        }

        private string GetCurrentPublicIP()
        {
            _logger.LogInformation(DateTime.Now + " GetCurrentPublicIP - Checking current public IP address using Public IP provders list");
            List<string> publicIpProviders = _appsetting.PublicIPProviders;
            foreach (var ipProvider in publicIpProviders)
            {
                try
                {
                    _logger.LogInformation(DateTime.Now + " GetCurrentPublicIP -  Checking current public IP from Public IP Provider: " + ipProvider);
                    string publicIp = GetPublicIP(ipProvider);
                    return publicIp;
                }
                catch (Exception ex)
                {
                    _logger.LogError(" GetCurrentPublicIP - Failed to check current public IP from provider: " + ipProvider);
                    _logger.LogError(" GetCurrentPublicIP - Exception: " + ex.Message);
                    _logger.LogError(ex.StackTrace);
                }
            }
            throw new System.ArgumentException("GetCurrentPublicIP - Unable to get the current Public IP from the Public IP Providers list. Please check appsettings.json and update your PublicIPProviders list.");
        }

        private string GetPublicIP(string ipProvider)
        {
            httpClient.DefaultRequestHeaders.Add("User-Agent", "curl/7.9.8 (i686-pc-linux-gnu) libcurl 7.9.8 (OpenSSL 0.9.6b) (ipv6 enabled)");
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Content-Type", "application/json");
            var reshttpClient = httpClient.GetAsync(ipProvider).Result;
            reshttpClient.EnsureSuccessStatusCode();
            var reshttp = reshttpClient.Content.ReadAsStringAsync().Result;
            var publicIp = reshttp.Trim();
            IPAddress ipaddress;
            var checkIP = IPAddress.TryParse(publicIp, out ipaddress);

            if (checkIP)
            {
                return publicIp;
            }

            throw new System.ArgumentException("GetPublicIP - Unable to get the Public IP from the Public IP Provider " + ipProvider + ". Unable to parse the response to an valid IP Address. Response: " + reshttp);
        }

        public void updateDNSARecord(string recordName, string ipAddress)
        {
            _logger.LogInformation(DateTime.Now + " updateDNSARecord - Updating Azure DNS Zone with new Public IP " + ipAddress);
            _logger.LogInformation(DateTime.Now + " updateDNSARecord - Azure DNS Zone: " + _appsetting.AzureSettings.AzureDNSZone);
            _logger.LogInformation(DateTime.Now + " updateDNSARecord - Azure DNS A Record: " + _appsetting.AzureSettings.AzureDNSRecord);

            var azure = GetAzureCred();

            _logger.LogInformation(DateTime.Now + " updateDNSARecord - Validating Azure Resource Group: " + _appsetting.AzureSettings.AzureResourceGroup);
            var resourceGroup = azure.ResourceGroups.GetByName(_appsetting.AzureSettings.AzureResourceGroup);
            _logger.LogInformation(DateTime.Now + " updateDNSARecord - Azure Resource Group validated: " + resourceGroup.Id);

            _logger.LogInformation(DateTime.Now + " updateDNSARecord - Validating Azure DnsZone: " + _appsetting.AzureSettings.AzureDNSZone);
            var rootDnsZone = azure.DnsZones.GetByResourceGroup(resourceGroup.Name, _appsetting.AzureSettings.AzureDNSZone);
            _logger.LogInformation(DateTime.Now + " updateDNSARecord - Azure DnsZone validated: " + rootDnsZone.Id);

            if (CheckARecordExist(rootDnsZone))
            {
                _logger.LogInformation(DateTime.Now + " updateDNSARecord - Loading Azure DNS A Record");
                var aRecord = rootDnsZone.ARecordSets.GetByName(_appsetting.AzureSettings.AzureDNSRecord);

                _logger.LogInformation(DateTime.Now + " updateDNSARecord - Cleaning old Azure DNS A Record entry");
                foreach (var ip in aRecord.IPv4Addresses)
                {
                    rootDnsZone.Update().UpdateARecordSet(aRecord.Name).WithoutIPv4Address(ip);
                }
                rootDnsZone.Update().Apply();

                _logger.LogInformation(DateTime.Now + " updateDNSARecord - updating Azure DNS A Record with new public IP");
                rootDnsZone.Update().UpdateARecordSet(aRecord.Name).WithIPv4Address(ipAddress);
                rootDnsZone.Update().Apply();
            }
            else
            {
                _logger.LogInformation(DateTime.Now + " updateDNSARecord - Creating Azure DNS A Record since it does not exist");
                rootDnsZone.Update()
                    .DefineARecordSet(_appsetting.AzureSettings.AzureDNSRecord)
                    .WithIPv4Address(ipAddress)
                    .WithTimeToLive(0)
                    .Attach()
                    .Apply();
            }
        }

        private bool CheckARecordExist(IDnsZone rootDnsZone)
        {
            _logger.LogInformation(DateTime.Now + " CheckARecordExist - Checking if A Record \"" + _appsetting.AzureSettings.AzureDNSRecord + "\" exist at Azure DNS Zone: " + _appsetting.AzureSettings.AzureDNSZone);

            if (_checkARecordExist)
            {
                _logger.LogInformation(DateTime.Now + " CheckARecordExist - Azure DNS A Record found from a privous verification");
                return _checkARecordExist;
            }

            _logger.LogInformation(DateTime.Now + " CheckARecordExist - Loading Azure DNS A Record list");
            var aRecordSets = rootDnsZone
                    .ARecordSets
                    .List();

            foreach (var aRecordSet in aRecordSets)
            {
                if (_appsetting.AzureSettings.AzureDNSRecord == aRecordSet.Name)
                {
                    _logger.LogInformation(DateTime.Now + " CheckARecordExist - Azure DNS A Record found " + aRecordSet.Id);
                    _checkARecordExist = true;
                    return _checkARecordExist;
                }
            }
            _logger.LogInformation(DateTime.Now + " CheckARecordExist - Azure DNS A Record not found");
            return _checkARecordExist;
        }

        public IAzure GetAzureCred()
        {
            _logger.LogInformation(DateTime.Now + " GetAzureCred - Authenticating with Azure Resource Manager");
            _logger.LogInformation(DateTime.Now + " GetAzureCred - Azure AAD ClientID: " + _appsetting.AzureSettings.AzureAADClientID);
            _logger.LogInformation(DateTime.Now + " GetAzureCred - Azure AAD TenantID: " + _appsetting.AzureSettings.AzureAADTenantID);
            _logger.LogInformation(DateTime.Now + " GetAzureCred - Azure AAD SubscriptionID: " + _appsetting.AzureSettings.AzureSubscriptionID);

            cred = SdkContext.AzureCredentialsFactory.FromServicePrincipal(_appsetting.AzureSettings.AzureAADClientID, _appsetting.AzureSettings.AzureAADClientSecret, _appsetting.AzureSettings.AzureAADTenantID, AzureEnvironment.AzureGlobalCloud);

            var azure = Azure
                .Configure()
                .WithLogLevel(HttpLoggingDelegatingHandler.Level.Basic)
                .Authenticate(cred)
                .WithSubscription(_appsetting.AzureSettings.AzureSubscriptionID);

            return azure;
        }
        public void updateDNSbkp()
        {
            _logger.LogInformation("Hello World!");
            var cred = SdkContext.AzureCredentialsFactory.FromServicePrincipal("ecae79c9-b0b1-4ddf-bfbe-8518421bcfb8", "-&cO@}@ZRMRO&O%%D)HB)EufZ{b@d17;Bi/k|u+}]U?7a%|+)uJ", "victorhepoca.onmicrosoft.com", AzureEnvironment.AzureGlobalCloud);

            var azure = Azure
                .Configure()
                .WithLogLevel(HttpLoggingDelegatingHandler.Level.Basic)
                .Authenticate(cred)
                .WithDefaultSubscription();

            var resourceGroup = azure.ResourceGroups.GetByName("ExternalDNS");
            //var rootDnsZone = azure.DnsZones.Define("hepoca.com");
            var rootDnsZone = azure.DnsZones.GetByResourceGroup(resourceGroup.Name, "hepoca.com");

            var aRecord = rootDnsZone.ARecordSets.GetByName("victorcasa");

            _logger.LogInformation(aRecord.IPv4Addresses[0].ToString());

            // Add
            var resARecord = rootDnsZone.Update().UpdateARecordSet(aRecord.Name).WithIPv4Address("127.0.0.1");

            // Remove
            var resARecord2 = rootDnsZone.Update().UpdateARecordSet(aRecord.Name).WithoutIPv4Address("127.0.0.1");
            //var resARecord = rootDnsZone.Update().//(aRecord.Name).WithIPv4Address("127.0.0.1");
            var res = rootDnsZone.Update().Apply();

            _logger.LogInformation("Test Log");
        }

    }
}
