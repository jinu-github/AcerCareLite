using AcerCareLite.Core.Battery;
using AcerCareLite.Core.Hardware;
using AcerCareLite.Core.Monitoring;
using AcerCareLite.Core.Settings;
using AcerCareLite.Core.Utilities;
using AcerCareLite.Windows.Monitoring;
using Microsoft.Extensions.DependencyInjection;

namespace AcerCareLite.Windows;

public static class WindowsMonitoringServiceCollectionExtensions
{
    public static IServiceCollection AddWindowsMonitoring(this IServiceCollection services)
    {
        services.AddSingleton<ICpuMonitor, CpuMonitor>();
        services.AddSingleton<IGpuMonitor, GpuMonitor>();
        services.AddSingleton<IMemoryMonitor, MemoryMonitor>();
        services.AddSingleton<IStorageMonitor, StorageMonitor>();
        services.AddSingleton<IPowerMonitor, PowerMonitor>();
        services.AddSingleton<ISystemInfoProvider, SystemInfoProvider>();
        services.AddSingleton<IBatteryReader, BatteryReader>();
        services.AddSingleton<IHardwareInfoProvider, HardwareInfoProvider>();
        services.AddSingleton<IStartupAppService, StartupAppService>();
        services.AddSingleton<IRecycleBinService, RecycleBinService>();
        services.AddSingleton<IAutoStartService, AutoStartService>();
        return services;
    }
}
