using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace DEM.Net.Extension.VisualTopo
{
    public static class ServiceCollectionExtension
    {
        public static IServiceCollection AddDemNetVisualTopoExtension(this IServiceCollection services)
        {
            services.AddScoped<VisualTopoService>();

            return services;
        }
    }
}
