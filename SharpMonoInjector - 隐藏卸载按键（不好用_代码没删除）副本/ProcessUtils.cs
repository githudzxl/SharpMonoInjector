using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace SharpMonoInjector
{
    public class ProcessItem
    {
        public string ProcessName { get; set; }
        public int Id { get; set; }
        public string DisplayText => $"{ProcessName} (PID: {Id})";

        public ProcessItem(string processName, int id)
        {
            ProcessName = processName;
            Id = id;
        }
    }

    public static class ProcessUtils
    {
        public static List<ProcessItem> GetMonoProcesses()
        {
            List<ProcessItem> result = new List<ProcessItem>();
            var allProcesses = Process.GetProcesses()
                .Where(p => !string.IsNullOrEmpty(p.ProcessName))
                .OrderBy(p => p.ProcessName)
                .ToList();

            foreach (var process in allProcesses)
            {
                IntPtr processHandle = IntPtr.Zero;
                try
                {
                    processHandle = Native.OpenProcess(
                        ProcessAccessRights.PROCESS_QUERY_INFORMATION | ProcessAccessRights.PROCESS_VM_READ,
                        false, process.Id);

                    if (processHandle == IntPtr.Zero)
                        continue;

                    if (GetMonoModule(processHandle, out _))
                    {
                        result.Add(new ProcessItem(process.ProcessName, process.Id));
                    }
                }
                catch
                {
                    continue;
                }
                finally
                {
                    if (processHandle != IntPtr.Zero)
                        Native.CloseHandle(processHandle);
                }
            }

            return result;
        }

        // 【修复】CS1626：先收集到列表，再在try-catch外yield return
        public static IEnumerable<ExportedFunction> GetExportedFunctions(IntPtr handle, IntPtr mod)
        {
            List<ExportedFunction> results = new List<ExportedFunction>();

            try
            {
                using (Memory memory = new Memory(handle))
                {
                    int e_lfanew = memory.ReadInt(mod + 0x3C);
                    IntPtr ntHeaders = mod + e_lfanew;
                    IntPtr optionalHeader = ntHeaders + 0x18;
                    IntPtr dataDirectory = optionalHeader + (Is64BitProcess(handle) ? 0x70 : 0x60);
                    IntPtr exportDirectory = mod + memory.ReadInt(dataDirectory);
                    IntPtr names = mod + memory.ReadInt(exportDirectory + 0x20);
                    IntPtr ordinals = mod + memory.ReadInt(exportDirectory + 0x24);
                    IntPtr functions = mod + memory.ReadInt(exportDirectory + 0x1C);
                    int count = memory.ReadInt(exportDirectory + 0x18);

                    for (int i = 0; i < count; i++)
                    {
                        int offset = memory.ReadInt(names + i * 4);
                        string name = memory.ReadString(mod + offset, 256, Encoding.ASCII);
                        short ordinal = memory.ReadShort(ordinals + i * 2);
                        IntPtr address = mod + memory.ReadInt(functions + ordinal * 4);

                        if (address != IntPtr.Zero)
                        {
                            // 先添加到临时列表，不直接yield return
                            results.Add(new ExportedFunction(name, address));
                        }
                    }
                }
            }
            catch
            {
                // 出错时清空列表，确保不返回部分结果
                results.Clear();
            }

            // 在try-catch块外面进行yield return
            foreach (var func in results)
            {
                yield return func;
            }
        }

        public static bool GetMonoModule(IntPtr handle, out IntPtr monoModule)
        {
            int size = Is64BitProcess(handle) ? 8 : 4;
            IntPtr[] ptrs = Array.Empty<IntPtr>();

            try
            {
                if (!Native.EnumProcessModulesEx(
                    handle, ptrs, 0, out int bytesNeeded, ModuleFilter.LIST_MODULES_ALL))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                int count = bytesNeeded / size;
                ptrs = new IntPtr[count];

                if (!Native.EnumProcessModulesEx(
                    handle, ptrs, bytesNeeded, out bytesNeeded, ModuleFilter.LIST_MODULES_ALL))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            catch (Exception ex)
            {
                throw new InjectorException("Failed to enumerate process modules", ex);
            }

            for (int i = 0; i < ptrs.Length; i++)
            {
                try
                {
                    StringBuilder path = new StringBuilder(260);
                    Native.GetModuleFileNameEx(handle, ptrs[i], path, 260);
                    string moduleName = path.ToString().ToLower();

                    if (moduleName.Contains("mono"))
                    {
                        if (!Native.GetModuleInformation(handle, ptrs[i], out MODULEINFO info, (uint)(size * ptrs.Length)))
                            continue;

                        var funcs = GetExportedFunctions(handle, info.lpBaseOfDll);
                        if (funcs.Any(f => f.Name == "mono_get_root_domain"))
                        {
                            monoModule = info.lpBaseOfDll;
                            return true;
                        }
                    }
                }
                catch
                {
                    continue;
                }
            }

            monoModule = IntPtr.Zero;
            return false;
        }

        public static bool Is64BitProcess(IntPtr handle)
        {
            if (!Environment.Is64BitOperatingSystem)
                return false;

            if (!Native.IsWow64Process(handle, out bool isWow64))
                return IntPtr.Size == 8;

            return !isWow64;
        }
    }
}