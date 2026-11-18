// ---------------------------------------------------------------------------
//  Native.cs —— Windows 底层 P/Invoke 声明（setupapi / hid / kernel32 / dwmapi）
//
//  只依赖 Windows 自带 DLL，不含任何第三方库。
//  所有结构体严格按 Win32 头文件顺序排列（含对齐/填充字段），
//  这样 Marshal.SizeOf / PtrToStructure 得到的偏移与 C 端一致。
// ---------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

namespace BatterySentinel.Hid
{
    // =======================================================================
    //  结构体
    // =======================================================================

    /// <summary>SP_DEVICE_INTERFACE_DATA。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    /// <summary>SP_DEVINFO_DATA。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    /// <summary>DEVPROPKEY（= PROPERTYKEY 的 DEVPROP 版本）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    /// <summary>OVERLAPPED（异步 I/O 用；ctypes/Marshal 里都没有现成的）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct OVERLAPPED
    {
        public IntPtr Internal;
        public IntPtr InternalHigh;
        public uint Offset;
        public uint OffsetHigh;
        public IntPtr hEvent;
    }

    /// <summary>HIDD_ATTRIBUTES。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES
    {
        public uint Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    /// <summary>
    /// HIDP_CAPS。
    /// 注意 Reserved[17]：这里展开成 17 个字段，避免 ByValArray 在不同
    /// marshaler 下的额外折算，保证与 C 端 sizeof 完全一致（64 位下 66 字节）。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;

        // USHORT Reserved[17]
        public ushort R00, R01, R02, R03, R04, R05, R06, R07, R08,
                      R09, R10, R11, R12, R13, R14, R15, R16;

        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    /// <summary>
    /// HIDP_VALUE_CAPS。
    /// 末尾的 union 展开成最大成员（Range / NotRange 都是 16 字节）；
    /// 其中 UsageMin 与 NotRange.Usage 在同一个偏移（union 起点）上，
    /// 所以读取单个 usage 时用 UsageMin 字段即可。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_VALUE_CAPS
    {
        public ushort UsagePage;
        public byte ReportID;
        public byte IsAlias;
        public ushort BitField;
        public ushort LinkCollection;
        public ushort LinkUsage;
        public ushort LinkUsagePage;
        public byte IsRange;
        public byte IsStringRange;
        public byte IsDesignatorRange;
        public byte IsAbsolute;
        public byte HasNull;
        public byte Reserved;
        public ushort BitSize;
        public ushort ReportCount;

        // USHORT Reserved2[5]
        public ushort R20, R21, R22, R23, R24;

        public uint UnitsExp;
        public uint Units;
        public int LogicalMin;
        public int LogicalMax;
        public int PhysicalMin;
        public int PhysicalMax;

        // union { Range{UsageMin,UsageMax,...}; NotRange{Usage,Reserved1,...} }
        public ushort UsageMin;     // == NotRange.Usage
        public ushort UsageMax;     // == NotRange.Reserved1
        public ushort StringMin;
        public ushort StringMax;
        public ushort DesignatorMin;
        public ushort DesignatorMax;
        public ushort DataIndexMin;
        public ushort DataIndexMax;

        /// <summary>该能力项对应的 usage（NotRange 语义）。</summary>
        public int Usage { get { return UsageMin; } }
    }

    // =======================================================================
    //  常量
    // =======================================================================

    internal static class Win32
    {
        // ---- setupapi ----
        public const uint DIGCF_PRESENT = 0x00000002;
        public const uint DIGCF_DEVICEINTERFACE = 0x00000010;
        public const uint DIGCF_ALLCLASSES = 0x00000004;

        // ---- 设备注册表属性 ----
        public const uint SPDRP_DEVICEDESC = 0x00000000;
        public const uint SPDRP_FRIENDLYNAME = 0x0000000C;

        // ---- CreateFile ----
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 0x00000001;
        public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint OPEN_EXISTING = 3;
        public const uint FILE_FLAG_OVERLAPPED = 0x40000000;

        // ---- 错误码 ----
        public const int ERROR_IO_PENDING = 997;
        public const int ERROR_NOT_FOUND = 1168;
        public const int ERROR_ACCESS_DENIED = 5;
        public const int ERROR_INVALID_PARAMETER = 87;

        // ---- 等待 ----
        public const uint WAIT_OBJECT_0 = 0x00000000;
        public const uint WAIT_TIMEOUT = 0x00000102;
        public const uint WAIT_FAILED = 0xFFFFFFFF;

        // ---- HID ----
        public const int HidP_Input = 0;
        public const int HidP_Output = 1;
        public const int HidP_Feature = 2;

        public const int HIDP_STATUS_SUCCESS = 0x00110000;

        // ---- DEVPROP ----
        public const uint DEVPROP_TYPE_BYTE = 0x00000003;

        /// <summary>无效句柄 (HANDLE)-1。</summary>
        public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
    }

    // =======================================================================
    //  P/Invoke
    // =======================================================================

    internal static class Native
    {
        // ---------------------------------------------------------------- setupapi
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true,
            EntryPoint = "SetupDiGetClassDevsW")]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid,
            string enumerator, IntPtr hwndParent, uint flags);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true,
            EntryPoint = "SetupDiGetClassDevsW")]
        public static extern IntPtr SetupDiGetClassDevsNoClass(IntPtr classGuid,
            string enumerator, IntPtr hwndParent, uint flags);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr hDevInfo,
            IntPtr devInfo, ref Guid interfaceClassGuid, uint memberIndex,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr hDevInfo,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData,
            uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInfo(IntPtr hDevInfo, uint memberIndex,
            ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true,
            EntryPoint = "SetupDiGetDeviceRegistryPropertyW")]
        public static extern bool SetupDiGetDeviceRegistryProperty(IntPtr hDevInfo,
            ref SP_DEVINFO_DATA deviceInfoData, uint property, out uint propertyRegDataType,
            IntPtr propertyBuffer, uint propertyBufferSize, out uint requiredSize);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiGetDeviceInstanceId(IntPtr hDevInfo,
            ref SP_DEVINFO_DATA deviceInfoData, IntPtr deviceInstanceId,
            uint deviceInstanceIdSize, out uint requiredSize);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true,
            EntryPoint = "SetupDiGetDevicePropertyW")]
        public static extern bool SetupDiGetDeviceProperty(IntPtr hDevInfo,
            ref SP_DEVINFO_DATA deviceInfoData, ref DEVPROPKEY propertyKey,
            out uint propertyType, IntPtr propertyBuffer, uint propertyBufferSize,
            out uint requiredSize, uint flags);

        // ---------------------------------------------------------------- kernel32
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true,
            EntryPoint = "CreateFileW")]
        public static extern IntPtr CreateFile(string fileName, uint desiredAccess,
            uint shareMode, IntPtr securityAttributes, uint creationDisposition,
            uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(IntPtr hFile, IntPtr lpBuffer, uint bytesToRead,
            IntPtr lpNumberOfBytesRead, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(IntPtr hFile, IntPtr lpBuffer, uint bytesToWrite,
            out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true,
            EntryPoint = "CreateEventW")]
        public static extern IntPtr CreateEvent(IntPtr eventAttributes, bool manualReset,
            bool initialState, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ResetEvent(IntPtr hEvent);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(IntPtr hHandle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetOverlappedResult(IntPtr hFile, IntPtr lpOverlapped,
            out uint numberOfBytesTransferred, bool wait);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CancelIoEx(IntPtr hFile, IntPtr lpOverlapped);

        // ---------------------------------------------------------------- hid
        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetAttributes(IntPtr hidDeviceObject,
            out HIDD_ATTRIBUTES attributes);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetPreparsedData(IntPtr hidDeviceObject,
            out IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern int HidP_GetValueCaps(int reportType, IntPtr valueCaps,
            ref ushort valueCapsLength, IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern int HidP_GetUsageValue(int reportType, ushort usagePage,
            ushort linkCollection, ushort usage, out uint usageValue,
            IntPtr preparsedData, IntPtr report, uint reportLength);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetFeature(IntPtr hidDeviceObject, IntPtr reportBuffer,
            uint reportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_SetFeature(IntPtr hidDeviceObject, IntPtr reportBuffer,
            uint reportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_SetOutputReport(IntPtr hidDeviceObject,
            IntPtr reportBuffer, uint reportBufferLength);

        [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool HidD_GetProductString(IntPtr hidDeviceObject,
            IntPtr buffer, uint bufferLength);

        [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool HidD_GetManufacturerString(IntPtr hidDeviceObject,
            IntPtr buffer, uint bufferLength);

        [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool HidD_GetSerialNumberString(IntPtr hidDeviceObject,
            IntPtr buffer, uint bufferLength);
    }

    // =======================================================================
    //  已知 GUID
    // =======================================================================

    internal static class KnownGuids
    {
        /// <summary>HID 设备接口类 {4D1E55B2-F16F-11CF-88CB-001111000030}。</summary>
        public static readonly Guid HidDeviceInterface =
            new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");

        /// <summary>蓝牙类 {e0cbf06c-cd8b-4647-bb8a-263b43f0f974}。</summary>
        public static readonly Guid BluetoothClass =
            new Guid("E0CBF06C-CD8B-4647-BB8A-263B43F0F974");

        /// <summary>系统类 {4d36e97d-e325-11ce-bfc1-08002be10318}（HFP 免提节点在这里）。</summary>
        public static readonly Guid SystemClass =
            new Guid("4D36E97D-E325-11CE-BFC1-08002BE10318");

        /// <summary>蓝牙电量属性 fmtid：{104EA319-6EE2-4701-BD47-8DDBF425BBE5}。</summary>
        public static readonly Guid BluetoothBatteryProperty =
            new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5");

        /// <summary>蓝牙电量属性 pid（DEVPROP_TYPE_BYTE，值为 0-100）。</summary>
        public const uint BluetoothBatteryPid = 2;
    }

    // =======================================================================
    //  dwmapi —— 仅给 UI 层做深色标题栏用（HID 层自己不用）
    // =======================================================================

    public static class Dwm
    {
        /// <summary>DWMWA_USE_IMMERSIVE_DARK_MODE（Win10 20H1+）。</summary>
        public const int WindowUseImmersiveDarkMode = 20;

        /// <summary>旧版编号（Win10 1809 ~ 1909）。</summary>
        public const int WindowUseImmersiveDarkModeLegacy = 19;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute,
            ref int attributeValue, int attributeSize);

        /// <summary>给窗口标题栏设置深色/浅色（失败返回 false，不抛异常）。</summary>
        public static bool SetDarkTitleBar(IntPtr hwnd, bool dark)
        {
            if (hwnd == IntPtr.Zero) return false;
            int value = dark ? 1 : 0;
            try
            {
                int hr = DwmSetWindowAttribute(hwnd, WindowUseImmersiveDarkMode,
                    ref value, sizeof(int));
                if (hr != 0)
                {
                    hr = DwmSetWindowAttribute(hwnd, WindowUseImmersiveDarkModeLegacy,
                        ref value, sizeof(int));
                }
                return hr == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
