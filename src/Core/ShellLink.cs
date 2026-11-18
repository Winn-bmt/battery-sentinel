using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace BatterySentinel.Core
{
    /// <summary>shell32 的 IShellLink/IPersistFile：进程内创建 .lnk，不依赖 powershell。</summary>
    internal static class ShellLink
    {
        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        [ClassInterface(ClassInterfaceType.None)]
        private class CShellLink { }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("0000010b-0000-0000-C000-000000000046")]
        private interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            [PreserveSig]
            int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }

        public static bool Create(string lnkPath, string target, string arguments, string workingDir, string iconPath, string description)
        {
            string ignored;
            return Create(lnkPath, target, arguments, workingDir, iconPath, description, out ignored);
        }

        public static bool Create(string lnkPath, string target, string arguments, string workingDir, string iconPath, string description, out string error)
        {
            error = "";
            object link = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(lnkPath));
                link = new CShellLink();
                var shellLink = (IShellLinkW)link;
                shellLink.SetPath(target);
                if (!string.IsNullOrEmpty(arguments)) shellLink.SetArguments(arguments);
                if (!string.IsNullOrEmpty(workingDir)) shellLink.SetWorkingDirectory(workingDir);
                if (!string.IsNullOrEmpty(iconPath)) shellLink.SetIconLocation(iconPath, 0);
                if (!string.IsNullOrEmpty(description)) shellLink.SetDescription(description);
                ((IPersistFile)link).Save(lnkPath, true);
                if (!File.Exists(lnkPath))
                {
                    error = "Save 返回成功但文件不存在：" + lnkPath;
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = Describe(ex);
                return false;
            }
            finally
            {
                if (link != null)
                {
                    try { Marshal.FinalReleaseComObject(link); } catch { }
                }
            }
        }

        private static string Describe(Exception ex)
        {
            string text = ex.Message;
            if (ex.InnerException != null && !string.IsNullOrEmpty(ex.InnerException.Message))
                text += " / " + ex.InnerException.Message;
            return text.Trim();
        }

        public static string TargetOf(string lnkPath)
        {
            object link = null;
            try
            {
                link = new CShellLink();
                var persist = (IPersistFile)link;
                persist.Load(lnkPath, 0);
                var sb = new StringBuilder(1024);
                ((IShellLinkW)link).GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
                return sb.ToString();
            }
            catch
            {
                return "";
            }
            finally
            {
                if (link != null)
                {
                    try { Marshal.FinalReleaseComObject(link); } catch { }
                }
            }
        }
    }
}
