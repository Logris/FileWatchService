﻿using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Management;
using System.Security;
using System.Security.Principal;
using System.Diagnostics;
using System.Reflection;
using System.Resources;
using System.Text;
using System.IO;

namespace MiracleAdmin
{ 
namespace Service
{ 
    public static class Reboot
    {
        //импортируем API функцию InitiateSystemShutdown
        [DllImport("advapi32.dll", EntryPoint = "InitiateSystemShutdownEx")]
        static extern int InitiateSystemShutdown(string lpMachineName, string lpMessage, int dwTimeout, bool bForceAppsClosed, bool bRebootAfterShutdown);
        //импортируем API функцию AdjustTokenPrivileges
        [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
        internal static extern bool AdjustTokenPrivileges(IntPtr htok, bool disall,
        ref TokPriv1Luid newst, int len, IntPtr prev, IntPtr relen);
        //импортируем API функцию GetCurrentProcess
        [DllImport("kernel32.dll", ExactSpelling = true)]
        internal static extern IntPtr GetCurrentProcess();
        //импортируем API функцию OpenProcessToken
        [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
        internal static extern bool OpenProcessToken(IntPtr h, int acc, ref IntPtr phtok);
        //импортируем API функцию LookupPrivilegeValue
        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool LookupPrivilegeValue(string host, string name, ref long pluid);
        //импортируем API функцию LockWorkStation
        [DllImport("user32.dll", EntryPoint = "LockWorkStation")]
        static extern bool LockWorkStation();
        //объявляем структуру TokPriv1Luid для работы с привилегиями
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct TokPriv1Luid
        {
            public int Count;
            public long Luid;
            public int Attr;
        }
        //объявляем необходимые, для API функций, константые значения, согласно MSDN
        internal const int SE_PRIVILEGE_ENABLED = 0x00000002;
        internal const int TOKEN_QUERY = 0x00000008;
        internal const int TOKEN_ADJUST_PRIVILEGES = 0x00000020;
        internal const string SE_SHUTDOWN_NAME = "SeShutdownPrivilege";
        //функция SetPriv для повышения привилегий процесса
        private static void SetPriv()
        {
            TokPriv1Luid tkp; //экземпляр структуры TokPriv1Luid 
            IntPtr htok = IntPtr.Zero;
            //открываем "интерфейс" доступа для своего процесса
            if (OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, ref htok))
            {
                //заполняем поля структуры
                tkp.Count = 1;
                tkp.Attr = SE_PRIVILEGE_ENABLED;
                tkp.Luid = 0;
                //получаем системный идентификатор необходимой нам привилегии
                LookupPrivilegeValue(null, SE_SHUTDOWN_NAME, ref tkp.Luid);
                //повышем привилигеию своему процессу
                AdjustTokenPrivileges(htok, false, ref tkp, 0, IntPtr.Zero, IntPtr.Zero);
            }
        }
        //публичный метод для перезагрузки/выключения машины
        public static int Halt(bool reboot, bool Force)
        {
            SetPriv(); //получаем привилегия
            //вызываем функцию InitiateSystemShutdown, передавая ей необходимые параметры
            return InitiateSystemShutdown(null, null, 0, Force, reboot);
        }
        //публичный метод для блокировки операционной системы
        public static int Lock()
        {
            if (LockWorkStation())
                return 1;
            else
                return 0;
        }
    }

    public class LaunchInDiffSess
    {
        [StructLayout(LayoutKind.Sequential)]
        struct STARTUPINFO
        {
            public Int32 cb;
            public String lpReserved;
            public String lpDesktop;
            public String lpTitle;
            public UInt32 dwX;
            public UInt32 dwY;
            public UInt32 dwXSize;
            public UInt32 dwYSize;
            public UInt32 dwXCountChars;
            public UInt32 dwYCountChars;
            public UInt32 dwFillAttribute;
            public UInt32 dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public UInt32 dwProcessId;
            public UInt32 dwThreadId;
        }

        const UInt32 WTSConnectState = 8;

        [Flags]
        enum WTS_CONNECTSTATE_CLASS
        {
            WTSActive,
            WTSConnected,
            WTSConnectQuery,
            WTSShadow,
            WTSDisconnected,
            WTSIdle,
            WTSListen,
            WTSReset,
            WTSDown,
            WTSInit
        }

        [DllImport("kernel32.dll")]
        public static extern int FormatMessage(
            int Flags, IntPtr Source, int MessageID, int LanguageID,
            StringBuilder Buffer, int Size, IntPtr Args);

