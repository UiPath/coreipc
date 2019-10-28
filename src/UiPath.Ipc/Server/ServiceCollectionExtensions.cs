using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace UiPath.Ipc
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddIpc(this IServiceCollection services)
        {
            services.AddSingleton<ISerializer, JsonSerializer>();
            return services;
        }
    }
}
