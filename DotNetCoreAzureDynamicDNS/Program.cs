using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.FileExtensions;
using Microsoft.Extensions.Configuration.Json;
using System.IO;

namespace DotNetCoreAzureDynamicDNS
{
    class Program
    {
        static void Main(string[] args)
        {
            var processname = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
            Log.Logger = new LoggerConfiguration()
                .WriteTo.File(processname+".log")
                .CreateLogger();

            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            var serviceProvider = serviceCollection.BuildServiceProvider();

            var logger = serviceProvider.GetService<ILogger<Program>>();
            logger.LogInformation(DateTime.Now + " App Started");


            var dynamicDNS = serviceProvider.GetService<DynamicDNS>();
            dynamicDNS.Run();

        }

        private static void ConfigureServices(IServiceCollection services)
        {
            services.AddLogging(configure =>
            {
                configure.AddConsole();
                configure.AddSerilog();
            });
            services.AddTransient<DynamicDNS>();
        }
    }
}
