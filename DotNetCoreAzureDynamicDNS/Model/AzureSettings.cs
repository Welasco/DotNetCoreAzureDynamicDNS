using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetCoreAzureDynamicDNS.Model
{
    class AzureSettings
    {
        public string AzureSubscriptionID { get; set; }
        public string AzureResourceGroup { get; set; }
        public string AzureDNSZone { get; set; }
        public string AzureDNSRecord { get; set; }
        public string AzureAADClientID { get; set; }
        public string AzureAADClientSecret { get; set; }
        public string AzureAADTenantID { get; set; }
    }
}