        public static string GetErrorMessage(int ErrorCode)
        {
            var buf = new StringBuilder(256);
            int len = FormatMessage(0x1200, IntPtr.Zero,
                ErrorCode, 0, buf, buf.Capacity, IntPtr.Zero);
            if (len <= 0) return "";
            int k = buf.Length - 1;
            for (; k > 0; k--)
            {
                char u = buf[k];
                if (u > ' ' && u != '.') break;
            }
            buf.Length = k + 1;
            buf.Append('.');
            return buf.ToString();
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll")]
        public static extern Int32 WTSGetActiveConsoleSessionId();

        [DllImport("Wtsapi32.dll", SetLastError = true)]
        static extern bool WTSQueryUserToken(Int32 SessionId, ref IntPtr hToken);

        [DllImport("Wtsapi32.dll", SetLastError = true)]
        static extern bool WTSQuerySessionInformation(
            IntPtr hServer, Int32 SessionId, UInt32 WTSInfoClass,
            out IntPtr ppBuffer, out Int32 BytesReturned);

        [DllImport("Wtsapi32.dll")]
        static extern void WTSFreeMemory(IntPtr pBuf);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool CreateProcessAsUser(
            IntPtr hToken, String lpApplicationName, String lpCommandLine,
            IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
            bool bInheritHandle, UInt32 dwCreationFlags, IntPtr lpEnvironment,
            String lpCurrentDirectory, ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32", SetLastError = true)]
        static extern bool OpenProcessToken(
            IntPtr ProcessHandle, Int32 DesiredAccess, ref IntPtr TokenHandle);

        [DllImport("userenv.dll", SetLastError = true)]
        static extern bool CreateEnvironmentBlock(
            ref IntPtr lpEnvironment, IntPtr hToken, Boolean bInherit);

        [DllImport("userenv.dll", SetLastError = true)]
        static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int SendMessage(IntPtr hwnd, int wMsg, int wParam, int lParam);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool SetTokenInformation(
            IntPtr TokenHandle, UInt32 TokenInformationClass,
            ref Int32 TokenInformation, Int32 TokenInformationLength);

        private static int RunInteractiveProcess(Int32 SessId, string FileName, string Args, bool AsSystem, ref StreamWriter log)
        {
            var userToken = IntPtr.Zero; ;
            var Environment = IntPtr.Zero;
            try
            {
                int result;
                if (AsSystem)
                {
                    if (!OpenProcessToken(GetCurrentProcess(), 0x2000000, ref userToken))
                    {
                        return Marshal.GetLastWin32Error();
                    }
                    if (!SetTokenInformation(userToken, 12, ref SessId, Marshal.SizeOf(SessId)))
                    {
                        return Marshal.GetLastWin32Error();
                    }
                }
                else
                {
                    if (!WTSQueryUserToken(SessId, ref userToken))
                    {
                        result = Marshal.GetLastWin32Error();

                        return result;
                    }
                    if (!CreateEnvironmentBlock(ref Environment, userToken, false))
                        return Marshal.GetLastWin32Error();
                }

                var SI = new STARTUPINFO();
                SI.cb = Marshal.SizeOf(SI);
                if (AsSystem) SI.lpDesktop = @"WinSta0\Default";
                UInt32 ctorFlags = 0;
                if (Environment != IntPtr.Zero) ctorFlags |= 0x400;
                var PI = new PROCESS_INFORMATION();

                if (CreateProcessAsUser(userToken,
                    FileName, Args, IntPtr.Zero, IntPtr.Zero, false,
                    ctorFlags, Environment, null, ref SI, out PI))
                {
                    CloseHandle(PI.hProcess);
                    CloseHandle(PI.hThread);
                }
                else
                    return Marshal.GetLastWin32Error();
            }
            catch (Exception ex)
            {
                log.WriteLine(ex.Message);
                if (userToken != IntPtr.Zero) CloseHandle(userToken);
                if (Environment != IntPtr.Zero) DestroyEnvironmentBlock(Environment);
            }
            return 0;
        }

        public static void Execute(string exe_name, string cmd_params, ref StreamWriter log)
        {
            var SessId = WTSGetActiveConsoleSessionId();

            int R;
            IntPtr pBuf = new IntPtr();
            Int32 bufLen;
            if (!WTSQuerySessionInformation(IntPtr.Zero, SessId, WTSConnectState, out pBuf, out bufLen))
            {
                R = Marshal.GetLastWin32Error();
                string s = string.Format(
                    "Query session information failed({0}: {1}", R, GetErrorMessage(R));

                return;
            }

            WTS_CONNECTSTATE_CLASS state;
            unsafe
            {
                state = *(WTS_CONNECTSTATE_CLASS*)(pBuf);
            }
            WTSFreeMemory(pBuf);

            if (state != WTS_CONNECTSTATE_CLASS.WTSActive)
            {
                string s =
                    string.Format("Console session is inactive. Current state = {0}", state);
                return;
            }

            RunInteractiveProcess(SessId, exe_name, cmd_params, false, ref log);
        }
    }
}
}